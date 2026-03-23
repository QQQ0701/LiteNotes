using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using StockEvernote.Contracts;

namespace StockEvernote.ViewModel;

/// <summary>
/// 登入頁面 ViewModel，注入身份驗證與對話框服務。
/// </summary>
public partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IDialogService _dialogService;
    private readonly IUserSession _userSession;
    private readonly ITelegramService _gramService;
    private readonly ILogger<LoginViewModel> _logger;
    public Action? CloseAction { get; set; }
    public LoginViewModel(IAuthService authService, IDialogService dialogService,
        IUserSession userSession, ITelegramService gramService, ILogger<LoginViewModel> logger)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _userSession = userSession ?? throw new ArgumentNullException(nameof(userSession));
        _gramService = gramService ?? throw new ArgumentNullException(nameof(gramService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // 輸入屬性
    [ObservableProperty] private string email = string.Empty;
    [ObservableProperty] private string password = string.Empty;
    [ObservableProperty] private string confirmPassword = string.Empty;
    [ObservableProperty] private string? firstName;
    [ObservableProperty] private string? lastName;

    // 狀態屬性
    [ObservableProperty] private bool isLoading = false;
    [ObservableProperty] private string errorMessage = string.Empty;
    [ObservableProperty] private bool isLoginMode = true;


    /// <summary>
    /// 切換登入/註冊模式指令。
    /// </summary>
    [RelayCommand]
    private void ToggleMode()
    {
        IsLoginMode = !IsLoginMode;
        ErrorMessage = string.Empty;
        Email = string.Empty;
        Password = string.Empty;
        ConfirmPassword = string.Empty;
        FirstName = string.Empty;
        LastName = string.Empty;
    }

    /// <summary>
    /// 登入指令：防止重複點擊，錯誤處理與狀態切換。
    /// </summary>
    [RelayCommand]
    private async Task LoginAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "請輸入電子郵件與密碼。";
                return;
            }
            var result = await _authService.LoginAsync(Email, Password);

            if (result.IsSuccess)
            {
                // TODO: 可將 result.UserId 存入全域狀態或傳遞給 NotesWindow
                _userSession.LocalId = result.UserId;
                _userSession.IdToken = result.IdToken;

                _logger.LogInformation($"使用者登入成功！LocalId: {result.UserId}");

                string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                _ = _gramService.SendMessageAsync($"*系統通知*\n您已於 `{time}` 成功登入 StockEvernote！");

                _dialogService.ShowMessage("登入成功", "歡迎回來！");
                
                // 導向主畫面（NotesWindow）
                CloseAction?.Invoke();
            }
            else
            {
                ErrorMessage = result.ErrorMessage ?? "登入失敗";
                _logger.LogWarning($"使用者登入失敗。Email: {Email}, 原因: {ErrorMessage}");
                _dialogService.ShowMessage("錯誤", ErrorMessage);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 註冊指令：驗證密碼、錯誤處理與狀態切換。
    /// </summary>
    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password) || string.IsNullOrWhiteSpace(ConfirmPassword))
            {
                ErrorMessage = "請完整填寫註冊資訊。";
                return;
            }
            if (Password != ConfirmPassword)
            {
                ErrorMessage = "密碼與確認密碼不一致。";
                return;
            }
            _logger.LogInformation("使用者嘗試註冊新帳號。Email: {Email}", Email);//  記錄行為

            var result = await _authService.RegisterAsync(Email, Password);

            if (result.IsSuccess)
            {
                _logger.LogInformation($"帳號註冊成功！Email: {Email}");
                _dialogService.ShowMessage("註冊成功", "帳號建立完成！");
                CloseAction?.Invoke();
            }
            else
            {
                ErrorMessage = result.ErrorMessage ?? "註冊失敗";
                _logger.LogWarning($"使用者註冊失敗。Email: {Email}, 原因: {ErrorMessage}" );
                _dialogService.ShowMessage("錯誤", ErrorMessage);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }
}
