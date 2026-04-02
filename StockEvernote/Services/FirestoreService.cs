using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using StockEvernote.Contracts;
using StockEvernote.Data;
using StockEvernote.Model;
using StockEvernote.Model.Firestore;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Policy;

namespace StockEvernote.Services;

public class FirestoreService : IFirestoreService
{
    private readonly HttpClient _httpClient;
    private readonly EvernoteDbContext _dbContext;
    private readonly IUserSession _userSession;
    private readonly string _projectId;
    private readonly ILogger<FirestoreService> _logger;
    private const string BaseUrl = "https://firestore.googleapis.com/v1";
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(5, 5);

    public FirestoreService(
        HttpClient httpClient,
        EvernoteDbContext dbContext,
        IUserSession userSession,
        IConfiguration configuration,
        ILogger<FirestoreService> logger)
    {
        _httpClient = httpClient;
        _dbContext = dbContext;
        _userSession = userSession;
        _logger = logger;
        _projectId = configuration["Firebase:ProjectId"]
            ?? throw new InvalidOperationException("找不到 Firebase:ProjectId");
    }

    /// <summary>
    /// 執行全量雲端同步作業，依序將本地端未同步的筆記本與筆記推送至 Firestore。
    /// </summary>
    /// <remarks>
    /// 同步順序具有嚴格相依性：必須先確保筆記本 (Notebook) 實體存在於雲端，才能推送其轄下的筆記 (Note)。
    /// 資料來源直接依賴本地 SQLite 資料庫，而非 UI 層的資料綁定集合，以確保背景同步的絕對完整性。
    /// </remarks>
    /// <param name="userId">當前經授權驗證的使用者唯一識別碼 (Firebase UID)</param>
    /// <returns>代表非同步操作的 Task，執行完畢即代表單向 (本地推至雲端) 同步完成</returns>
    /// <exception cref="System.Net.Http.HttpRequestException">網路異常或 Firestore 伺服器拒絕連線時拋出</exception>
    public async Task SyncAllAsync(string userId)
    {
        _logger.LogInformation("開始全量同步，UserId：{UserId}", userId);

        // 1. 確保外鍵關聯的父實體 (Notebook) 優先上傳，避免 Firestore 寫入孤兒筆記
        await SyncNotebooksAsync(userId);

        // 2. 直接查詢實體資料庫取得所有活躍的 NotebookId
        // 防坑 (Why)：嚴禁使用 ViewModel.Notebooks，防止 UI 尚未渲染或資料過濾導致同步清單缺漏
        var allNotebookIds = await _dbContext.Notebooks
            .Where(n => n.UserId == userId && n.IsDeleted == false)
            .Select(n => n.Id)
            .ToListAsync();

        // 3. 遍歷所有有效筆記本，批次執行筆記實體的 Upsert 或 Delete 操作
        foreach (var notebookId in allNotebookIds)
        {
            await SyncNotesAsync(notebookId, userId);
        }

        _logger.LogInformation("全量同步完成，共同步 {Count} 本筆記本", allNotebookIds.Count);
    }

    // ===== Notebook 同步 =====

