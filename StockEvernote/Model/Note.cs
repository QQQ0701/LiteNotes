using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StockEvernote.Model;
public partial class Note: ObservableObject
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    [Required]
    public string NotebookId { get; set; } = string.Empty;

    [ObservableProperty] private string _name = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;


    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public bool IsSynced { get; set; } = false;
    public bool IsDeleted { get; set; } = false; 

    public Notebook? Notebook { get; set; }

    [property: NotMapped]
    [ObservableProperty] private bool _isEditing;
    [property: NotMapped]
    [ObservableProperty] private string? _editingName;
}
