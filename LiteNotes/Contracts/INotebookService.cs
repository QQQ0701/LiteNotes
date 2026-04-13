using LiteNotes.Model;

namespace LiteNotes.Contracts;

public interface INotebookService
{
    Task<List<Notebook>> GetNotebooksAsync(string userId);
    Task<Notebook> CreateNotebookAsync(string name ,string userId);
    Task<Notebook> UpdateNotebookAsync(Notebook notebook);
    Task DeleteNotebookAsync(string notebookId);
}
