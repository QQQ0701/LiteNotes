using StockEvernote.Model;
using StockEvernote.ViewModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;

namespace StockEvernote.View;

/// <summary>
/// Interaction logic for NotesWindow1.xaml
/// </summary>
public partial class NotesWindow : Window
{
    private System.Threading.Timer? _autoSaveTimer;
    private readonly NotesViewModel _vm;
    private Note? _previousNote;
    private bool _isInternalLoading = false;

    public NotesWindow(NotesViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = _vm;

        this.Loaded += NotesWindow_Loaded;

        _vm.PropertyChanged += NotesViewModel_PropertyChanged;

        _vm.LogoutAction = () =>
        {
            var scope = App.ServiceProvider!.CreateScope();
            var loginWindow = scope.ServiceProvider.GetRequiredService<LoginWindow>();
            loginWindow.Closed += (s, e) => scope.Dispose();
            loginWindow.Show();
            this.Close();
        };
    }
   

    // 自動儲存
    private void ContentRichTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_vm.SelectedNote is null || _isInternalLoading) return;

        // 每次輸入都重置計時器，停止輸入 1.5 秒後才儲存
        _autoSaveTimer?.Dispose();
        _autoSaveTimer = new System.Threading.Timer(_ =>
        {
            Dispatcher.Invoke(() => SaveNote(_vm.SelectedNote));
        }, null, 1500, System.Threading.Timeout.Infinite);
    }

    private void SaveNote(Note? note)
    {
        if (note is null) return;

        var range = new TextRange(
            contentRichTextBox.Document.ContentStart,
            contentRichTextBox.Document.ContentEnd);

        using var stream = new MemoryStream();
        range.Save(stream, DataFormats.Rtf);
        note.Content = Encoding.ASCII.GetString(stream.ToArray());

        _ = _vm.SaveSpecificNoteCommand.ExecuteAsync(note);
    }

    // 切換筆記
    private void NotesViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_vm.SelectedNote))
        {
            // ✅ 切換前先儲存上一篇
            if (_autoSaveTimer != null && _previousNote != null)
            {
                _autoSaveTimer.Dispose();
                _autoSaveTimer = null;
                SaveNote(_previousNote);
            }

            _previousNote = _vm.SelectedNote;
            LoadRichTextContent();
        }
    }

    // 載入筆記內容
    private void LoadRichTextContent()
    {
        // ✅ 鎖定旗標，防止 Load 觸發 TextChanged 啟動自動儲存
        _isInternalLoading = true;

        try
        {
            contentRichTextBox.Document.Blocks.Clear();

            if (string.IsNullOrEmpty(_vm.SelectedNote?.Content)) return;

            var bytes = Encoding.ASCII.GetBytes(_vm.SelectedNote.Content);
            using var stream = new MemoryStream(bytes);
            var range = new TextRange(
                contentRichTextBox.Document.ContentStart,
                contentRichTextBox.Document.ContentEnd);

            range.Load(stream, DataFormats.Rtf);
        }
        catch
        {
            // 舊資料不是 RTF 格式就當純文字顯示
            contentRichTextBox.Document.Blocks.Clear();
        }
        finally
        {
            // ✅ 解鎖
            _isInternalLoading = false;
        }
    }

    // ❌ 關閉視窗
    protected override void OnClosing(CancelEventArgs e)
    {
        // ✅ 關閉前強制儲存
        if (_autoSaveTimer != null && _vm.SelectedNote != null)
        {
            _autoSaveTimer.Dispose();
            _autoSaveTimer = null;
            SaveNote(_vm.SelectedNote);
        }

        base.OnClosing(e);
    }
    private void RenameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is Notebook notebook)
        {
            // 檢查新的焦點是否還在同一個 TextBox 內，避免重複觸發
            var focusedElement = FocusManager.GetFocusedElement(this);
            if (focusedElement == tb) return;

            _vm.ConfirmNotebookRenameCommand.Execute(notebook);
        }
    }
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var editingNotebook = _vm.Notebooks.FirstOrDefault(n => n.IsEditing);
        if (editingNotebook is null) return;

        var clicked = e.OriginalSource as DependencyObject;
        while (clicked != null)
        {
            if (clicked is TextBox) return;
            clicked = VisualTreeHelper.GetParent(clicked);
        }
        _vm.ConfirmNotebookRenameCommand.Execute(editingNotebook);
    }
    private void NoteRenameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is Note note)
        {
            var focusedElement = FocusManager.GetFocusedElement(this);
            if (focusedElement == tb) return;
            _vm.ConfirmNoteRenameCommand.Execute(note);
        }
    }
    private async void NotesWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _vm.LoadNotebooksCommand.ExecuteAsync(null);
    }
    private void MenuItem_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void TitleTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ContentRichTextBox_TextChanged(sender, e);
    }





    // ✅ 新增：Note 的 LostFocus




    //private async void SaveButton_Click(object sender, RoutedEventArgs e)
    //{
    //    _vm.NoteContent = GetRichTextContent();
    //    await _vm.SaveNoteCommand.ExecuteAsync(null);
    //    MessageBox.Show("儲存成功", "提示" ,MessageBoxButton.OK, MessageBoxImage.Information);
    //}
    //private string GetRichTextContent()
    //{
    //    var textRange = new System.Windows.Documents.TextRange(
    //        contentRichTextBox.Document.ContentStart,
    //        contentRichTextBox.Document.ContentEnd);

    //    using var stream = new System.IO.MemoryStream();
    //    textRange.Save(stream, System.Windows.DataFormats.Rtf);

    //    stream.Position = 0; // 把讀取游標拉回起點
    //    using var reader = new System.IO.StreamReader(stream);
    //    return reader.ReadToEnd(); // 這樣讀最安全，絕對沒有編碼亂碼！
    //}

    //private void LoadRichTextContent(string content)
    //{
    //    contentRichTextBox.Document.Blocks.Clear();
    //    if (string.IsNullOrEmpty(content)) return;

    //    try
    //    {
    //        var textRange = new System.Windows.Documents.TextRange(
    //            contentRichTextBox.Document.ContentStart,
    //            contentRichTextBox.Document.ContentEnd);

    //        using var stream = new System.IO.MemoryStream(
    //            System.Text.Encoding.ASCII.GetBytes(content)); // ✅ RTF 用 ASCII

    //        textRange.Load(stream, System.Windows.DataFormats.Rtf);
    //    }
    //    catch
    //    {
    //        // 舊資料不是 RTF 格式就當純文字顯示
    //        contentRichTextBox.AppendText(content);
    //    }
    //}
}
