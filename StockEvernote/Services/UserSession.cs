using StockEvernote.Contracts;

namespace StockEvernote.Services;

/// <summary>
/// 實作全域使用者狀態（將在 DI 容器中註冊為 Singleton 單例）。
/// </summary>
public class UserSession : IUserSession
{
    public string? LocalId { get; set; }
    public string? IdToken { get; set; }
    public bool IsLoggedIn => !string.IsNullOrEmpty(IdToken);
    public void Clear()
    {
        LocalId = null;
        IdToken = null;
    }
}