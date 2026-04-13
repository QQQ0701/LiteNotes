using LiteNotes.Model;

namespace LiteNotes.Contracts;

/// <summary>
/// 筆記服務介面：負責筆記的查詢、建立、更新與軟刪除。
/// </summary>
public interface INoteService
{
    Task<List<Note>> GetNotesAsync(string notebookId);
    Task<Note> CreateNoteAsync(string notebookId, string name);
    Task<Note> UpdateNoteAsync(Note note);
    Task DeleteNoteAsync(string noteId);
}
