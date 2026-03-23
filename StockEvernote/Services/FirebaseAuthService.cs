using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StockEvernote.Contracts;
using StockEvernote.Model;
using StockEvernote.Model.Firebase;
using System.Net.Http;
using System.Net.Http.Json;

namespace StockEvernote.Services;
public class FirebaseAuthService : IAuthService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly ILogger<FirebaseAuthService> _logger;

    public FirebaseAuthService(HttpClient httpClient, IConfiguration configuration, ILogger<FirebaseAuthService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        // 讀取 AppSettings 的環境變數
        _apiKey = configuration["Firebase:ApiKey"]
                  ?? throw new InvalidOperationException("設定檔中找不到 Firebase:ApiKey");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    public async Task<AuthResult> LoginAsync(string email, string password)
    {
        string url = $"v1/accounts:signInWithPassword?key={_apiKey}";
        return await ExecuteAuthRequestAsync(url, email, password);
    }

    public async Task<AuthResult> RegisterAsync(string email, string password)
    {
        string url = $"v1/accounts:signUp?key={_apiKey}";
        return await ExecuteAuthRequestAsync(url, email, password);
    }
    /// <summary>
    /// 共用的 HTTP 請求邏輯與防腐層轉換
    /// </summary>
    private async Task<AuthResult> ExecuteAuthRequestAsync(string url, string email, string password)
    {
        try
        {
            // 1. 封裝發送 DTO
            var requestBody = new FirebaseAuthRequest
            {
                Email = email,
                Password = password,
                ReturnSecureToken = true
            };

            // 2. 發送 API 請求
            var response = await _httpClient.PostAsJsonAsync(url, requestBody);

            // 3. 處理成功回應
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<FirebaseAuthResponse>();

                if (result != null && !string.IsNullOrEmpty(result.LocalId) && !string.IsNullOrEmpty(result.IdToken))
                {
                    return AuthResult.Success(result.LocalId, result.IdToken);
                }
                return AuthResult.Fail("API 回傳成功，但缺少必要的識別資訊。");
            }

            // 4. 處理失敗回應 (HTTP 400)
            var errorResult = await response.Content.ReadFromJsonAsync<FirebaseErrorResponse>();
            string errorMessage = errorResult?.Error?.Message ?? "發生未知的驗證錯誤。";

            return AuthResult.Fail(TranslateFirebaseError(errorMessage));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "呼叫 Firebase API 時發生網路連線失敗。Email: {Email}", email);
            // 攔截網路斷線等底層例外，轉換為業務錯誤結果
            return AuthResult.Fail("網路連線失敗，請檢查您的網路狀態。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "呼叫 Firebase API 時發生未預期的系統崩潰！Email: {Email}", email);
            return AuthResult.Fail($"發生未預期的錯誤：{ex.Message}");
        }
    }
    /// <summary>
    /// 將 Firebase 英文錯誤代碼轉譯為使用者友善的中文訊息
    /// </summary>
    private string TranslateFirebaseError(string errorCode)
    {
        return errorCode switch
        {
            "EMAIL_NOT_FOUND" => "找不到此電子郵件的帳號。",
            "INVALID_PASSWORD" => "密碼錯誤。",
            "USER_DISABLED" => "此帳號已被停用。",
            "EMAIL_EXISTS" => "此電子郵件已被註冊。",
            "INVALID_LOGIN_CREDENTIALS" => "登入憑證無效或帳號密碼錯誤。",
            _ => $"驗證失敗 ({errorCode})"
        };
    }
}
