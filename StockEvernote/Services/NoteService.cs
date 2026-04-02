using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockEvernote.Contracts;
using StockEvernote.Data;
using StockEvernote.Model;

namespace StockEvernote.Services;

/// <summary>
/// 筆記服務實作：透過 EF Core 操作筆記資料表，採用軟刪除機制。
/// </summary>
public class NoteService : INoteService
{
    private readonly EvernoteDbContext _dbContext;
    private readonly ILogger<NoteService> _logger;

    public NoteService(EvernoteDbContext dbContext, ILogger<NoteService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }
    public async Task<List<Note>> GetNotesAsync(string notebookId)
    {
        var result = await _dbContext.Notes
           .Where(n => n.NotebookId == notebookId && n.IsDeleted == false)
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
    public async Task DeleteNoteAsync(string noteId)
    {
        var existing = await _dbContext.Notes.FindAsync(noteId)
           ?? throw new KeyNotFoundException($"找不到 Note Id：{noteId}");

        existing.IsDeleted = true;  // 軟刪除，配合你原本的設計
        existing.IsSynced = false;  // 標記為未同步，等待雲端同步刪除
        existing.UpdatedAt = DateTime.Now;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("軟刪除筆記：{Name}，Id：{Id}", existing.Name, existing.Id);
    }
}