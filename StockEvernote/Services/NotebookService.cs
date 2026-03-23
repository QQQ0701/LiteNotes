using Microsoft.EntityFrameworkCore;
using StockEvernote.Contracts;
using StockEvernote.Data;
using StockEvernote.Model;

namespace StockEvernote.Services;
public class NotebookService : INotebookService
{
    private readonly EvernoteDbContext _dbContext;
    public NotebookService(EvernoteDbContext dbContext)
    {
        _dbContext = dbContext;
    }
    public async Task<Notebook> CreateNotebookAsync(string name, string userId)
    {
        var newnotebook = new Notebook()
        {
            Name = name,
            UserId = userId,
            IsSynced = false,   // 預設還沒同步到雲端
            IsDeleted = false
        };

        _dbContext.Notebooks.Add(newnotebook);
        await _dbContext.SaveChangesAsync();

        return newnotebook;
    }
    public async Task<List<Notebook>> GetNotebooksAsync(string userId)
    {
        return await _dbContext.Notebooks
                        .Where(n => n.UserId == userId && n.IsDeleted == false) // 💡 順便過濾掉被軟刪除的
                        .OrderByDescending(n => n.CreatedAt) // 💡 貼心小設計：最新的排在最上面
                        .ToListAsync();
    }
    public async Task<Notebook> UpdateNotebookAsync(Notebook notebook)
    {
        var existing = await _dbContext.Notebooks.FindAsync(notebook.Id)
            ?? throw new KeyNotFoundException($"找不到 Notebook Id：{notebook.Id}");

        existing.Name = notebook.Name;
        existing.IsSynced = false; // 修改後標記為未同步
        existing.UpdatedAt = DateTime.Now;
        await _dbContext.SaveChangesAsync();
        return existing;
    }

    public async Task DeleteNotebookAsync(string notebookId)
    {
        var existing = await _dbContext.Notebooks.FindAsync(notebookId)
            ?? throw new KeyNotFoundException($"找不到 Notebook Id：{notebookId}");

        existing.UpdatedAt = DateTime.Now;
        existing.IsDeleted = true;  // 軟刪除，配合你原本的設計
        existing.IsSynced = false;  // 標記為未同步，等待雲端同步刪除

        await _dbContext.SaveChangesAsync();
    }
}
