using StockEvernote.Contracts;
using StockEvernote.Model;

namespace EvernoteClone.Mocks;

/// <summary>
/// 模擬身份驗證服務：模擬網路延遲並回傳假使用者資料。
/// </summary>
public class MockAuthService : IAuthService
{
    // 測試專用帳號與密碼
    private const string ValidEmail = "5566@gmail.com";
    private const string ValidPassword = "1234";

    /// <summary>模擬登入：延遲後回傳假使用者。</summary>
    public async Task<AuthResult> LoginAsync(string email, string password)
    {
        await Task.Delay(1000);

        if (email == ValidEmail && password == ValidPassword)
        {
            // 模擬成功，回傳假 ID 與假 Token
            return AuthResult.Success(Guid.NewGuid().ToString(), "fake_jwt_token");
        }

        // 模擬失敗，不拋出 Exception，而是回傳錯誤結果
        return AuthResult.Fail($"帳號或密碼錯誤！(測試請用：{ValidEmail} / {ValidPassword})");
    }

    /// <summary>模擬註冊：延遲後回傳假使用者。</summary>
    public async Task<AuthResult> RegisterAsync(string email, string password)
    {
        await Task.Delay(1000);

        if (email == ValidEmail)
        {
            return AuthResult.Fail("這個 Email 已經被註冊過了喔！");
        }
        return AuthResult.Success(Guid.NewGuid().ToString(), "fake_jwt_token");
    }
}

