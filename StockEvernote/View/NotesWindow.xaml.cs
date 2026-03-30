using StockEvernote.Model;
using StockEvernote.ViewModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace StockEvernote.View;

/// <summary>
/// Interaction logic for NotesWindow1.xaml
/// </summary>
public partial class NotesWindow : Window
{
    NotesViewModel _viewModel;

    public NotesWindow(NotesViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        this.Loaded += NotesWindow_Loaded;

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(NotesViewModel.SelectedNote))
                LoadRichTextContent(_viewModel.SelectedNote?.Content ?? string.Empty);
        };
    }
   

    private async void NotesWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadNotebooksCommand.ExecuteAsync(null);
    }
    private void RenameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is Notebook notebook)
        {
            // 檢查新的焦點是否還在同一個 TextBox 內，避免重複觸發
            var focusedElement = FocusManager.GetFocusedElement(this);
            if (focusedElement == tb) return;

            _viewModel.ConfirmNotebookRenameCommand.Execute(notebook);
        }
    }
    // ✅ 新增：Note 的 LostFocus
    private void NoteRenameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is Note note)
        {
            var focusedElement = FocusManager.GetFocusedElement(this);
            if (focusedElement == tb) return;
            _viewModel.ConfirmNoteRenameCommand.Execute(note);
        }
    }

    private void MenuItem_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var editingNotebook = _viewModel.Notebooks.FirstOrDefault(n => n.IsEditing);
        if (editingNotebook is null) return;

        var clicked = e.OriginalSource as DependencyObject;
        while (clicked != null)
        {
            if (clicked is TextBox) return;
            clicked = VisualTreeHelper.GetParent(clicked);
        }
        _viewModel.ConfirmNotebookRenameCommand.Execute(editingNotebook);
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.NoteContent = GetRichTextContent();
        await _viewModel.SaveNoteCommand.ExecuteAsync(null);
        MessageBox.Show("儲存成功", "提示" ,MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private string GetRichTextContent()
    {
        var textRange = new System.Windows.Documents.TextRange(
            contentRichTextBox.Document.ContentStart,
            contentRichTextBox.Document.ContentEnd);

        using var stream = new System.IO.MemoryStream();
        textRange.Save(stream, System.Windows.DataFormats.Rtf);

        stream.Position = 0; // 把讀取游標拉回起點
        using var reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd(); // 這樣讀最安全，絕對沒有編碼亂碼！
    }

    private void LoadRichTextContent(string content)
    {
        contentRichTextBox.Document.Blocks.Clear();
        if (string.IsNullOrEmpty(content)) return;

        try
        {
            var textRange = new System.Windows.Documents.TextRange(
                contentRichTextBox.Document.ContentStart,
                contentRichTextBox.Document.ContentEnd);

            using var stream = new System.IO.MemoryStream(
                System.Text.Encoding.ASCII.GetBytes(content)); // ✅ RTF 用 ASCII

            textRange.Load(stream, System.Windows.DataFormats.Rtf);
        }
        catch
        {
            // 舊資料不是 RTF 格式就當純文字顯示
            contentRichTextBox.AppendText(content);
        }
    }
}
