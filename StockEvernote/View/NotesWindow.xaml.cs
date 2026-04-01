using Microsoft.Extensions.DependencyInjection;
using StockEvernote.Model;
using StockEvernote.ViewModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

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
    private string _originalNoteTitle = string.Empty;

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

    // 負責 1：當 TextBox 出現時，強制把游標塞進去並全選文字
    private void AutoSelectTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox tb && (bool)e.NewValue == true)
        {
            tb.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (tb.IsVisible) // 再次確認還是可見的（防止快速切換）
                {
                    tb.Focus();
                    Keyboard.Focus(tb);
                    tb.SelectAll();
                }
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }
    // ──────────────────────────────────────────────
    // 全域焦點守衛：只負責「強制失焦」，不碰任何業務邏輯
    // ──────────────────────────────────────────────
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // 1. Fail-fast：找出所有正在編輯的項目
        var editingNotebook = _vm.Notebooks?.FirstOrDefault(n => n.IsEditing);
        Note? editingNote = null;

        if (editingNotebook == null)
            editingNote = _vm.Notes?.FirstOrDefault(n => n.IsEditing);

        var focusedElement = Keyboard.FocusedElement as TextBox;

        if (editingNotebook == null && editingNote == null
            && focusedElement == null) return;

        // 2. 檢查點擊目標是否為「正在編輯的那個 TextBox」
        var clicked = e.OriginalSource as DependencyObject;
        while (clicked != null)
        {
            if (clicked is not Visual && clicked is not System.Windows.Media.Media3D.Visual3D)
                break;

            if (clicked is TextBox tb)
            {
                // 精確比對 DataContext：只有點到自己正在編輯的 TextBox 才放行
                var dc = tb.DataContext;
                if (dc == editingNotebook || dc == editingNote ||
                    tb == focusedElement)
                {
                    return; // 點的是正在編輯的 TextBox，不介入
                }
                break; // 點到其他 TextBox（搜尋列、標題等），跳出迴圈執行確認
            }
            clicked = VisualTreeHelper.GetParent(clicked);
        }
        Keyboard.ClearFocus();

        // 3. 執行確認指令
        if (editingNotebook != null)
        {
            _vm.ConfirmNotebookRenameCommand.Execute(editingNotebook);
        }

        if (editingNote != null)
        {
            _vm.ConfirmNoteRenameCommand.Execute(editingNote);
        }
    }

    // ──────────────────────────────────────────────
    // Notebook 重新命名：LostFocus 事件處理
    // ──────────────────────────────────────────────
    private void NotebookRenameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is Notebook notebook)
        {
            if (!notebook.IsEditing) return;

            if (tb.Tag is NotesViewModel vm &&
                vm.ConfirmNotebookRenameCommand.CanExecute(notebook))
            {
                vm.ConfirmNotebookRenameCommand.Execute(notebook);
            }
        }
    }

    // ──────────────────────────────────────────────
    // Note 重新命名：LostFocus 事件處理
    // ──────────────────────────────────────────────
    private void NoteRenameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is Note note)
        {
            if (!note.IsEditing) return;

            if (tb.Tag is NotesViewModel vm &&
                vm.ConfirmNoteRenameCommand.CanExecute(note))
            {
                vm.ConfirmNoteRenameCommand.Execute(note);
            }
        }
    }
    //----------------------------------------------------------------------
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
        if (_vm.SelectedNote is null || _isInternalLoading) return;

        // 1. 攔截：如果使用者把標題刪光了，立刻銷毀計時器，絕對不准啟動自動儲存！
        if (string.IsNullOrWhiteSpace(_vm.SelectedNote.Name))
        {
            _autoSaveTimer?.Dispose();
            _autoSaveTimer = null;
            return;
        }

        // 2. 放行：如果標題不是空白的（使用者有打字），才去呼叫下方的方法啟動 1.5 秒存檔
        ContentRichTextBox_TextChanged(sender, e);
    }

  
    private void TitleTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedNote != null)
        {
            _originalNoteTitle = _vm.SelectedNote.Name;
        }
    }

    private void TitleTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedNote == null) return;

        // 🌟 核心防呆：如果名字變成空白的，直接拿剛才背起來的原名覆蓋回去！
        if (string.IsNullOrWhiteSpace(_vm.SelectedNote.Name))
        {
            _vm.SelectedNote.Name = _originalNoteTitle;

            // 順便把 1.5 秒的存檔計時器停掉，避免存入空白資料
            _autoSaveTimer?.Dispose();
            _autoSaveTimer = null;
        }
        else if (_vm.SelectedNote.Name != _originalNoteTitle)
        {
            // 如果使用者有乖乖打上新名字，就更新我們的記憶
            _originalNoteTitle = _vm.SelectedNote.Name;
        }
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
