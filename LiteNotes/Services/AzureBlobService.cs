using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using LiteNotes.Contracts;
using LiteNotes.Data;
using LiteNotes.Helpers;
using LiteNotes.Model;
using System.IO;

namespace LiteNotes.Services;
/// <summary>
/// Azure Blob Storage 檔案服務：處理附件的上傳、下載、刪除，
/// 以及本地 SQLite 附件記錄的 CRUD。
/// </summary>
/// <remarks>
/// Blob 路徑規則：{userId}/{noteId}/{guid}_{filename}
/// 檔案大小限制：圖片 5MB、文件 20MB。
/// </remarks>
public class AzureBlobService : IFileUploadService
{
    private readonly BlobContainerClient _containerClient;
    private readonly EvernoteDbContext _dbContext;
    private readonly ILogger<AzureBlobService> _logger;

    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp"
    };
    private static readonly HashSet<string> AllowedDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".xlsx", ".pptx", ".txt", ".csv"
    };

    private const long MaxImageSize = 5 * 1024 * 1024;     
    private const long MaxDocumentSize = 20 * 1024 * 1024;  


    public AzureBlobService(
    EvernoteDbContext dbContext,
    IConfiguration configuration,
    ILogger<AzureBlobService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;

        var connectionString = configuration["Azure:Storage:ConnectionString"]
            ?? throw new InvalidOperationException("找不到 Azure:BlobConnectionString");

        var containerName = configuration["Azure:Storage:ContainerName"]
            ?? throw new InvalidOperationException("找不到 Azure:BlobContainerName");

        var blobServiceClient = new BlobServiceClient(connectionString);
        _containerClient = blobServiceClient.GetBlobContainerClient(containerName);
    }

    /// <summary>
    /// 確保 Container 存在。啟動時呼叫一次即可。
    /// </summary>
    public async Task InitializeAsync()
    {
        await _containerClient.CreateIfNotExistsAsync(PublicAccessType.None);
        _logger.LogInformation("Azure Blob Container 初始化完成");
    }

    /// <summary>
    /// 驗證檔案類型與大小 → 上傳到 Azure Blob → 建立本地 SQLite 附件記錄。
    /// </summary>
    /// <exception cref="InvalidOperationException">檔案類型不支援或大小超過限制。</exception>

    public async Task<Attachment> UploadAsync(string filePath, string noteId, string userId)
    {
        var fileInfo = new FileInfo(filePath);
        var extension = fileInfo.Extension.ToLowerInvariant();
        var isImage = AllowedImageExtensions.Contains(extension);
        var isDocument = AllowedDocumentExtensions.Contains(extension);

        if (!isImage && !isDocument)
            throw new InvalidOperationException($"不支援的檔案類型：{extension}");

        var maxSize = isImage ? MaxImageSize : MaxDocumentSize;
        if (fileInfo.Length > maxSize)
            throw new InvalidOperationException(
                $"檔案大小超過限制：{FileHelper.FormatFileSize(fileInfo.Length)}" +
                $"，上限 {FileHelper.FormatFileSize(maxSize)}");
 
        var blobName = $"{userId}/{noteId}/{Guid.NewGuid():N}_{fileInfo.Name}";
        var blobClient = _containerClient.GetBlobClient(blobName);

        var contentType = FileHelper.GetContentType(extension);
        var headers = new BlobHttpHeaders { ContentType = contentType };

        await using var stream = File.OpenRead(filePath);
        await blobClient.UploadAsync(stream, new BlobUploadOptions { HttpHeaders = headers });

        _logger.LogInformation("檔案上傳成功：{FileName}，大小：{Size}",
            fileInfo.Name, FileHelper.FormatFileSize(fileInfo.Length));

        var attachment = new Attachment
        {
            NoteId = noteId,
            UserId = userId,
            FileName = fileInfo.Name,
            BlobUrl = blobClient.Uri.ToString(),
            BlobName = blobName,
            FileSize = fileInfo.Length,
            ContentType = contentType,
            IsImage = isImage,
            IsSynced = false
        };

        _dbContext.Attachments.Add(attachment);
        await _dbContext.SaveChangesAsync();

        return attachment;
    }

    /// <summary>從 Azure Blob 下載到系統暫存資料夾，回傳完整路徑供外部程式開啟。</summary>
    public async Task<string> DownloadToTempAsync(Attachment attachment)
    {
        var blobClient = _containerClient.GetBlobClient(attachment.BlobName);

        var tempDir = Path.Combine(Path.GetTempPath(), "LiteNotes");
        Directory.CreateDirectory(tempDir);

        var tempPath = Path.Combine(tempDir, attachment.FileName);

        await using var stream = File.Create(tempPath);
        await blobClient.DownloadToAsync(stream);

        _logger.LogInformation("檔案下載完成：{FileName}，暫存路徑：{Path}",
            attachment.FileName, tempPath);

        return tempPath;
    }

    /// <summary>刪除 Azure Blob 上的檔案本體。由 FirestoreService 同步時呼叫。</summary>
    public async Task DeleteBlobAsync(string blobName)
    {
        var blobClient = _containerClient.GetBlobClient(blobName);
        await blobClient.DeleteIfExistsAsync();

        _logger.LogInformation("Azure Blob 已刪除：{BlobName}", blobName);
    }

    /// <summary>軟刪除本地附件記錄，標記 IsSynced=false 等待下次同步時清理雲端。</summary>
    public async Task SoftDeleteAsync(string attachmentId)
    {
        var attachment = await _dbContext.Attachments.FindAsync(attachmentId)
            ?? throw new KeyNotFoundException($"找不到 Attachment Id：{attachmentId}");

        attachment.IsDeleted = true;
        attachment.IsSynced = false;
        attachment.UpdatedAt = DateTime.Now;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("軟刪除附件：{FileName}", attachment.FileName);

    }

    /// <summary>取得指定筆記的所有未刪除附件，依建立時間降序排列。</summary>
    public async Task<List<Attachment>> GetAttachmentsAsync(string noteId, CancellationToken token = default)
    {
        return await _dbContext.Attachments
              .Where(a => a.NoteId == noteId && !a.IsDeleted)
              .OrderByDescending(a => a.CreatedAt)
              .ToListAsync(token);
    }
}
