using LiteNotes.Contracts;
using LiteNotes.Model;

namespace EvernoteClone.Mocks;

/// <summary>
/// 模擬身份驗證服務：模擬網路延遲並回傳假使用者資料。
/// </summary>
public class MockAuthService : IAuthService
{
    private const string ValidEmail = "5566@gmail.com";
    private const string ValidPassword = "1234";

    /// <summary>模擬登入：延遲後回傳假使用者。</summary>
    public async Task<AuthResult> LoginAsync(string email, string password)
    {
        await Task.Delay(1000);

        if (email == ValidEmail && password == ValidPassword)
        {
            return AuthResult.Success(Guid.NewGuid().ToString(), "fake_jwt_token");
        }
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
    public Task<AuthResult> ForgotPasswordAsync(string email)
    {
        return Task.FromResult(new AuthResult { IsSuccess = true });
    }
}

