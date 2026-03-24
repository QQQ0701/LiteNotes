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
        var editingNotebook = _viewModel.Notebooks.FirstOrDefault(n => n.IsEditing);
        if (editingNotebook is null) return;

        var clicked = e.OriginalSource as DependencyObject;
        while (clicked != null)
        {
            if (clicked is TextBox) return;
            clicked = VisualTreeHelper.GetParent(clicked);
        }

        _viewModel.ConfirmRenameCommand.Execute(editingNotebook);
    }
}
