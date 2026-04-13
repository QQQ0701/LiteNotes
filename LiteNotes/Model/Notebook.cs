using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LiteNotes.Model;
public partial class Notebook : ObservableObject
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    [Required]
    public string UserId { get; set; } = string.Empty;

    [ObservableProperty] private string _name = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public bool IsSynced { get; set; } = false;
    public bool IsDeleted { get; set; } = false;

    public ICollection<Note> Notes { get; set; } = new List<Note>();


    [property: NotMapped] 
    [ObservableProperty] private bool _isEditing = false;

    [property: NotMapped]
    [ObservableProperty] private string _editingName = string.Empty;
}
