using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StockEvernote.Contracts;
using StockEvernote.Data;
using StockEvernote.Model;
using StockEvernote.Model.Firestore;
using System.Net.Http;
using System.Net.Http.Json;

namespace StockEvernote.Services;

/// <summary>
/// 負責本地 SQLite 與 Firestore 之間的雙向資料同步，
/// 以及裝置換機時的雲端還原流程。
/// </summary>
/// <remarks>
/// Firestore 路徑設計採扁平結構：
///   users/{userId}/notebooks/{notebookId}
///   users/{userId}/notes/{noteId}         （筆記本路徑嚴格嵌套）
///   users/{userId}/attachments/{attachmentId}  （扁平，透過欄位關聯 noteId）
///
/// 並發控制：所有對外 HTTP 請求透過 <see cref="_semaphore"/>（上限 5）節流，
/// EF Core DbContext 操作則集中在非並發的主執行緒完成。
/// </remarks>
public class FirestoreService : IFirestoreService
{
    private readonly HttpClient _httpClient;
    private readonly EvernoteDbContext _dbContext;
    private readonly IUserSession _userSession;
    private readonly ILogger<FirestoreService> _logger;
    private readonly IFileUploadService _fileUploadService;
    private readonly string _projectId;

    private const string BaseUrl = "https://firestore.googleapis.com/v1";
    /// <summary>限制最大同時對外 HTTP 請求數，避免觸發 Firestore 速率限制。</summary>
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(5, 5);
    public FirestoreService(
        HttpClient httpClient,
        EvernoteDbContext dbContext,
        IUserSession userSession,
        IConfiguration configuration,
        ILogger<FirestoreService> logger,
        IFileUploadService fileUploadService)
    {
        _httpClient = httpClient;
        _dbContext = dbContext;
        _userSession = userSession;
        _fileUploadService = fileUploadService;
        _logger = logger;
        _projectId = configuration["Firebase:ProjectId"]
            ?? throw new InvalidOperationException("找不到 Firebase:ProjectId");
    }

    // ══════════════════════════════════════════════════════════
    // 上傳雲端
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// 將所有本地未同步的資料推送至 Firestore。
    /// 同步順序：筆記本 → 筆記（依賴筆記本路徑）→ 附件。
    /// </summary>
    public async Task SyncAllAsync(string userId)
    {
        _logger.LogInformation("開始全量同步，UserId：{UserId}", userId);

        await SyncNotebooksAsync(userId);

        var allNotebookIds = await _dbContext.Notebooks
                 .Where(n => n.UserId == userId && !n.IsDeleted)
                 .Select(n => n.Id)
                 .ToListAsync();

        foreach (var notebookId in allNotebookIds)
            await SyncNotesAsync(notebookId, userId);

        await SyncAttachmentsAsync(userId);

        _logger.LogInformation("全量同步完成，共同步 {Count} 本筆記本", allNotebookIds.Count);
    }
    /// <summary>將本地未同步的筆記本推送（新增 / 更新 / 軟刪除）至 Firestore。</summary>
    public async Task SyncNotebooksAsync(string userId)
    {
        var unsynced = await _dbContext.Notebooks
            .Where(n => n.UserId == userId && !n.IsSynced)
            .ToListAsync();

        _logger.LogInformation("待同步筆記本數量：{Count}", unsynced.Count);

        if (!unsynced.Any()) return;

        var syncTasks = unsynced.Select(async notebook =>
        {
            await _semaphore.WaitAsync();
            try
            {
                if (notebook.IsDeleted)
                    await DeleteFirestoreDocumentAsync($"users/{userId}/notebooks/{notebook.Id}");
                else
                    await UpsertNotebookAsync(userId, notebook);

                return (notebook, success: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "筆記本同步失敗：{Name}", notebook.Name);
                return (notebook, success: false);
            }
            finally
            {
                _semaphore.Release();
            }
        });
        var results = await Task.WhenAll(syncTasks);

        foreach (var (notebook, success) in results.Where(r => r.success))
            notebook.IsSynced = true;

        await _dbContext.SaveChangesAsync();
    }

