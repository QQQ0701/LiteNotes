namespace StockEvernote.Model;
/// <summary>
/// 封裝身分驗證操作的結果，取代拋出 Exception。
/// </summary>
public class AuthResult
{
    public bool IsSuccess { get; set; }
    public string? UserId { get; set; } // Firebase 回傳的 localId
    public string? IdToken { get; set; } // 用於後續 API 授權的 Token
    public string? ErrorMessage { get; set; }

    public static AuthResult Success(string userId, string idToken) =>
        new AuthResult { IsSuccess = true, UserId = userId, IdToken = idToken };

    public static AuthResult Fail(string errorMessage) =>
        new AuthResult { IsSuccess = false, ErrorMessage = errorMessage };
}

