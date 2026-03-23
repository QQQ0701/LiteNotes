using StockEvernote.Model;
using StockEvernote.ViewModel;
using System.Windows;
using System.Windows.Controls;

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
            _viewModel.ConfirmRenameCommand.Execute(notebook);
        }
    }
}
