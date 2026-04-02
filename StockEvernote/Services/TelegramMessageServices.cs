using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StockEvernote.Contracts;
using StockEvernote.Model.Telegram;
using System.Net.Http;
using System.Net.Http.Json;

namespace StockEvernote.Services;
public class TelegramMessageServices:ITelegramService
{
    private readonly HttpClient _httpClient;
    private readonly string _testChatId;
    private readonly ILogger<TelegramMessageServices> _logger;
    public TelegramMessageServices(HttpClient httpClient, IConfiguration configuration,
        ILogger<TelegramMessageServices> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        _testChatId = configuration["Telegram:TestChatId"] ??
            throw new InvalidOperationException("設定檔中找不到，找不到TestChatId");
        
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    public async Task<bool> SendMessageAsync(string message)
    {
        var requestBody = new TelegramMessageRequest
        {
            Chatid = _testChatId,
            Text = message,
            Parsemode = "Markdown",
        };
        try
        {
            var response = await _httpClient.PostAsJsonAsync("sendMessage", requestBody);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Telegram 訊息發送成功，內容：{Message}", message);
                return true;
            }

            string errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Telegram 發送失敗，HTTP 狀態碼：{StatusCode}，伺服器回應：{Error}", response.StatusCode, errorContent);

            return false;

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "呼叫 Telegram API 時發生未預期的崩潰！");
            return false;
        }
    }
}
