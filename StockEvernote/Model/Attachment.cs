using System.ComponentModel.DataAnnotations;

namespace StockEvernote.Model;

public class Attachment
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string NoteId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string BlobUrl { get; set; } = string.Empty;

    /// <summary>Azure Blob 內的路徑，刪除檔案時需要。</summary>
    public string BlobName { get; set; } = string.Empty;

    public long FileSize { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public bool IsImage { get; set; }
    public bool IsDeleted { get; set; } = false;
    public bool IsSynced { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}