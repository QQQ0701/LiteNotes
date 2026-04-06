using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StockEvernote.Contracts;
using StockEvernote.Data;
using StockEvernote.Helpers;
using StockEvernote.Model;
using System.IO;

namespace StockEvernote.Services;

public class AzureBlobService : IFileUploadService
{
    private readonly BlobContainerClient _containerClient;
    private readonly EvernoteDbContext _dbContext;
    private readonly ILogger _logger;

    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp"
    };
    private static readonly HashSet<string> AllowedDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".xlsx", ".pptx", ".txt", ".csv"
    };

    private const long MaxImageSize = 5 * 1024 * 1024;      // 5 MB
    private const long MaxDocumentSize = 20 * 1024 * 1024;   // 20 MB


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
        // 組合 Blob 路徑：userId/noteId/guid_filename
        var blobName = $"{userId}/{noteId}/{Guid.NewGuid():N}_{fileInfo.Name}";
        var blobClient = _containerClient.GetBlobClient(blobName);
        // 上傳到 Azure Blob
        var contentType = FileHelper.GetContentType(extension);
        var headers = new BlobHttpHeaders { ContentType = contentType };

        await using var stream = File.OpenRead(filePath);
        await blobClient.UploadAsync(stream, new BlobUploadOptions { HttpHeaders = headers });

        _logger.LogInformation("檔案上傳成功：{FileName}，大小：{Size}",
            fileInfo.Name, FileHelper.FormatFileSize(fileInfo.Length));

        // 建立本地資料庫記錄
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
    public async Task<string> DownloadToTempAsync(Attachment attachment)
    {
        var blobClient = _containerClient.GetBlobClient(attachment.BlobName);

        var tempDir = Path.Combine(Path.GetTempPath(), "StockEvernote");
        Directory.CreateDirectory(tempDir);

        var tempPath = Path.Combine(tempDir, attachment.FileName);

        await using var stream = File.Create(tempPath);
        await blobClient.DownloadToAsync(stream);

        _logger.LogInformation("檔案下載完成：{FileName}，暫存路徑：{Path}",
            attachment.FileName, tempPath);

        return tempPath;
    }

    public async Task DeleteBlobAsync(string blobName)
    {
        var blobClient = _containerClient.GetBlobClient(blobName);
        await blobClient.DeleteIfExistsAsync();

        _logger.LogInformation("Azure Blob 已刪除：{BlobName}", blobName);
    }

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

    public async Task<List<Attachment>> GetAttachmentsAsync(string noteId, CancellationToken token = default)
    {
        return await _dbContext.Attachments
              .Where(a => a.NoteId == noteId && !a.IsDeleted)
              .OrderByDescending(a => a.CreatedAt)
              .ToListAsync(token);
    }
}
