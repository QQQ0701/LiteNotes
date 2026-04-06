using Microsoft.Extensions.DependencyInjection;
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

namespace StockEvernote.View;

/// <summary>
/// Interaction logic for NotesWindow1.xaml
/// </summary>
public partial class NotesWindow : Window
{
    private readonly NotesViewModel _vm;
    private Note? _previousNote;
    private string _originalNoteTitle = string.Empty;
    private bool _isInternalLoading = false;

    private System.Threading.Timer? _searchTimer;
    private System.Threading.Timer? _autoSaveTimer;

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
        _vm.ShowErrorDialogAction = (title, message) =>
        {
            // 使用 Windows 內建的標準錯誤彈窗
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        };
    }

    // ══════════════════════════════════════════════════════════
    // 視窗生命週期（啟動 / 關閉）
    // ══════════════════════════════════════════════════════════
    private async void NotesWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _vm.InitializeInfrastructureCommand.ExecuteAsync(null);
        // 🔧 開發階段：透過開關控制是否啟動同步
        // 📌 正式上線：刪除 if 判斷，讓 try-catch 區塊直接執行
        if (_vm.IsAutoSyncEnabled)
        {
            try
            {
                // 先推本地變更 → 再拉雲端最新（順序不能反，避免已刪除的資料被復活）
                _vm.StatusMessage = "⏳ 上傳本地變更...";
                await _vm.SyncCommand.ExecuteAsync(null);

                _vm.StatusMessage = "⏳ 從雲端同步中...";
                await _vm.RestoreFromCloudCommand.ExecuteAsync(null);
            }
            catch
            {
                _vm.StatusMessage = "❌ 啟動同步失敗，使用本地資料";
            }
        }

        await _vm.LoadNotebooksCommand.ExecuteAsync(null);
        await _vm.RebuildSearchIndexCommand.ExecuteAsync(null);
    }
    protected override async void OnClosing(CancelEventArgs e)
    {
        // 強制儲存當前正在編輯的筆記
        if (_autoSaveTimer != null && _vm.SelectedNote != null)
        {
            _autoSaveTimer.Dispose();
            _autoSaveTimer = null;
            SaveNote(_vm.SelectedNote);
        }

        // 🔧 開發階段：透過開關控制是否關閉前同步
        // 📌 正式上線：刪除 if 判斷，讓 try-catch 區塊直接執行
        if (_vm.IsAutoSyncEnabled)
        {
            try
            {
                _vm.StatusMessage = "⏳ 關閉前同步中...";
                await _vm.SyncCommand.ExecuteAsync(null);
            }
            catch
            {
                // 同步失敗不阻擋關閉，下次啟動會再同步
            }
        }

        base.OnClosing(e);
    }

    // ══════════════════════════════════════════════════════════
    //  筆記切換與 RTF 載入
    // ══════════════════════════════════════════════════════════
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
    private void LoadRichTextContent()
    {
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
            _isInternalLoading = false;
        }
    }

    // ══════════════════════════════════════════════════════════
    //  自動儲存（停止輸入 1.5 秒後觸發）
    // ══════════════════════════════════════════════════════════
    private void ContentRichTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_vm.SelectedNote is null || _isInternalLoading) return;

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

    // ══════════════════════════════════════════════════════════
    //  筆記標題編輯：防空白 + 自動儲存
    // ══════════════════════════════════════════════════════════

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

    // ══════════════════════════════════════════════════════════
    // 搜尋防抖
    // ══════════════════════════════════════════════════════════

    /// <summary>搜尋列文字變更：延遲 300ms 後觸發搜尋</summary>
    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchTimer?.Dispose();
        _searchTimer = new System.Threading.Timer(_ =>
        {
            Dispatcher.Invoke(() =>
            {
                if (_vm.SearchCommand.CanExecute(null))
                    _vm.SearchCommand.Execute(null);
            });
        }, null, 300, System.Threading.Timeout.Infinite);
    }

    // ══════════════════════════════════════════════════════════
    //  重新命名 TextBox 自動聚焦
    // ══════════════════════════════════════════════════════════

    /// <summary>TextBox 變成可見時聚焦（右鍵重新命名用）</summary>
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

    /// <summary>TextBox Loaded 時聚焦（新增項目用，優先級更低確保贏過 ListView）</summary>
    private void AutoSelectTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.IsVisible)
        {
            // ContextIdle：保證 WPF 把畫面上的東西都排版完、渲染完，才執行這段
            tb.Dispatcher.BeginInvoke(new Action(() =>
            {
                tb.Focus();
                Keyboard.Focus(tb);
                tb.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
    }

    // ══════════════════════════════════════════════════════════
    // 重新命名：點擊空白處確認
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// 攔截滑鼠預覽點擊事件，用於在點擊非編輯區域時，自動結束並確認重新命名的編輯狀態。
    /// </summary>
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
        FocusManager.SetFocusedElement(this, null);

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

    // ══════════════════════════════════════════════════════════
    //  LostFocus 備援確認（焦點自然轉移時觸發）
    // ══════════════════════════════════════════════════════════

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

    // ══════════════════════════════════════════════════════════
    //  附件上傳
    // ══════════════════════════════════════════════════════════

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedNote is null) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "選擇要上傳的檔案",
            Filter = "圖片檔案|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.webp"
                   + "|文件檔案|*.pdf;*.docx;*.xlsx;*.pptx;*.txt;*.csv"
                   + "|所有支援的檔案|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.webp;*.pdf;*.docx;*.xlsx;*.pptx;*.txt;*.csv",
            FilterIndex = 3
        };

        if (dialog.ShowDialog() == true)
        {
            await _vm.UploadFileCommand.ExecuteAsync(dialog.FileName);
        }
    }

    /// <summary>處理使用者將檔案拖曳進編輯區的事件</summary>
    private async void Grid_Drop(object sender, DragEventArgs e)
    {
        // 1. 防呆：必須要有選中的筆記才能上傳
        if (_vm.SelectedNote is null) return;

        // 2. 檢查拖進來的東西是不是「檔案」
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            // 3. 把檔案路徑拿出來 (可能有一次拖很多個檔案，這是一個陣列)
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);

            // 4. 把每個檔案丟給 ViewModel 去上傳
            foreach (var filePath in files)
            {
                // 檢查指令是否允許執行，然後非同步上傳
                if (_vm.UploadFileCommand.CanExecute(filePath))
                {
                    await _vm.UploadFileCommand.ExecuteAsync(filePath);
                }
            }
        }
    }

    /// <summary>攔截 RichTextBox 的拖曳經過，強制顯示「複製」的游標</summary>
    private void ContentRichTextBox_PreviewDragOver(object sender, DragEventArgs e)
    {
        // 只有當拖進來的是「檔案」時，我們才介入
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true; // 🌟 告訴 RichTextBox：「這件事我接管了，不要顯示禁止圖示！」
        }
    }

    /// <summary>攔截 RichTextBox 的放下事件，執行我們的上傳邏輯</summary>
    private async void ContentRichTextBox_PreviewDrop(object sender, DragEventArgs e)
    {
        if (_vm.SelectedNote is null) return;

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Handled = true; // 🌟 告訴 RichTextBox：「檔案我拿走了，你不要把它變成純文字貼上！」

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);

            foreach (var filePath in files)
            {
                if (_vm.UploadFileCommand.CanExecute(filePath))
                {
                    await _vm.UploadFileCommand.ExecuteAsync(filePath);
                }
            }
        }
    }

    // ══════════════════════════════════════════════════════════
    //  其他
    // ══════════════════════════════════════════════════════════

    private void MenuItem_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    // ══════════════════════════════════════════════════════════════
    //  📌 正式上線 Checklist
    // ══════════════════════════════════════════════════════════════
    //
    //  1. ViewModel：刪除 [ObservableProperty] private bool _isAutoSyncEnabled
    //  2. Code-Behind：NotesWindow_Loaded → 刪除 if (_vm.IsAutoSyncEnabled)，保留 try-catch 內容
    //  3. Code-Behind：OnClosing → 刪除 if (_vm.IsAutoSyncEnabled)，保留 try-catch 內容
    //  4. XAML：StatusBar → 刪除自動同步的 CheckBox
    //
    // ══════════════════════════════════════════════════════════════

}