    /// <summary>將指定筆記本下未同步的筆記推送至 Firestore。</summary>
    public async Task SyncNotesAsync(string notebookId, string userId)
    {
        var unsynced = await _dbContext.Notes
            .Where(n => n.NotebookId == notebookId && !n.IsSynced)
            .ToListAsync();

        _logger.LogInformation("待同步筆記數量：{Count}，NotebookId：{NotebookId}", unsynced.Count, notebookId);

        if (!unsynced.Any()) return;

        var syncTakes = unsynced.Select(async note =>
        {
            await _semaphore.WaitAsync();

            try
            {
                if (note.IsDeleted)
                    await DeleteFirestoreDocumentAsync($"users/{userId}/notebooks/{notebookId}/notes/{note.Id}");
                else
                    await UpsertNoteAsync(userId, notebookId, note);

                return (note, success: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "筆記同步失敗：{Name}", note.Name);
                return (note, success: false);
            }
            finally
            {
                _semaphore.Release();
            }
        });

        var results = await Task.WhenAll(syncTakes);

        foreach (var (note, success) in results.Where(r => r.success))
            note.IsSynced = true;

        await _dbContext.SaveChangesAsync();
    }

    /// <summary>將本地未同步的附件推送（新增 / 刪除）至 Firestore，刪除時同步清除 Azure Blob。</summary>
    public async Task SyncAttachmentsAsync(string userId)
    {
        var unsynced = await _dbContext.Attachments
            .Where(a => a.UserId == userId && !a.IsSynced)
            .ToListAsync();

        _logger.LogInformation("待同步附件數量：{Count}", unsynced.Count);

        if (!unsynced.Any()) return;

        var syncTasks = unsynced.Select(async attachment =>
        {
            await _semaphore.WaitAsync();
            try
            {
                if (attachment.IsDeleted)
                {
                    await DeleteFirestoreDocumentAsync($"users/{userId}/attachments/{attachment.Id}");
                    await DeleteBlobIfExistsAsync(attachment.BlobName);
                }
                else
                {
                    await UpsertAttachmentAsync(userId, attachment);
                }

                return (attachment, success: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "附件同步失敗：{FileName}", attachment.FileName);
                return (attachment, success: false);
            }
            finally
            {
                _semaphore.Release();
            }
        });

        var results = await Task.WhenAll(syncTasks);

        foreach (var (attachment, success) in results.Where(r => r.success))
            attachment.IsSynced = true;

        await _dbContext.SaveChangesAsync();
    }

