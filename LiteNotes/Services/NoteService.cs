using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using LiteNotes.Contracts;
using LiteNotes.Data;
using LiteNotes.Model;

namespace LiteNotes.Services;

/// <summary>
/// 筆記服務：CRUD 操作 + 刪除時連鎖軟刪除附件與清理搜尋索引。
/// </summary>
public class NoteService : INoteService
{
    private readonly EvernoteDbContext _dbContext;
    private readonly ISearchService _searchService;
    private readonly ILogger<NoteService> _logger;

    public NoteService(
        EvernoteDbContext dbContext,
        ISearchService searchService,
        ILogger<NoteService> logger)
    {
        _dbContext = dbContext;
        _searchService = searchService;
        _logger = logger;
    }
    public async Task<List<Note>> GetNotesAsync(string notebookId)
    {
        var result = await _dbContext.Notes
           .Where(n => n.NotebookId == notebookId && !n.IsDeleted)
            .OrderByDescending(n => n.UpdatedAt)
            .ToListAsync();

        _logger.LogDebug("查詢筆記，NotebookId：{NotebookId}，共 {Count} 篇", notebookId, result.Count);
        return result;
    }
    public async Task<Note> CreateNoteAsync(string name, string notebookId)
    {
        var newNote = new Note()
        {
            NotebookId = notebookId,
            Name = name,
            Content = string.Empty,
            IsSynced = false,
            IsDeleted = false
        };

        _dbContext.Notes.Add(newNote);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("建立筆記：{Name}，Id：{Id}，NotebookId：{NotebookId}", name, newNote.Id, notebookId);
        return newNote;
    }

    public async Task<Note> UpdateNoteAsync(Note note)
    {
        var existing = await _dbContext.Notes.FindAsync(note.Id)
            ?? throw new KeyNotFoundException($"找不到 Notebook Id：{note.Id}");

        existing.Name = note.Name;
        existing.Content = note.Content ?? string.Empty;
        existing.IsSynced = false;
        existing.UpdatedAt = DateTime.Now;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("更新筆記：{Name}，Id：{Id}", existing.Name, existing.Id);
        return existing;
    }

    /// <summary>
    /// 刪除筆記：軟刪除筆記本體 → 連鎖軟刪除所有附件 → 清理搜尋索引。
    /// 附件和索引為次要操作，失敗不影響筆記本體的刪除。
    /// </summary>
    public async Task DeleteNoteAsync(string noteId)
    {
        var existing = await _dbContext.Notes.FindAsync(noteId)
           ?? throw new KeyNotFoundException($"找不到 Note Id：{noteId}");

        existing.IsDeleted = true;  
        existing.IsSynced = false;  
        existing.UpdatedAt = DateTime.Now;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("軟刪除筆記：{Name}，Id：{Id}", existing.Name, existing.Id);

        try
        {
            var attachments = await _dbContext.Attachments
                .Where(a => a.NoteId == noteId && !a.IsDeleted)
                .ToListAsync();

            foreach (var att in attachments)
            {
                att.IsDeleted = true;
                att.IsSynced = false;
                att.UpdatedAt = DateTime.Now;
            }

            if (attachments.Any())
            {
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("連鎖軟刪除附件 {Count} 筆，NoteId：{NoteId}",
                    attachments.Count, noteId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "刪除筆記時，連鎖軟刪除附件發生錯誤");
        }

        try
        {
            await _searchService.RemoveNoteIndexAsync(noteId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "刪除筆記時，清理搜尋索引發生錯誤");
        }
    }
}