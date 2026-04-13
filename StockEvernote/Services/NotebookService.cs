using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockEvernote.Contracts;
using StockEvernote.Data;
using StockEvernote.Model;

namespace StockEvernote.Services;
/// <summary>
/// 筆記本服務：CRUD 操作 + 刪除時透過 NoteService 觸發連鎖刪除（筆記 → 附件 → 索引）。
/// </summary>
public class NotebookService : INotebookService
{
    private readonly EvernoteDbContext _dbContext;
    private readonly INoteService _noteService;
    private readonly ILogger<NotebookService> _logger;
    public NotebookService(
        EvernoteDbContext dbContext,
        INoteService noteService,
        ISearchService searchService,
        ILogger<NotebookService> logger)
    {
        _dbContext = dbContext;
        _noteService = noteService;
        _logger = logger;
    }
    public async Task<Notebook> CreateNotebookAsync(string name, string userId)
    {
        var newnotebook = new Notebook()
        {
            Name = name,
            UserId = userId,
            IsSynced = false,
            IsDeleted = false
        };

        _dbContext.Notebooks.Add(newnotebook);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("建立筆記本：{Name}，Id：{Id}", name, newnotebook.Id);
        return newnotebook;
    }
    public async Task<List<Notebook>> GetNotebooksAsync(string userId)
    {
        var result = await _dbContext.Notebooks
                        .Where(n => n.UserId == userId && !n.IsDeleted)
                        .OrderByDescending(n => n.CreatedAt)
                        .ToListAsync();

        _logger.LogDebug("查詢筆記本，UserId：{UserId}，共 {Count} 本", userId, result.Count);
        return result;
    }
    public async Task<Notebook> UpdateNotebookAsync(Notebook notebook)
    {
        var existing = await _dbContext.Notebooks.FindAsync(notebook.Id)
            ?? throw new KeyNotFoundException($"找不到 Notebook Id：{notebook.Id}");

        existing.Name = notebook.Name;
        existing.IsSynced = false;
        existing.UpdatedAt = DateTime.Now;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("更新筆記本：{Name}，Id：{Id}", existing.Name, existing.Id);
        return existing;
    }
    /// <summary>
    /// 刪除筆記本：軟刪除筆記本本體 → 逐筆呼叫 NoteService.DeleteNoteAsync 觸發連鎖
    /// （每篇筆記會自動連鎖軟刪除附件 + 清理搜尋索引）。
    /// </summary>
    public async Task DeleteNotebookAsync(string notebookId)
    {
        var existing = await _dbContext.Notebooks.FindAsync(notebookId)
            ?? throw new KeyNotFoundException($"找不到 Notebook Id：{notebookId}");

        existing.UpdatedAt = DateTime.Now;
        existing.IsDeleted = true;
        existing.IsSynced = false;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("軟刪除筆記本：{Name}，Id：{Id}", existing.Name, existing.Id);

        try
        {
            var childNotes = await _dbContext.Notes
                .Where(n => n.NotebookId == notebookId && !n.IsDeleted)
                .ToListAsync();

            foreach (var note in childNotes)
                await _noteService.DeleteNoteAsync(note.Id);

            _logger.LogInformation("連鎖刪除筆記 {Count} 篇，NotebookId：{NotebookId}",
                childNotes.Count, notebookId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "刪除筆記本時，連鎖刪除筆記發生錯誤");
        }

    }
}