    // ══════════════════════════════════════════════════════════
    // 雲端下載
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// 從 Firestore 拉取全部資料還原至本地 SQLite。
    /// 還原順序：筆記本 → 筆記 → 附件，整體包在單一 Transaction 內，
    /// 任一步驟失敗即 Rollback。
    /// </summary>
    public async Task RestoreFromCloudAsync(string userId)
    {
        _logger.LogInformation("開始從雲端還原，UserId：{UserId}", userId);

        var strategy = _dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // 1. 還原筆記本，取得所有 notebookId 供後續步驟使用
                var notebookDocs = await FetchDocumentsAsync($"users/{userId}/notebooks");
                var restoredNotebookIds = await RestoreNotebooksAsync(userId, notebookDocs);

                // 2. 併發拉取所有筆記本的筆記
                var fetchTasks = restoredNotebookIds.Select(async notebookId =>
                {
                    await _semaphore.WaitAsync();
                    try
                    {
                        var noteResponse = await FetchDocumentsAsync(
                            $"users/{userId}/notebooks/{notebookId}/notes");

                        return noteResponse.Select(doc => (notebookId, doc));
                    }
                    finally
                    {
                        _semaphore.Release();
                    }
                });

                var allNoteDocs = (await Task.WhenAll(fetchTasks))
                    .SelectMany(x => x)
                    .ToList();

                await RestoreNotesAsync(allNoteDocs);
                await RestoreAttachmentsAsync(userId);
                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("從雲端還原成功");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "從雲端還原失敗，已 Rollback");
                throw;
            }
        });
    }

    // ══════════════════════════════════════════════════════════
    // 私有：Firestore Upsert
    // ══════════════════════════════════════════════════════════
    private async Task UpsertNotebookAsync(string userId, Notebook notebook)
    {
        _logger.LogInformation("上傳筆記本：{Name}", notebook.Name);

        var url = $"{BaseUrl}/projects/{_projectId}/databases/(default)/documents/users/{userId}/notebooks/{notebook.Id}";

        var body = new
        {
            fields = new Dictionary<string, object>
            {
                ["name"] = new { stringValue = notebook.Name },
                ["userId"] = new { stringValue = notebook.UserId },
                ["isDeleted"] = new { booleanValue = notebook.IsDeleted },
                ["createdAt"] = new { timestampValue = notebook.CreatedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                ["updatedAt"] = new { timestampValue = notebook.UpdatedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ") }
            }
        };

        await SendPatchAsync(url, body);
    }

    private async Task UpsertNoteAsync(string userId, string notebookId, Note note)
    {
        _logger.LogInformation("上傳筆記：{Name}", note.Name);

        var url = $"{BaseUrl}/projects/{_projectId}/databases/(default)/documents/users/{userId}/notebooks/{notebookId}/notes/{note.Id}";
        var body = new
        {
            fields = new Dictionary<string, object>
            {
                ["name"] = new { stringValue = note.Name },
                ["content"] = new { stringValue = note.Content ?? string.Empty },
                ["notebookId"] = new { stringValue = note.NotebookId },
                ["isDeleted"] = new { booleanValue = note.IsDeleted },
                ["createdAt"] = new { timestampValue = note.CreatedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                ["updatedAt"] = new { timestampValue = note.UpdatedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ") }
            }
        };

        await SendPatchAsync(url, body);
    }

    private async Task UpsertAttachmentAsync(string userId, Attachment attachment)
    {
        _logger.LogInformation("上傳附件記錄：{FileName}", attachment.FileName);

        var url = $"{BaseUrl}/projects/{_projectId}/databases/(default)/documents/users/{userId}/attachments/{attachment.Id}";
        var body = new
        {
            fields = new Dictionary<string, object>
            {
                ["fileName"] = new { stringValue = attachment.FileName },
                ["blobUrl"] = new { stringValue = attachment.BlobUrl },
                ["blobName"] = new { stringValue = attachment.BlobName },
                ["fileSize"] = new { integerValue = attachment.FileSize.ToString() },
                ["contentType"] = new { stringValue = attachment.ContentType },
                ["isImage"] = new { booleanValue = attachment.IsImage },
                ["noteId"] = new { stringValue = attachment.NoteId },
                ["userId"] = new { stringValue = attachment.UserId },
                ["isDeleted"] = new { booleanValue = attachment.IsDeleted },
                ["createdAt"] = new { timestampValue = attachment.CreatedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                ["updatedAt"] = new { timestampValue = attachment.UpdatedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ") }
            }
        };

        await SendPatchAsync(url, body);
    }

    // ══════════════════════════════════════════════════════════
    // 私有：Firestore 還原
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// 還原筆記本。對每筆雲端記錄執行：
    /// 1. 本地已存在且未刪除 → 跳過
    /// 2. 本地軟刪除 → 還原（IsDeleted = false）
    /// 3. 本地完全不存在 → AddRange 新增
    /// </summary>
    /// <returns>雲端所有 NotebookId，供後續拉取筆記使用。</returns>
    private async Task<List<string>> RestoreNotebooksAsync(string userId, List<FirestoreDocument> documents)
    {
        _logger.LogInformation("雲端筆記本文件數量：{Count}", documents.Count);
        if (!documents.Any()) return new List<string>();

        var cloudDocs = documents
            .Where(d => d.Fields != null)
            .Select(d => new
            {
                Id = d.Name?.Split('/').Last() ?? Guid.NewGuid().ToString(),
                Name = d.Fields!.GetValueOrDefault("name")?.StringValue ?? "未命名"
            })
            .ToList();

        _logger.LogInformation("解析出筆記本數量：{Count}", cloudDocs.Count);

        var cloudIds = cloudDocs.Select(d => d.Id).ToList();

        // 1. 查出本地未刪除的已存在 Id
        // ✅ Chunking 防止 SQLite 參數上限
        var existingIds = new HashSet<string>();
        foreach (var chunk in cloudIds.Chunk(500))
        {
            var idsInChunk = await _dbContext.Notebooks
                .Where(n => chunk.Contains(n.Id) && n.IsDeleted == false)
                .Select(n => n.Id)
                .ToListAsync();
            existingIds.UnionWith(idsInChunk);
        }

        _logger.LogInformation("本地已存在筆記本數量：{Count}", existingIds.Count);

        // 2. 找出雲端有但本地是軟刪除的，把它們還原
        var maybeDeletedIds = cloudIds
            .Where(id => !existingIds.Contains(id))
            .ToList();

        var softDeleted = await _dbContext.Notebooks
            .Where(n => maybeDeletedIds.Contains(n.Id) && n.IsDeleted == true)
            .ToListAsync();

        foreach (var nb in softDeleted)
        {
            nb.IsDeleted = false;
            nb.IsSynced = true;
            _logger.LogInformation("還原軟刪除筆記本：{Name}", nb.Name);
        }

        // 3. 查出資料庫完全沒有的（才需要 AddRange）
        var allExistingIds = new HashSet<string>();
        foreach (var chunk in cloudIds.Chunk(500))
        {
            var idsInChunk = await _dbContext.Notebooks
                .Where(n => chunk.Contains(n.Id))
                .Select(n => n.Id)
                .ToListAsync();
            allExistingIds.UnionWith(idsInChunk);
        }

        var newNotebooks = cloudDocs
            .Where(d => !allExistingIds.Contains(d.Id))
            .Select(d => new Notebook
            {
                Id = d.Id,
                UserId = userId,
                Name = d.Name,
                IsSynced = true,
                IsDeleted = false,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            })
            .ToList();

        if (newNotebooks.Any())
        {
            await _dbContext.Notebooks.AddRangeAsync(newNotebooks);
            _logger.LogInformation("還原筆記本數量：{Count}", newNotebooks.Count);
        }

        return cloudIds;
    }
    /// <summary>還原筆記，邏輯同 <see cref="RestoreNotebooksAsync"/>。</summary>
    private async Task RestoreNotesAsync(List<(string NotebookId, FirestoreDocument Doc)> allDocs)
    {
        if (!allDocs.Any()) return;

        var cloudNotes = allDocs
            .Where(x => x.Doc.Fields != null)
            .Select(x => new
            {
                Id = x.Doc.Name?.Split('/').Last() ?? Guid.NewGuid().ToString(),
                x.NotebookId,
                Name = x.Doc.Fields!.GetValueOrDefault("name")?.StringValue ?? "未命名",
                Content = x.Doc.Fields!.GetValueOrDefault("content")?.StringValue ?? string.Empty
            })
            .ToList();

        var cloudIds = cloudNotes.Select(n => n.Id).ToList();

        var existingMap = new Dictionary<string, bool>();

        foreach (var chunk in cloudIds.Chunk(500))
        {
            var rows = await _dbContext.Notes
                .Where(n => chunk.Contains(n.Id))
                .Select(n => new { n.Id, n.IsDeleted })
                .ToListAsync();

            foreach (var r in rows)
                existingMap[r.Id] = r.IsDeleted;
        }

        // 在記憶體裡分類，不需要第二次查詢
        var softDeletedIds = existingMap
            .Where(kv => kv.Value == true)
            .Select(kv => kv.Key)
            .ToHashSet();

        var allExistingIds = existingMap.Keys.ToHashSet();

        // 還原軟刪除
        var softDeleted = await _dbContext.Notes
            .Where(n => softDeletedIds.Contains(n.Id))
            .ToListAsync();

        foreach (var note in softDeleted)
        {
            note.IsDeleted = false;
            note.IsSynced = true;
            _logger.LogInformation("還原軟刪除筆記：{Name}", note.Name);
        }

        var newNotes = cloudNotes
            .Where(n => !allExistingIds.Contains(n.Id))
            .Select(n => new Note
            {
                Id = n.Id,
                NotebookId = n.NotebookId,
                Name = n.Name,
                Content = n.Content,
                IsSynced = true,
                IsDeleted = false,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            })
            .ToList();

        if (newNotes.Any())
        {
            await _dbContext.Notes.AddRangeAsync(newNotes);
            _logger.LogInformation("新增筆記數量：{Count}", newNotes.Count);
        }
        _logger.LogInformation("還原筆記完成，軟刪除還原：{Restored} 篇，新增：{New} 篇",
        softDeleted.Count, newNotes.Count);
    }
    /// <summary>還原附件，路徑採扁平結構，透過欄位 noteId 關聯筆記。</summary>
    private async Task RestoreAttachmentsAsync(string userId)
    {
        var documents = await FetchDocumentsAsync($"users/{userId}/attachments");
        if (!documents.Any()) return;

        var cloudAttachments = documents
            .Where(d => d.Fields != null)
            .Select(d => new
            {
                Id = d.Name?.Split('/').Last() ?? Guid.NewGuid().ToString(),
                FileName = d.Fields!.GetValueOrDefault("fileName")?.StringValue ?? "未命名",
                BlobUrl = d.Fields!.GetValueOrDefault("blobUrl")?.StringValue ?? string.Empty,
                BlobName = d.Fields!.GetValueOrDefault("blobName")?.StringValue ?? string.Empty,
                FileSize = long.TryParse(d.Fields!.GetValueOrDefault("fileSize")?.IntegerValue, out var size) ? size : 0,
                ContentType = d.Fields!.GetValueOrDefault("contentType")?.StringValue ?? string.Empty,
                IsImage = d.Fields!.GetValueOrDefault("isImage")?.BooleanValue ?? false,
                NoteId = d.Fields!.GetValueOrDefault("noteId")?.StringValue ?? string.Empty,
                UserId = userId
            })
           .ToList();

        var cloudIds = cloudAttachments.Select(a => a.Id).ToList();

        // 查出本地已存在的

        var existingMap = new Dictionary<string, bool>(); // id -> IsDeleted
        foreach (var chunk in cloudIds.Chunk(500))
        {
            var rows = await _dbContext.Attachments
                .Where(a => chunk.Contains(a.Id))
                .Select(a => new { a.Id, a.IsDeleted })
                .ToListAsync();
            foreach (var r in rows)
                existingMap[r.Id] = r.IsDeleted;
        }

        // 在記憶體裡分類，不需要第二次查詢
        var softDeletedIds = existingMap
            .Where(kv => kv.Value == true)
            .Select(kv => kv.Key)
            .ToHashSet();

        var allExistingIds = existingMap.Keys.ToHashSet();

        // 還原軟刪除
        var softDeleted = await _dbContext.Attachments
            .Where(a => softDeletedIds.Contains(a.Id))
            .ToListAsync();

        foreach (var att in softDeleted)
        {
            att.IsDeleted = false;
            att.IsSynced = true;
            _logger.LogInformation("還原軟刪除附件：{FileName}", att.FileName);
        }

        // 新增本地完全沒有的
        var newAttachments = cloudAttachments
            .Where(a => !allExistingIds.Contains(a.Id))
            .Select(a => new Attachment
            {
                Id = a.Id,
                NoteId = a.NoteId,
                UserId = a.UserId,
                FileName = a.FileName,
                BlobUrl = a.BlobUrl,
                BlobName = a.BlobName,
                FileSize = a.FileSize,
                ContentType = a.ContentType,
                IsImage = a.IsImage,
                IsSynced = true,
                IsDeleted = false,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            })
            .ToList();

        if (newAttachments.Any())
        {
            await _dbContext.Attachments.AddRangeAsync(newAttachments);
            _logger.LogInformation("還原附件數量：{Count}", newAttachments.Count);
        }
    }


    // ══════════════════════════════════════════════════════════
    // 私有：HTTP 工具
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// 拉取 Firestore 集合下的所有文件，自動處理分頁（pageToken）。
    /// 每次最多請求 300 筆以最大化單次命中率。
    /// </summary>
    private async Task<List<FirestoreDocument>> FetchDocumentsAsync(string path)
    {
        var allDocuments = new List<FirestoreDocument>();
        string? pageToken = null;

        do
        {
            var url = $"{BaseUrl}/projects/{_projectId}/databases/(default)/documents/{path}?pageSize=300";
            if (pageToken != null)
                url += $"&pageToken={pageToken}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = BearerToken();

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("拉取雲端文件失敗，Path：{Path}，Status：{Status}", path, response.StatusCode);
                break;
            }

            var result = await DeserializeResponseAsync<FirestoreListResponse>(response);
            if (result?.Documents != null)
                allDocuments.AddRange(result.Documents);

            pageToken = result?.NextPageToken;
        }
        while (pageToken != null);

        return allDocuments;
    }
    /// <summary>以 PATCH 方式新增或覆寫 Firestore 文件。</summary>
    private async Task SendPatchAsync(string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = BearerToken();

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Firestore 寫入失敗：{Status} - {Error}", response.StatusCode, error);
            throw new Exception($"Firestore 寫入失敗：{response.StatusCode} - {error}");
        }
    }
    /// <summary>刪除指定路徑的 Firestore 文件。</summary>
    private async Task DeleteFirestoreDocumentAsync(string path)
    {
        _logger.LogInformation("刪除雲端文件：{Path}", path);

        var url = $"{BaseUrl}/projects/{_projectId}/databases/(default)/documents/{path}";
        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization = BearerToken();

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("刪除失敗：{Status} - {Error}", response.StatusCode, error);
            throw new Exception($"刪除失敗：{response.StatusCode} - {error}");
        }
    }
    /// <summary>刪除 Azure Blob，失敗時僅記錄警告，不中斷同步流程。</summary>
    private async Task DeleteBlobIfExistsAsync(string blobName)
    {
        try
        {
            await _fileUploadService.DeleteBlobAsync(blobName);
            _logger.LogInformation("同步刪除 Azure Blob：{BlobName}", blobName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "同步時刪除 Blob 失敗：{BlobName}", blobName);
        }
    }

    // ══════════════════════════════════════════════════════════
    // 私有：共用工具
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// 批次查詢本地 DB，建立 id → IsDeleted 的映射表。
    /// 使用 Chunk(500) 避免 SQLite IN 子句參數上限。
    /// </summary>
    private async Task<Dictionary<string, bool>> BuildExistingMapAsync<T>(
        List<string> ids,
        Func<IEnumerable<string>, IQueryable<T>> queryBuilder)
        where T : class
    {
        var map = new Dictionary<string, bool>();

        foreach (var chunk in ids.Chunk(500))
        {
            // 利用反射取得匿名型別的 Id / IsDeleted 欄位
            var rows = await queryBuilder(chunk)
                .Cast<dynamic>()
                .ToListAsync();

            foreach (var r in rows)
                map[(string)r.Id] = (bool)r.IsDeleted;
        }

        return map;
    }

    /// <summary>產生帶有目前使用者 IdToken 的 Bearer Authorization header。</summary>
    private System.Net.Http.Headers.AuthenticationHeaderValue BearerToken() =>
        new("Bearer", _userSession.IdToken);

    /// <summary>將 HTTP 回應 JSON 反序列化為指定型別。</summary>
    private static async Task<T?> DeserializeResponseAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return System.Text.Json.JsonSerializer.Deserialize<T>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }
}

