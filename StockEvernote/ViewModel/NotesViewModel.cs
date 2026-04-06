using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StockEvernote.Contracts;
using StockEvernote.Model;
using System.Collections.ObjectModel;
using System.IO;
using Attachment = StockEvernote.Model.Attachment;
using SearchResult = StockEvernote.Model.SearchResult;

namespace StockEvernote.ViewModel;

/// <summary>
/// 筆記本與筆記管理之核心 ViewModel，負責處理 CRUD 操作、全文檢索連動與雲端同步狀態。
/// </summary>
public partial class NotesViewModel : ObservableObject
{
    #region 注入服務與私有欄位

    private readonly INotebookService _notebookService;
    private readonly INoteService _noteService;
    private readonly IUserSession _userSession;
    private readonly IFirestoreService _firestoreService;
    private readonly ILogger<NotesViewModel> _logger;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly ISearchService _searchService;
    private readonly IFileUploadService _fileUploadService;

    /// <summary>
    /// 搜尋跳轉期間抑制 OnSelectedNotebookChanged 的自動載入，防止 Double Fetching。
    /// </summary>
    private bool _isNavigatingFromSearch = false;
    private string CurrentUserId => _userSession.LocalId ?? string.Empty;
    private CancellationTokenSource? _loadAttachmentsCts;
    public Action? LogoutAction { get; set; }
    public Action<string, string>? ShowErrorDialogAction { get; set; }

    #endregion

    #region 可觀察屬性

    [ObservableProperty] private ObservableCollection<Notebook> _notebooks = new();
    [ObservableProperty] private ObservableCollection<Note> _notes = new();
    [ObservableProperty] private ObservableCollection<SearchResult> _searchResults = new();
    [ObservableProperty] private ObservableCollection<Attachment> _attachments = new();

    [ObservableProperty] private Notebook? _selectedNotebook;
    [ObservableProperty] private Note? _selectedNote;
    [ObservableProperty] private SearchResult? _selectedSearchResult;

    [ObservableProperty] private string _newNotebookName = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _saveStatus = string.Empty;
    [ObservableProperty] private string _searchKeyword = string.Empty;

    [ObservableProperty] private bool _isSearchMode = false;
    [ObservableProperty] private bool _isSearchGlobal = true;  // true=全域, false=當前筆記本

    #endregion
    //---------------------正式要刪除-------------------------------
    [ObservableProperty] private bool _isAutoSyncEnabled = false;
    //---------------------正式要刪除-------------------------------

    #region 建構函式
    public NotesViewModel(INotebookService notebookService,
        INoteService noteService,
        IUserSession userSession,
        IFirestoreService firestoreService,
        ILogger<NotesViewModel> logger,
        ISearchService searchService,
        IFileUploadService fileUploadService)
    {
        _notebookService = notebookService;
        _noteService = noteService;
        _userSession = userSession;
        _firestoreService = firestoreService;
        _logger = logger;
        _searchService = searchService;
        _fileUploadService = fileUploadService;
    }
    #endregion
    #region 系統啟動與基礎設施

    /// <summary>
    /// 階段一：確保底層基礎設施準備完畢 (造輪胎)
    /// </summary>
    [RelayCommand]
    private async Task InitializeInfrastructureAsync()
    {
        await _searchService.InitializeAsync();

        await _fileUploadService.InitializeAsync();

        _logger.LogInformation("基礎設施 (搜尋表、Azure容器) 初始化完成");
    }

    /// <summary>
    /// 重建搜尋索引 (必須在資料最新時執行)
    /// </summary>
    [RelayCommand]
    private async Task RebuildSearchIndexAsync()
    {
        await _searchService.RebuildIndexAsync(CurrentUserId);
        _logger.LogInformation("搜尋索引重建完成");
    }

    #endregion

