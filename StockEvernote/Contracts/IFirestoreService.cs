namespace StockEvernote.Contracts;

public interface IFirestoreService
{
    Task SyncNotebooksAsync(string userId);
    Task SyncNotesAsync(string notebookId, string userId);
    Task SyncAllAsync(string userId);
}