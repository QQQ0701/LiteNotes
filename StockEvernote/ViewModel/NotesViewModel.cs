using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockEvernote.Contracts;
using StockEvernote.Model;
using System.Collections.ObjectModel;

namespace StockEvernote.ViewModel;

public partial class NotesViewModel:ObservableObject
{
    private readonly INotebookService _notebookService;
    private readonly string _currentUserId = "TestUser123";
    public NotesViewModel(INotebookService notebookService)
    {
        _notebookService = notebookService;
    }

    // ★ WPF 的魔法集合：當裡面的資料增加或減少時，畫面會「自動」更新！
    [ObservableProperty] private ObservableCollection<Notebook> _notebooks = new();
    // 綁定到 UI 上 TextBox 的文字
    [ObservableProperty] private string _newNotebookName = string.Empty;

    [ObservableProperty] private Notebook? _selectedNotebook;


    // ★ 3. 加上 [RelayCommand] 與對應的方法
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
    }
}