    #region 附件管理 (Azure Blob Storage)
    /// <summary>
    /// 上傳檔案到 Azure Blob 並建立附件記錄。
    /// 上傳完成後透過事件通知 View 層處理圖片插入。
    /// </summary>
    [RelayCommand]
    private async Task UploadFileAsync(string filePath)
    {
        if (SelectedNote is null || string.IsNullOrEmpty(filePath)) return;

        try
        {
            var attachment = await _fileUploadService.UploadAsync(
                filePath, SelectedNote.Id, CurrentUserId);

            Attachments.Add(attachment);

            _logger.LogInformation("上傳附件：{FileName}，NoteId：{NoteId}",
                attachment.FileName, SelectedNote.Id);

            StatusMessage = string.Empty;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("上傳驗證失敗：{Message}", ex.Message);
            StatusMessage = "❌ 上傳中止";
            ShowErrorDialogAction?.Invoke("上傳失敗", ex.Message);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "檔案讀取失敗，路徑：{Path}", filePath);
            StatusMessage = "無法讀取檔案，請確認檔案是否被其他程式（如 Word 或圖片檢視器）開啟中。";
            ShowErrorDialogAction?.Invoke("檔案讀取失敗", "無法讀取檔案，請確認檔案是否被其他程式（如 Word 或圖片檢視器）開啟中。");
        }
        catch (Azure.RequestFailedException ex)
        {
            // Azure 雲端連線失敗 (需 using Azure;)
            _logger.LogError(ex, "Azure 雲端上傳失敗");
            StatusMessage = "雲端伺服器連線異常，請檢查網路連線或稍後再試。";
            ShowErrorDialogAction?.Invoke("連線異常", "雲端伺服器連線異常，請檢查網路連線或稍後再試。");
        }
        catch (Exception ex)
        {
            // 終極防線：攔截所有未知的當機錯誤
            _logger.LogError(ex, "發生未知的上傳錯誤，檔案：{Path}", filePath);
            StatusMessage = "發生未知的錯誤，無法完成上傳。";
            ShowErrorDialogAction?.Invoke("系統錯誤", "發生未知的錯誤，無法完成上傳。");
        }
    }

    [RelayCommand]
    private async Task OpenAttachmentAsync(Attachment attachment)
    {
        if (attachment is null) return;

        try
        {
            var tempPath = await _fileUploadService.DownloadToTempAsync(attachment);

            // 用系統預設程式開啟
            var psi = new System.Diagnostics.ProcessStartInfo(tempPath)
            {
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);

            _logger.LogInformation("開啟附件：{FileName}", attachment.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "開啟附件失敗：{FileName}", attachment.FileName);
            StatusMessage = "下載或開啟檔案失敗，請檢查網路連線。";
            ShowErrorDialogAction?.Invoke("開啟失敗", "下載或開啟檔案失敗，請檢查網路連線。");
        }
    }

    [RelayCommand]
    private async Task DeleteAttachmentAsync(Attachment attachment)
    {
        if (attachment is null) return;

        try
        {
            await _fileUploadService.SoftDeleteAsync(attachment.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "資料庫刪除附件失敗：{FileName}", attachment.FileName);
            StatusMessage = "刪除檔案記錄失敗，請稍後再試。";
            ShowErrorDialogAction?.Invoke("刪除失敗", "無法刪除檔案記錄，請檢查網路或稍後再試。");
            return;
        }

        Attachments.Remove(attachment);

        _logger.LogInformation("刪除附件成功：{FileName}", attachment.FileName);
        try
        {
            await _fileUploadService.DeleteBlobAsync(attachment.BlobName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Azure Blob 刪除失敗，產生孤兒檔案：{BlobName}", attachment.BlobName);
        }

        _logger.LogInformation("刪除附件成功：{FileName}", attachment.FileName);
    }
    partial void OnSelectedNoteChanged(Note? value)
    {
        _loadAttachmentsCts?.Cancel();
        if (value is null)
        {
            Attachments.Clear();
            return;
        }
        _loadAttachmentsCts = new CancellationTokenSource();

        _ = LoadAttachmentsAsync(value.Id, _loadAttachmentsCts.Token);
    }

    /// <summary>
    /// 切換筆記時載入該筆記的附件列表。
    /// </summary>
    private async Task LoadAttachmentsAsync(string noteId, CancellationToken token)
    {
        try
        {
            // await Task.Delay(3000, token);
            var data = await _fileUploadService.GetAttachmentsAsync(noteId, token);

            if (token.IsCancellationRequested) return;
            Attachments.Clear();

            foreach (var item in data)
                Attachments.Add(item);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
                _logger.LogError(ex, "載入附件列表失敗");
        }
    }

    #endregion

    #region 搜尋系統 (Search Subsystem)

    /// <summary>
    /// 執行全文檢索，依據 IsSearchGlobal 決定搜尋範圍。
    /// </summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchKeyword))
        {
            SearchResults.Clear();
            IsSearchMode = false;
            return;
        }

        IsSearchMode = true;

        var notebookId = IsSearchGlobal ? null : SelectedNotebook?.Id;
        var results = await _searchService.SearchAsync(SearchKeyword, CurrentUserId, notebookId);

        SearchResults.Clear();
        foreach (var result in results)
            SearchResults.Add(result);

        _logger.LogInformation("搜尋「{Keyword}」，結果 {Count} 筆", SearchKeyword, SearchResults.Count);
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchKeyword = string.Empty;
        SearchResults.Clear();
        IsSearchMode = false;
    }

    /// <summary>
    /// 點擊搜尋結果時，跳轉至對應筆記本並選中目標筆記。
    /// 透過 _isNavigatingFromSearch 旗標避免觸發 OnSelectedNotebookChanged 的重複載入。
    /// </summary>
    partial void OnSelectedSearchResultChanged(SearchResult? value)
    {
        if (value is null) return;

        // 切換到對應筆記本
        var targetNotebook = Notebooks.FirstOrDefault(n => n.Id == value.NotebookId);
        if (targetNotebook != null)
        {
            if (SelectedNotebook != targetNotebook)
            {
                _isNavigatingFromSearch = true;
                SelectedNotebook = targetNotebook;
                _isNavigatingFromSearch = false;

                _ = ExecuteLoadNotesAsync(targetNotebook.Id, value.NoteId);
            }
            else
            {
                // 因為已經載入過了，直接切換選取狀態即可
                SelectedNote = Notes.FirstOrDefault(n => n.Id == value.NoteId);
            }
        }
    }
    #endregion

    #region 雲端同步與帳號操作 (Cloud Sync & Session)
    [RelayCommand]
    private async Task SyncAsync()
    {
        try
        {
            StatusMessage = "⏳ 同步中...";
            _logger.LogInformation("開始同步，UserId：{UserId}", CurrentUserId);
            await _firestoreService.SyncAllAsync(CurrentUserId);
            StatusMessage = $"✅ 同步成功 {DateTime.Now:HH:mm:ss}";
            _logger.LogInformation("同步成功");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "同步失敗");
            StatusMessage = "❌ 同步失敗";
        }
    }

    /// <summary>
    /// 從雲端還原資料後重新載入畫面並全量重建 FTS5 索引。
    /// </summary>
    [RelayCommand]
    private async Task RestoreFromCloudAsync()
    {
        try
        {
            StatusMessage = "⏳ 從雲端還原中...";
            _logger.LogInformation("開始從雲端還原，UserId：{UserId}", CurrentUserId);

            await _firestoreService.RestoreFromCloudAsync(CurrentUserId);
            await LoadNotebooksAsync(); // 還原後重新載入畫面

            // 雲端還原後重建索引
            await _searchService.RebuildIndexAsync(CurrentUserId);

            StatusMessage = $"✅ 還原成功 {DateTime.Now:HH:mm:ss}";
            _logger.LogInformation("從雲端還原成功，共 {Count} 本筆記本", Notebooks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "從雲端還原失敗");
            StatusMessage = "❌ 還原失敗";
        }
    }

    [RelayCommand]
    private void Logout()
    {
        _userSession.Clear();
        _logger.LogInformation("使用者登出");
        LogoutAction?.Invoke();
    }
    #endregion

    #region 筆記本管理 (Notebook Management)
    // 當選擇的筆記本改變時，自動載入該本的筆記
    partial void OnSelectedNotebookChanged(Notebook? value)
    {
        if (value is null) return;
        if (_isNavigatingFromSearch) return;
        _ = ExecuteLoadNotesAsync(value.Id);
    }

    [RelayCommand]
    private async Task LoadNotebooksAsync()
    {
        var data = await _notebookService.GetNotebooksAsync(CurrentUserId);
        Notebooks.Clear();

        foreach (var item in data)
            Notebooks.Add(item);

        _logger.LogInformation("載入筆記本，共 {Count} 本", Notebooks.Count);
    }

    [RelayCommand]
    private async Task AddNotebookAsync()
    {
        var name = string.IsNullOrWhiteSpace(NewNotebookName) ? "新筆記本" : NewNotebookName.Trim();
        var created = await _notebookService.CreateNotebookAsync(name, CurrentUserId);
        _logger.LogInformation("新增筆記本：{Name}", created.Name);

        Notebooks.Insert(0, created);
        SelectedNotebook = created;

        // ✅ 新增後自動進入編輯模式，讓使用者可以直接改名
        Edit(created);
    }

    [RelayCommand]
    private void Edit(Notebook notebook)
    {
        notebook.EditingName = notebook.Name; // 預填目前名稱
        notebook.IsEditing = true;
    }

    /// <summary>
    /// 確認筆記本重新命名，並更新該筆記本下所有筆記的 FTS5 索引。
    /// </summary>
    [RelayCommand]
    private async Task ConfirmNotebookRenameAsync(Notebook notebook)
    {
        if (string.IsNullOrWhiteSpace(notebook.EditingName))
        {
            notebook.EditingName = notebook.Name;
            notebook.IsEditing = false;
            return;
        }

        var newName = notebook.EditingName.Trim();
        notebook.IsEditing = false;

        if (notebook.Name == newName) return;

        _logger.LogInformation("筆記本重新命名：{OldName} → {NewName}", notebook.Name, newName);

        notebook.Name = newName;
        await _notebookService.UpdateNotebookAsync(notebook);

        // 精準更新此筆記本下的所有筆記索引，避免大重置
        var childNotes = await _noteService.GetNotesAsync(notebook.Id);
        foreach (var note in childNotes)
        {
            await _searchService.IndexNoteAsync(
                note.Id, note.Name, note.Content ?? string.Empty,
                notebook.Id, newName); // 傳入新的筆記本名稱
        }
    }

    [RelayCommand]
    private void CancelNotebookRename(Notebook notebook)
    {
        _logger.LogInformation("取消筆記本重新命名：{Name}", notebook.Name);

        notebook.IsEditing = false;
        notebook.EditingName = string.Empty;
    }

    /// <summary>
    /// 刪除筆記本及其所有子筆記的 FTS5 索引。
    /// 索引清理為次要操作，失敗不影響刪除本身。
    /// </summary>
    [RelayCommand]
    private async Task DeleteNotebookAsync(Notebook notebook)
    {
        var notesToRemove = await _noteService.GetNotesAsync(notebook.Id);

        await _notebookService.DeleteNotebookAsync(notebook.Id);
        _logger.LogInformation("刪除筆記本：{Name}", notebook.Name);

        bool isSelected = SelectedNotebook == notebook;
        Notebooks.Remove(notebook);

        // 移除該筆記本下所有筆記的索引
        try
        {
            foreach (var note in notesToRemove)
            {
                await _searchService.RemoveNoteIndexAsync(note.Id);
            }

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "刪除筆記本時，清理索引發生錯誤。");
            // 未來可加入自動修復機制，或依靠 RebuildIndexAsync 解決
        }

        // 如果刪的是目前選中的筆記本，清空右側筆記列表
        if (isSelected)
        {
            SelectedNote = null;
            Notes.Clear();
            SelectedNotebook = null;
        }
    }

    #endregion

    #region 筆記管理 (Note Management)

    [RelayCommand]
    private async Task LoadNotesAsync(string notebookId)
    {
        await ExecuteLoadNotesAsync(notebookId, null);
    }
    /// <summary>
    /// 載入筆記清單的核心方法。支援載入完畢後選中指定筆記（搜尋跳轉用）。
    /// </summary>
    private async Task ExecuteLoadNotesAsync(string notebookId, string? targetNoteIdToSelect = null)
    {
        var data = await _noteService.GetNotesAsync(notebookId);
        Notes.Clear();

        foreach (var item in data)
            Notes.Add(item);

        _logger.LogInformation("載入筆記，NotebookId：{NotebookId}，共 {Count} 篇",
            notebookId, Notes.Count);

        if (!string.IsNullOrEmpty(targetNoteIdToSelect))
        {
            SelectedNote = Notes.FirstOrDefault(n => n.Id == targetNoteIdToSelect);
        }
    }

    [RelayCommand]
    private async Task StartAddNoteAsync()
    {
        if (SelectedNotebook is null) return;

        var created = await _noteService.CreateNoteAsync("新筆記", SelectedNotebook.Id);
        Notes.Insert(0, created);
        created.EditingName = created.Name;
        created.IsEditing = true;

        _logger.LogInformation("新增筆記：{Name}，NotebookId：{NotebookId}", created.Name, SelectedNotebook.Id);
    }

    /// <summary>
    /// 確認筆記重新命名，並更新 FTS5 索引。
    /// </summary>
    [RelayCommand]
    private async Task ConfirmNoteRenameAsync(Note note)
    {
        if (string.IsNullOrWhiteSpace(note.EditingName))
        {
            note.EditingName = note.Name;
            note.IsEditing = false;
            return;
        }

        var newName = note.EditingName.Trim();
        note.IsEditing = false;

        if (note.Name == newName) return;

        _logger.LogInformation("筆記重新命名：{OldName} → {NewName}", note.Name, newName);

        note.Name = newName;
        await _noteService.UpdateNoteAsync(note);

        if (SelectedNotebook != null)
        {
            var existingNote = Notes.FirstOrDefault(n => n.Id == note.Id);
            await _searchService.IndexNoteAsync(
                note.Id, newName, existingNote?.Content ?? string.Empty,
                note.NotebookId, SelectedNotebook.Name);
        }
    }

    [RelayCommand]
    private void CancelNoteRename(Note note)
    {
        _logger.LogInformation("取消筆記重新命名：{Name}", note.Name);

        if (note.Name == "新筆記")
            Notes.Remove(note);
        note.IsEditing = false;
        note.EditingName = string.Empty;
    }

    [RelayCommand]
    private async Task DeleteNoteAsync(Note note)
    {
        await _noteService.DeleteNoteAsync(note.Id);
        _logger.LogInformation("刪除筆記：{Name}", note.Name);
        Notes.Remove(note);

        try
        {
            await _searchService.RemoveNoteIndexAsync(note.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "刪除筆記時，清理索引發生錯誤");
            ShowErrorDialogAction?.Invoke("刪除失敗", "無法刪除筆記，請稍後再試。");
        }

        if (SelectedNote == note)
            SelectedNote = null;
    }

    /// <summary>
    /// 自動儲存筆記內容至資料庫，完成後於鎖外更新 FTS5 索引。
    /// 索引更新獨立於儲存鎖之外，避免延長鎖的持有時間影響下一次自動儲存。
    /// </summary>
    [RelayCommand]
    private async Task SaveSpecificNoteAsync(Note note)
    {
        if (note is null) return;

        await _saveLock.WaitAsync();
        try
        {
            await _noteService.UpdateNoteAsync(note);
            SaveStatus = $"自動儲存 {DateTime.Now:HH:mm:ss}";
            _logger.LogInformation("儲存筆記：{Name}", note.Name);
        }
        catch (Exception ex) 
        {
            _logger.LogError(ex, "自動儲存失敗：{Name}", note.Name);
            SaveStatus = "❌ 儲存失敗"; 
        }
        finally
        {
            _saveLock.Release();
        }

        try
        {
            // 更新搜尋索引
            if (SelectedNotebook != null)
            {
                await _searchService.IndexNoteAsync(
                    note.Id, note.Name, note.Content ?? string.Empty,
                    note.NotebookId, SelectedNotebook.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "儲存筆記時，更新索引發生錯誤");
        }
    }

    #endregion
}


//手動儲存功能

//[RelayCommand]
//private async Task SaveNoteAsync()
//{
//    if (SelectedNote is null) return;

//    SelectedNote.Content = NoteContent;
//    await _noteService.UpdateNoteAsync(SelectedNote);
//    SaveStatus = $"已儲存 {DateTime.Now:HH:mm:ss}";
//    _logger.LogInformation("儲存筆記：{Name}", SelectedNote.Name);
//}

