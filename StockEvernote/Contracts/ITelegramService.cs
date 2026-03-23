namespace StockEvernote.Contracts;
public interface ITelegramService
{
    // 傳入訊息內容，並回傳是否發送成功
    Task<bool> SendMessageAsync(string message);
}