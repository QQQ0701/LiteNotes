using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StockEvernote.Contracts;
using StockEvernote.Model;
using StockEvernote.Services;
using System.Collections.ObjectModel;
using System.DirectoryServices;
using SearchResult = StockEvernote.Model.SearchResult;

namespace StockEvernote.ViewModel;

public partial class NotesViewModel : ObservableObject
{
    private readonly INotebookService _notebookService;
    private readonly INoteService _noteService;
    private readonly IUserSession _userSession;
    private readonly IFirestoreService _firestoreService;
    private readonly ILogger<NotesViewModel> _logger;
    private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);
    private readonly ISearchService _searchService;
    private string CurrentUserId => _userSession.LocalId ?? string.Empty;
    public Action? LogoutAction { get; set; }
    public NotesViewModel(INotebookService notebookService,
        INoteService noteService,
        IUserSession userSession,
        IFirestoreService firestoreService,
        ILogger<NotesViewModel> logger,
        ISearchService searchService)
    {
        _notebookService = notebookService;
        _noteService = noteService;
        _userSession = userSession;
        _firestoreService = firestoreService;
        _logger = logger;
        _searchService = searchService;
    }

    [ObservableProperty] private ObservableCollection<Notebook> _notebooks = new();
    [ObservableProperty] private ObservableCollection<Note> _notes = new();
    [ObservableProperty] private string _newNotebookName = string.Empty;
    [ObservableProperty] private Notebook? _selectedNotebook;
    [ObservableProperty] private Note? _selectedNote;
    [ObservableProperty] private string _syncStatus = string.Empty;
    [ObservableProperty] private string _saveStatus = string.Empty;

    [ObservableProperty] private string _searchKeyword = string.Empty;
    [ObservableProperty] private ObservableCollection<SearchResult> _searchResults = new();
    [ObservableProperty] private bool _isSearchMode = false;
    [ObservableProperty] private bool _isSearchGlobal = true;  // true=全域, false=當前筆記本
    [ObservableProperty] private SearchResult? _selectedSearchResult;

    //---------------------正式要刪除-------------------------------
    [ObservableProperty] private bool _isAutoSyncEnabled = false;
    //---------------------正式要刪除-------------------------------

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
    /// <summary>點擊搜尋結果：跳到對應筆記本和筆記</summary>
    partial void OnSelectedSearchResultChanged(SearchResult? value)
    {
        if (value is null) return;

        // 切換到對應筆記本
        var targetNotebook = Notebooks.FirstOrDefault(n => n.Id == value.NotebookId);
        if (targetNotebook != null && SelectedNotebook != targetNotebook)
        {
            SelectedNotebook = targetNotebook;
        }

        // 等筆記載入完後選中對應筆記（需要延遲，因為 OnSelectedNotebookChanged 是 async）
        _ = SelectNoteAfterLoadAsync(value.NoteId);
    }

    /// <summary>
    /// 延遲等待 LoadNotesAsync 完成後再選中筆記。
    /// OnSelectedNotebookChanged 會觸發 async 載入，直接選中會找不到目標筆記。
    /// </summary>
    private async Task SelectNoteAfterLoadAsync(string noteId)
    {
        // 等一下讓 LoadNotesAsync 跑完
        await Task.Delay(200);

        var targetNote = Notes.FirstOrDefault(n => n.Id == noteId);
        if (targetNote != null)
        {
            SelectedNote = targetNote;
        }
    }

    [RelayCommand]
    private async Task InitializeSearchAsync()
    {
        await _searchService.InitializeAsync();
        await _searchService.RebuildIndexAsync(CurrentUserId);
        _logger.LogInformation("搜尋索引初始化完成");
    }
    //-------------------------------------------------------------------
    [RelayCommand]
    private async Task SaveSpecificNoteAsync(Note note)
    {
        if (note is null) return;

        await _saveLock.WaitAsync();
        try
        {
            await _noteService.UpdateNoteAsync(note);
            SaveStatus = $"自動儲存 {DateTime.Now:HH:mm:ss}";

            // 更新搜尋索引
            if (SelectedNotebook != null)
            {
                await _searchService.IndexNoteAsync(
                    note.Id, note.Name, note.Content ?? string.Empty,
                    note.NotebookId, SelectedNotebook.Name);
            }

            _logger.LogInformation("儲存筆記：{Name}", note.Name);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    [RelayCommand]
    private void Logout()
    {
        _userSession.Clear();
        _logger.LogInformation("使用者登出");
        LogoutAction?.Invoke();
    }

    [RelayCommand]
    private async Task SyncAsync()
    {
        try
        {
            SyncStatus = "⏳ 同步中...";
            _logger.LogInformation("開始同步，UserId：{UserId}", CurrentUserId);
            await _firestoreService.SyncAllAsync(CurrentUserId);
            SyncStatus = $"✅ 同步成功 {DateTime.Now:HH:mm:ss}";
            _logger.LogInformation("同步成功");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "同步失敗");
            SyncStatus = "❌ 同步失敗";
        }
    }
    [RelayCommand]
    private async Task RestoreFromCloudAsync()
    {
        try
        {
            SyncStatus = "⏳ 從雲端還原中...";
            _logger.LogInformation("開始從雲端還原，UserId：{UserId}", CurrentUserId);

            await _firestoreService.RestoreFromCloudAsync(CurrentUserId);
            await LoadNotebooksAsync(); // 還原後重新載入畫面

            // 雲端還原後重建索引
            await _searchService.RebuildIndexAsync(CurrentUserId);

            SyncStatus = $"✅ 還原成功 {DateTime.Now:HH:mm:ss}";
            _logger.LogInformation("從雲端還原成功，共 {Count} 本筆記本", Notebooks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "從雲端還原失敗");
            SyncStatus = "❌ 還原失敗";
        }
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

    // 當選擇的筆記本改變時，自動載入該本的筆記
    partial void OnSelectedNotebookChanged(Notebook? value)
    {
        if (value is null) return;
        _ = LoadNotesAsync(value.Id);
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
    }

    [RelayCommand]
    private void CancelNotebookRename(Notebook notebook)
    {
        _logger.LogInformation("取消筆記本重新命名：{Name}", notebook.Name);

        notebook.IsEditing = false;
        notebook.EditingName = string.Empty;
    }

    [RelayCommand]
    private async Task DeleteNotebookAsync(Notebook notebook)
    {
        await _notebookService.DeleteNotebookAsync(notebook.Id);
        _logger.LogInformation("刪除筆記本：{Name}", notebook.Name);

        bool isSelected = SelectedNotebook == notebook;
        Notebooks.Remove(notebook);

        // 移除該筆記本下所有筆記的索引
        var notesToRemove = await _noteService.GetNotesAsync(notebook.Id);
        foreach (var note in notesToRemove)
            await _searchService.RemoveNoteIndexAsync(note.Id);

        // 如果刪的是目前選中的筆記本，清空右側筆記列表
        if (isSelected)
        {
            SelectedNote = null;
            Notes.Clear();
            SelectedNotebook = null;
        }
    }

    //----------------------------Note-----------------------------------------------

    [RelayCommand]
    private async Task LoadNotesAsync(string notebookId)
    {
        var data = await _noteService.GetNotesAsync(notebookId);
        Notes.Clear();

        foreach (var item in data)
            Notes.Add(item);

        _logger.LogInformation("載入筆記，NotebookId：{NotebookId}，共 {Count} 篇", 
            notebookId, Notes.Count);
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

        await _searchService.RemoveNoteIndexAsync(note.Id);

        //如果刪的是目前選中的筆記本，清空右側筆記列表
        if (SelectedNote == note)
            SelectedNote = null;

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
}
