using System.ComponentModel.DataAnnotations;

namespace StockEvernote.Model;
public class Note
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string NotebookId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // 🌟 2. 離線同步機制
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public bool IsSynced { get; set; } = false;
    public bool IsDeleted { get; set; } = false; // 放進資源回收筒

    // 關聯屬性 (Navigation Property)：讓 EF Core 知道如何抓取對應的筆記本
    public Notebook? Notebook { get; set; }
}
