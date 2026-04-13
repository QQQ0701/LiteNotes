using Microsoft.Extensions.DependencyInjection;
using LiteNotes.View;
using LiteNotes.ViewModel;
using System.Windows;

namespace LiteNotes;

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

        _vm.RequestClearPasswords += () => ClearPasswordBoxes();

        _vm.NavigateToNotes = () =>
        {
            var scope = App.ServiceProvider!.CreateScope();
            var notesWindow = scope.ServiceProvider.GetRequiredService<NotesWindow>();

            notesWindow.Closed += (s, e) => scope.Dispose();
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
    private void ClearPasswordBoxes()
    {
        loginPasswordBox.Clear();
        registerPasswordBox.Clear();
        registerConfirmPasswordBox.Clear();
    }
}