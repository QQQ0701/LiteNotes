using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StockEvernote.Model;
public partial class Notebook : ObservableObject
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;

    [ObservableProperty] private string _name = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // 🌟 2. 離線同步機制
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public bool IsSynced { get; set; } = false;
    public bool IsDeleted { get; set; } = false;
    // 關聯屬性 (Navigation Property)：一本筆記本可以有很多篇筆記
    public ICollection<Note> Notes { get; set; } = new List<Note>();

    [property: NotMapped] // 告訴 EF Core：這是 UI 用的障眼法開關，不要把它建進 SQLite 資料表裡！
    [ObservableProperty] private bool _isEditing = false;

    [property: NotMapped]
    [ObservableProperty] private string _editingName = string.Empty;
}
