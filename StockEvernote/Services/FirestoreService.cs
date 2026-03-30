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

public class FirestoreService : IFirestoreService
{
    private readonly HttpClient _httpClient;
    private readonly EvernoteDbContext _dbContext;
    private readonly IUserSession _userSession;
    private readonly string _projectId;
    private readonly ILogger<FirestoreService> _logger;
    private const string BaseUrl = "https://firestore.googleapis.com/v1";

    public FirestoreService(HttpClient httpClient,
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
    public async Task SyncAllAsync(string userId)
    {
        _logger.LogInformation("開始全量同步，UserId：{UserId}", userId);

        // 1. 同步所有筆記本
        await SyncNotebooksAsync(userId);

        // 2. 從資料庫撈所有筆記本 Id，不依賴畫面集合
        var allNotebookIds = await _dbContext.Notebooks
            .Where(n => n.UserId == userId && n.IsDeleted == false)
            .Select(n => n.Id)
            .ToListAsync();

        // 3. 對每本筆記本同步 Note
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

        foreach (var notebook in unsynced)
        {
            if (notebook.IsDeleted)
                await DeleteFirestoreDocumentAsync($"users/{userId}/notebooks/{notebook.Id}");
            else
                await UpsertNotebookAsync(userId, notebook);

            // 2. 標記為已同步
            notebook.IsSynced = true;
        }
        await _dbContext.SaveChangesAsync();
    }

    private async Task UpsertNotebookAsync(string userId, Notebook notebook)
    {
        SetAuthHeader(); // ✅ 設定 Bearer Token
        _logger.LogInformation("上傳筆記本：{Name}", notebook.Name);

        var url = $"{BaseUrl}/projects/{_projectId}/databases/(default)/documents/users/{userId}/notebooks/{notebook.Id}";

        var body = new
        {
            fields = new Dictionary<string, object>
            {
                ["name"] = new { stringValue = notebook.Name },
                ["userId"] = new { stringValue = notebook.UserId },
                ["isDeleted"] = new { booleanValue = notebook.IsDeleted },
                ["createdAt"] = new { timestampValue = notebook.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                ["updatedAt"] = new { timestampValue = notebook.UpdatedAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") }
            }
        };

        var response = await _httpClient.PatchAsJsonAsync(url, body);
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

        foreach (var note in unsynced)
        {
            if (note.IsDeleted)
                await DeleteFirestoreDocumentAsync($"users/{userId}/notebooks/{notebookId}/notes/{note.Id}");
            else
                await UpsertNoteAsync(userId, notebookId, note);

            note.IsSynced = true;
        }

        await _dbContext.SaveChangesAsync();
    }

    private async Task UpsertNoteAsync(string userId, string notebookId, Note note)
    {
        SetAuthHeader(); 
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
                ["createdAt"] = new { timestampValue = note.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                ["updatedAt"] = new { timestampValue = note.UpdatedAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") }
            }
        };

        var response = await _httpClient.PatchAsJsonAsync(url, body);
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
        SetAuthHeader();
        _logger.LogInformation("刪除雲端文件：{Path}", path);

        var url = $"{BaseUrl}/projects/{_projectId}/databases/(default)/documents/{path}";
        var response = await _httpClient.DeleteAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("刪除失敗：{Status} - {Error}", response.StatusCode, error); 
            throw new Exception($"刪除失敗：{response.StatusCode} - {error}");
        }
    }
    private void SetAuthHeader()
    {
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _userSession.IdToken);
    }
}