using Microsoft.Extensions.DependencyInjection;
using StockEvernote.View;
using StockEvernote.ViewModel;
using System.Windows;

namespace StockEvernote;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class LoginWindow : Window
{
    private readonly LoginViewModel _vm;
    public LoginWindow(LoginViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = _vm;

        _vm.CloseAction = () =>
        {
            var notesWindow = App.ServiceProvider?.GetRequiredService<NotesWindow>();
            notesWindow?.Show();
            this.Close();
        };
    }
    private void RegisterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm != null)
        {
            _vm.Password = registerPasswordBox.Password;
            _vm.ConfirmPassword = registerConfirmPasswordBox.Password;
        }
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm != null)
        {
            _vm.Password = loginPasswordBox.Password;
        }
    }
}