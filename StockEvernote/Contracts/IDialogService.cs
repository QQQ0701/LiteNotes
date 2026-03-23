namespace StockEvernote.Contracts;
/// <summary>
/// 對話框服務介面：抽象 UI 訊息顯示。
/// </summary>
public interface IDialogService
{
    void ShowMessage(string message, string title = "Info");
}
