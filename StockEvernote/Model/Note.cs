using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StockEvernote.Model;
public partial class Note: ObservableObject
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string NotebookId { get; set; } = string.Empty;

    [ObservableProperty] private string _name = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // 🌟 2. 離線同步機制
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public bool IsSynced { get; set; } = false;
    public bool IsDeleted { get; set; } = false; // 放進資源回收筒

    // 關聯屬性 (Navigation Property)：讓 EF Core 知道如何抓取對應的筆記本
    public Notebook? Notebook { get; set; }

    [property: NotMapped]
    [ObservableProperty] private bool _isEditing;
    [property: NotMapped]
    [ObservableProperty] private string? _editingName;
}
