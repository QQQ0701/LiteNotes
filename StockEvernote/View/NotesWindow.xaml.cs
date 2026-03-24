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
        _viewModel.RequestFocusNotebook += OnRequestFocusNotebook;
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

            _viewModel.ConfirmRenameCommand.Execute(notebook);
        }
    }

    private void MenuItem_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void NewNoteTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _viewModel.AddNoteCommand.Execute(null);
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // 找到目前正在編輯的筆記本
        var editingNotebook = _viewModel.Notebooks.FirstOrDefault(n => n.IsEditing);
        if (editingNotebook is null) return;

        // ✅ 往上找點擊的元素是否在 TextBox 裡面，是的話就不觸發
        var clicked = e.OriginalSource as DependencyObject;
        while (clicked != null)
        {
            if (clicked is TextBox) return; // 點在 TextBox 裡面，不確認
            clicked = VisualTreeHelper.GetParent(clicked);
        }

        // 點在 TextBox 以外的地方，確認輸入
        _viewModel.ConfirmRenameCommand.Execute(editingNotebook);
    }
    private void OnRequestFocusNotebook(Notebook notebook)
    {
        // 等 UI 完全渲染完畢後再找 TextBox 並 Focus
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
        {
            foreach (var item in FindVisualChildren<TextBox>(this))
            {
                if (item.DataContext == notebook && item.Visibility == Visibility.Visible)
                {
                    item.Focus();
                    item.SelectAll();
                    break;
                }
            }
        });
    }
    private async void RenameTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.Visibility == Visibility.Visible)
        {
            await Task.Delay(50);
            tb.Focus();
            Keyboard.Focus(tb);
            tb.SelectAll();
        }
    }
    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) yield return t;
            foreach (var grandChild in FindVisualChildren<T>(child))
                yield return grandChild;
        }
    }
}
