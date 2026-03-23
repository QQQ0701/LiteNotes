using StockEvernote.Contracts;
using System.Windows;

namespace StockEvernote.Services;
/// <summary>
/// WPF 對話框服務：以 MessageBox 顯示訊息。
/// </summary>
public class WpfDialogService : IDialogService
{
    public void ShowMessage(string message, string title = "Info")
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }
}
