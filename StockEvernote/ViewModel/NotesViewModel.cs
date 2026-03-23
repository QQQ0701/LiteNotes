using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockEvernote.Contracts;
using StockEvernote.Model;
using StockEvernote.Services;
using System.Collections.ObjectModel;

namespace StockEvernote.ViewModel;

public partial class NotesViewModel:ObservableObject
{
    private readonly INotebookService _notebookService;
    private readonly INoteService _noteService;
    private readonly string _currentUserId = "TestUser123";
    public NotesViewModel(INotebookService notebookService ,INoteService noteService)
    {
        _notebookService = notebookService;
        _noteService = noteService;
    }

    // ★ WPF 的魔法集合：當裡面的資料增加或減少時，畫面會「自動」更新！
    [ObservableProperty] private ObservableCollection<Notebook> _notebooks = new();
    [ObservableProperty] private string _newNotebookName = string.Empty;
    [ObservableProperty] private Notebook? _selectedNotebook;
    [ObservableProperty] private ObservableCollection<Note> _notes = new();
    [ObservableProperty] private string _newNoteName = string.Empty;
    [ObservableProperty] private Notebook? _selectedNote;

    // 當選擇的筆記本改變時，自動載入該本的筆記
    partial void OnSelectedNotebookChanged(Notebook? value)
    {
        if (value is null) return;
        _ = LoadNotesAsync(value);
    }

    [RelayCommand]
    private async Task LoadNotebooksAsync()
    {
        var data = await _notebookService.GetNotebooksAsync(_currentUserId);

        Notebooks.Clear();
        foreach (var item in data)
        {
            Notebooks.Add(item);
        }
    }
    [RelayCommand]
    private async Task LoadNotesAsync(Notebook? notebook)
    {
        Notes.Clear();
       // SelectedNote = null;
        if (notebook == null) return;

        var data = await _noteService.GetNotesAsync(notebook.Id);

        foreach (var item in data)
        {
            Notes.Add(item);
        }
    }
    // ★ 3. 加上 [RelayCommand] 與對應的方法
    // 點擊「新增」按鈕的邏輯

    [RelayCommand]
    private async Task AddNotebookAsync()
    {
        if (string.IsNullOrWhiteSpace(NewNotebookName)) return;

        // 1. 呼叫 Service 寫入 SQLite
        var createdNotebook = await _notebookService.CreateNotebookAsync(NewNotebookName, _currentUserId);

        // 2. 把新增成功的筆記本塞進集合裡，WPF 畫面就會瞬間多出一筆！
        Notebooks.Insert(0, createdNotebook); // 塞在最上面

        // 3. 清空 TextBox
        NewNotebookName = string.Empty;
    }

    [RelayCommand]
    private async Task AddNoteAsync(string notebookId)
    {
        if (SelectedNotebook == null) return;
        if (SelectedNotebook is null && string.IsNullOrWhiteSpace(NewNoteName)) return;

        var createdNote = await _noteService.CreateNoteAsync(
             SelectedNotebook.Id,
             NewNoteName.Trim());

        Notes.Insert(0, createdNote); // 塞在最上面

        NewNoteName = string.Empty;
       // SelectedNote = created;
    }

    [RelayCommand]
    private void Edit(Notebook notebook)
    {
        notebook.EditingName = notebook.Name; // 預填目前名稱
        notebook.IsEditing = true;
    }

    [RelayCommand]
    private async Task ConfirmRenameAsync(Notebook notebook)
    {
        if (string.IsNullOrWhiteSpace(notebook.EditingName)) return;

        var newName = notebook.EditingName.Trim();
        notebook.IsEditing = false;

        if (notebook.Name == newName) return;

        notebook.Name = newName;

        await _notebookService.UpdateNotebookAsync(notebook);
    }

    [RelayCommand]
    private void CancelRename(Notebook notebook)
    {
        notebook.IsEditing = false;
        notebook.EditingName = string.Empty;
    }

    [RelayCommand]
    private async Task DeleteAsync(Notebook notebook)
    {
        await _notebookService.DeleteNotebookAsync(notebook.Id);
        Notebooks.Remove(notebook);

        // 如果刪的是目前選中的筆記本，清空右側筆記列表
        if (SelectedNotebook == notebook)
        {
            SelectedNotebook = null;
            Notes.Clear();
        }
    }
    [RelayCommand]
    private async Task DeleteNoteAsync(Note note)
    {
        await _noteService.DeleteNoteAsync(note.Id);
        Notes.Remove(note);

        // 如果刪的是目前選中的筆記本，清空右側筆記列表
        //if (SelectedNote == note) { SelectedNote = null; }
            
    }
}
