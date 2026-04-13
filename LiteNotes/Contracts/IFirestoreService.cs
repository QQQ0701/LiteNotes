namespace LiteNotes.Contracts;

/// <summary>
/// 定義本地資料庫與 Firebase Firestore 之間的雙向同步操作契約。
/// </summary>
public interface IFirestoreService
{
    /// <summary>將本地未同步的筆記本推送至 Firestore，軟刪除的筆記本同步刪除雲端記錄。</summary>
    Task SyncNotebooksAsync(string userId);

    /// <summary>將指定筆記本下未同步的筆記推送至 Firestore。</summary>
    Task SyncNotesAsync(string notebookId, string userId);

    /// <summary>將本地未同步的附件記錄推送至 Firestore，軟刪除的附件同步清理 Azure Blob。</summary>
    Task SyncAttachmentsAsync(string userId);

    /// <summary>執行全量同步：依序推送筆記本、筆記、附件至 Firestore。</summary>
    Task SyncAllAsync(string userId);

    /// <summary>從 Firestore 拉取所有資料還原至本地資料庫（筆記本、筆記、附件）。</summary>
    Task RestoreFromCloudAsync(string userId);
}