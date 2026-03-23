using StockEvernote.Model;

namespace StockEvernote.Contracts;
/// <summary>
/// 身份驗證服務介面：定義登入與註冊方法。
/// </summary>
public interface IAuthService
{
    Task<AuthResult> LoginAsync(string email, string password);
    Task<AuthResult> RegisterAsync(string email, string password);
}
