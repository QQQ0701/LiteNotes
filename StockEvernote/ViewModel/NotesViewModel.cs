using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockEvernote.Contracts;
using StockEvernote.Model;
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
    [ObservableProperty] private bool _isAddingNote = false;

    // 當選擇的筆記本改變時，自動載入該本的筆記
    partial void OnSelectedNotebookChanged(Notebook? value)
    {
        if (value is null) return;
        _ = LoadNotesAsync(value.Id);
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
    private async Task LoadNotesAsync(string notebookId)
    {
        var data = await _noteService.GetNotesAsync(notebookId);
        Notes.Clear();
   
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
        // ✅ 改成直接用預設名稱新增，不需要先輸入
        var name = string.IsNullOrWhiteSpace(NewNotebookName) ? "新筆記本" : NewNotebookName.Trim();
        var created = await _notebookService.CreateNotebookAsync(name, _currentUserId);

        Notebooks.Insert(0, created);
        SelectedNotebook = created;

        // ✅ 新增後自動進入編輯模式，讓使用者可以直接改名
        Edit(created);
    }
    [RelayCommand]
    private void StartAddNote()
    {
        if (SelectedNotebook is null) return;
        NewNoteName = string.Empty;
        IsAddingNote = true;
    }
    [RelayCommand]
    private async Task AddNoteAsync(string notebookId)
    {
        if (SelectedNotebook is null) return;

        var name = string.IsNullOrWhiteSpace(NewNoteName) ? "New" : NewNoteName.Trim();
        var created = await _noteService.CreateNoteAsync(name, SelectedNotebook.Id);
        Notes.Insert(0, created);

        IsAddingNote = false;
        NewNoteName = string.Empty;
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

        //如果刪的是目前選中的筆記本，清空右側筆記列表
      //  if (SelectedNote == note) { SelectedNote = null; }

    }
}