    public async Task SyncNotebooksAsync(string userId)
    {
        // 1. 撈出本地未同步的 Notebook
        var unsynced = await _dbContext.Notebooks
            .Where(n => n.UserId == userId && n.IsSynced == false)
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

                notebook.IsSynced = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "筆記本同步失敗：{Name}", notebook.Name);
            }
            finally
            {
                _semaphore.Release();
            }
        });
        await Task.WhenAll(syncTasks);
        await _dbContext.SaveChangesAsync();
    }
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

        var request = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = JsonContent.Create(body)
        };

        request.Headers.Authorization =
           new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _userSession.IdToken);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Notebook 寫入失敗：{Status} - {Error}", response.StatusCode, error);
            throw new Exception($"Notebook 寫入失敗：{response.StatusCode} - {error}");
        }
    }

    // ===== Note 同步 =====

    public async Task SyncNotesAsync(string notebookId, string userId)
    {
        var unsynced = await _dbContext.Notes
            .Where(n => n.NotebookId == notebookId && n.IsSynced == false)
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

                note.IsSynced = true;

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "筆記同步失敗：{Name}", note.Name);
            }
            finally
            {
                _semaphore.Release();
            }
        });

        await Task.WhenAll(syncTakes);
        await _dbContext.SaveChangesAsync();
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

        var request = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _userSession.IdToken);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Note 寫入失敗：{Status} - {Error}", response.StatusCode, error);
            throw new Exception($"Note 寫入失敗：{response.StatusCode} - {error}");
        }
    }

    // ===== 共用刪除 =====

    private async Task DeleteFirestoreDocumentAsync(string path)
    {
        _logger.LogInformation("刪除雲端文件：{Path}", path);

        var url = $"{BaseUrl}/projects/{_projectId}/databases/(default)/documents/{path}";

        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _userSession.IdToken);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("刪除失敗：{Status} - {Error}", response.StatusCode, error);
            throw new Exception($"刪除失敗：{response.StatusCode} - {error}");
        }
    }

    // ==========================================
    // ⬇️ 從雲端還原
    // ==========================================

    public async Task RestoreFromCloudAsync(string userId)
    {
        _logger.LogInformation("開始從雲端還原，UserId：{UserId}", userId);

        var strategy = _dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // 1. 拉取並還原筆記本
                var notebookResponse = await FetchDocumentsAsync($"users/{userId}/notebooks");
                var restoredNotebookIds = await RestoreNotebooksAsync(userId, notebookResponse);

                // 2. 併發拉取所有筆記本的筆記
                var fetchTasks = restoredNotebookIds.Select(async notebookId =>
                {
                    var noteResponse = await FetchDocumentsAsync(
                        $"users/{userId}/notebooks/{notebookId}/notes");

                    if (noteResponse?.Documents is null)
                        return Enumerable.Empty<(string, FirestoreDocument)>();

                    return noteResponse.Documents.Select(doc => (notebookId, doc));
                });

                var fetchResults = await Task.WhenAll(fetchTasks);

                var allNoteDocs = fetchResults
                    .SelectMany(x => x)
                    .ToList();

                // 3. 還原所有筆記
                await RestoreNotesAsync(allNoteDocs);

                // 4. 全部成功才存檔和 Commit
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

    // 共用：拉取 Firestore 文件列表
    private async Task<FirestoreListResponse?> FetchDocumentsAsync(string path)
    {
        var url = $"{BaseUrl}/projects/{_projectId}/databases/(default)/documents/{path}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _userSession.IdToken);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("拉取雲端文件失敗，Path：{Path}，Status：{Status}", path, response.StatusCode);

            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        return System.Text.Json.JsonSerializer.Deserialize<FirestoreListResponse>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    // 還原筆記本，回傳雲端所有 NotebookId 供後續使用
    private async Task<List<string>> RestoreNotebooksAsync(string userId, FirestoreListResponse? result)
    {
        _logger.LogInformation("雲端筆記本文件數量：{Count}", result?.Documents?.Count ?? 0);

        if (result?.Documents is null) return new List<string>();

        var cloudDocs = result.Documents
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

    // 還原筆記
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

        // 1. 查出本地未刪除的已存在 Id
        var existingIds = new HashSet<string>();
        foreach (var chunk in cloudIds.Chunk(500))
        {
            var idsInChunk = await _dbContext.Notes
                .Where(n => chunk.Contains(n.Id) && n.IsDeleted == false)
                .Select(n => n.Id)
                .ToListAsync();
            existingIds.UnionWith(idsInChunk);
        }

        // 2. 找出軟刪除的，把它們還原
        var maybeDeletedIds = cloudIds
            .Where(id => !existingIds.Contains(id))
            .ToList();

        var softDeleted = await _dbContext.Notes
            .Where(n => maybeDeletedIds.Contains(n.Id) && n.IsDeleted == true)
            .ToListAsync();

        foreach (var note in softDeleted)
        {
            note.IsDeleted = false;
            note.IsSynced = true;
            _logger.LogInformation("還原軟刪除筆記：{Name}", note.Name);
        }

        // 3. 查出資料庫完全沒有的
        var allExistingIds = new HashSet<string>();
        foreach (var chunk in cloudIds.Chunk(500))
        {
            var idsInChunk = await _dbContext.Notes
                .Where(n => chunk.Contains(n.Id))
                .Select(n => n.Id)
                .ToListAsync();
            allExistingIds.UnionWith(idsInChunk);
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
}

    