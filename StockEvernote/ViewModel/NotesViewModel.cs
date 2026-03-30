using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StockEvernote.Contracts;
using StockEvernote.Model;
using System.Collections.ObjectModel;

namespace StockEvernote.ViewModel;

public partial class NotesViewModel : ObservableObject
{
    private readonly INotebookService _notebookService;
    private readonly INoteService _noteService;
    //  private readonly string _currentUserId = "TestUser123";
    private readonly IUserSession _userSession;
    private readonly IFirestoreService _firestoreService;
    private readonly ILogger<NotesViewModel> _logger;
    private string CurrentUserId => _userSession.LocalId ?? string.Empty;
    public NotesViewModel(INotebookService notebookService,
        INoteService noteService,
        IUserSession userSession,
        IFirestoreService firestoreService,
        ILogger<NotesViewModel> logger)
    {
        _notebookService = notebookService;
        _noteService = noteService;
        _userSession = userSession;
        _firestoreService = firestoreService;
        _logger = logger;
    }

    // ★ WPF 的魔法集合：當裡面的資料增加或減少時，畫面會「自動」更新！
    [ObservableProperty] private ObservableCollection<Notebook> _notebooks = new();
    [ObservableProperty] private ObservableCollection<Note> _notes = new();
    [ObservableProperty] private string _newNotebookName = string.Empty;
    [ObservableProperty] private Notebook? _selectedNotebook;
    [ObservableProperty] private Note? _selectedNote;
    [ObservableProperty] private string _noteContent = string.Empty;
    [ObservableProperty] private string _syncStatus = string.Empty;

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
    private async Task LoadNotebooksAsync()
    {
        var data = await _notebookService.GetNotebooksAsync(CurrentUserId);
        Notebooks.Clear();

        foreach (var item in data)
            Notebooks.Add(item);
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
        if (string.IsNullOrWhiteSpace(notebook.EditingName)) return;

        var newName = notebook.EditingName.Trim();
        notebook.IsEditing = false;

        if (notebook.Name == newName) return;

        notebook.Name = newName;
        await _notebookService.UpdateNotebookAsync(notebook);
    }

    [RelayCommand]
    private void CancelNotebookRename(Notebook notebook)
    {
        notebook.IsEditing = false;
        notebook.EditingName = string.Empty;
    }

    [RelayCommand]
    private async Task DeleteNotebookAsync(Notebook notebook)
    {
        await _notebookService.DeleteNotebookAsync(notebook.Id);
        _logger.LogInformation("刪除筆記本：{Name}", notebook.Name);
        Notebooks.Remove(notebook);

        // 如果刪的是目前選中的筆記本，清空右側筆記列表
        if (SelectedNotebook == notebook)
        {
            SelectedNotebook = null;
            Notes.Clear();
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
    }

    [RelayCommand]
    private async Task StartAddNoteAsync()
    {
        if (SelectedNotebook is null) return;

        var created = await _noteService.CreateNoteAsync("新筆記", SelectedNotebook.Id);
        Notes.Insert(0, created);
        created.EditingName = created.Name;
        created.IsEditing = true;
    }

    [RelayCommand]
    private async Task ConfirmNoteRenameAsync(Note note)
    {
        if (string.IsNullOrWhiteSpace(note.EditingName)) return;

        var newName = note.EditingName.Trim();
        note.IsEditing = false;

        if (note.Name == newName) return;

        note.Name = newName;
        await _noteService.UpdateNoteAsync(note);
    }

    [RelayCommand]
    private void CancelNoteRename(Note note)
    {
        if (note.Name == "新筆記")
            Notes.Remove(note); // 取消時直接刪掉新增的空白筆記
        note.IsEditing = false;
        note.EditingName = string.Empty;
    }


    // ✅ 儲存 Command（名稱 + 內容一起存）
    [RelayCommand]
    private async Task SaveNoteAsync()
    {
        if (SelectedNote is null) return;
        SelectedNote.Content = NoteContent;
        await _noteService.UpdateNoteAsync(SelectedNote);
        _logger.LogInformation("儲存筆記：{Name}", SelectedNote.Name);
    }


    [RelayCommand]
    private async Task DeleteNoteAsync(Note note)
    {
        await _noteService.DeleteNoteAsync(note.Id);
        _logger.LogInformation("刪除筆記：{Name}", note.Name);
        Notes.Remove(note);

        //如果刪的是目前選中的筆記本，清空右側筆記列表
        if (SelectedNote == note)
            SelectedNote = null;

    }
}
