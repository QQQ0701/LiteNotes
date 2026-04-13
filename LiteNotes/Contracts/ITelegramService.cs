namespace LiteNotes.Contracts;
public interface ITelegramService
{
    Task<bool> SendMessageAsync(string message);
}