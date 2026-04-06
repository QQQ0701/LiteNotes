using StockEvernote.Model;

namespace StockEvernote.Contracts;

public interface IFileUploadService
{
    /// <summary>負責連線到微軟 Azure，檢查 Blob Container 是否存在，沒有就建一個</summary>
    Task InitializeAsync();
    /// <summary>上傳檔案到 Azure Blob，回傳 Attachment 記錄。</summary>
    Task<Attachment> UploadAsync(string filePath, string noteId, string userId);

    /// <summary>從 Azure Blob 下載檔案到本機暫存資料夾，回傳暫存路徑。</summary>
    Task<string> DownloadToTempAsync(Attachment attachment);

    /// <summary>刪除 Azure Blob 上的檔案。</summary>
    Task DeleteBlobAsync(string blobName);

    /// <summary>取得指定筆記的所有附件。</summary>
    Task<List<Attachment>> GetAttachmentsAsync(string noteId, CancellationToken token = default);

    /// <summary>軟刪除附件記錄。</summary>
    Task SoftDeleteAsync(string attachmentId);
}
