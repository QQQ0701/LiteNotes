using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
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

    // 輸入屬性
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _confirmPassword = string.Empty;
    [ObservableProperty] private string? _firstName;
    [ObservableProperty] private string? _lastName;

    // 狀態屬性
    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isLoginMode = true;

    // 事件
    public Action? NavigateToNotes { get; set; }
    public event Action? RequestClearPasswords;

    public LoginViewModel(
        IAuthService authService, 
        IDialogService dialogService,
        IUserSession userSession, 
        ITelegramService gramService, 
        ILogger<LoginViewModel> logger)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _userSession = userSession ?? throw new ArgumentNullException(nameof(userSession));
        _gramService = gramService ?? throw new ArgumentNullException(nameof(gramService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

  

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
        RequestClearPasswords?.Invoke();
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
            if (string.IsNullOrWhiteSpace(Email))
            {
                ErrorMessage = "請輸入電子郵件。";
                return;
            }

            if (!IsValidEmail(Email))
            {
                ErrorMessage = "電子郵件格式不正確，例如：example@gmail.com";
                return;
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "請輸入密碼。";
                return;
            }
            var result = await _authService.LoginAsync(Email, Password);

            if (result.IsSuccess)
            {
                _userSession.LocalId = result.UserId;
                _userSession.IdToken = result.IdToken;

                _logger.LogInformation("使用者登入成功，LocalId：{LocalId}", result.UserId);

                string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                _ = _gramService.SendMessageAsync($"*系統通知*\n您已於 `{time}` 成功登入 StockEvernote！");

                NavigateToNotes?.Invoke();
            }
            else
            {
                ErrorMessage = result.ErrorMessage ?? "登入失敗";
                _logger.LogWarning("使用者登入失敗，Email：{Email}，原因：{Reason}", Email, ErrorMessage);
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
            if (string.IsNullOrWhiteSpace(Email))
            {
                ErrorMessage = "請輸入電子郵件。";
                return;
            }

            if (!IsValidEmail(Email))
            {
                ErrorMessage = "電子郵件格式不正確，例如：example@gmail.com";
                return;
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "請輸入密碼。";
                return;
            }

            if (Password.Length < 6)
            {
                ErrorMessage = "密碼長度至少需要 6 個字元。";
                return;
            }

            if (string.IsNullOrWhiteSpace(ConfirmPassword))
            {
                ErrorMessage = "請輸入確認密碼。";
                return;
            }

            if (Password != ConfirmPassword)
            {
                ErrorMessage = "密碼與確認密碼不一致。";
                return;
            }

            _logger.LogInformation("使用者嘗試註冊新帳號，Email：{Email}", Email);

            var result = await _authService.RegisterAsync(Email, Password);

            if (result.IsSuccess)
            {
                _logger.LogInformation("帳號註冊成功，Email：{Email}", Email);
                _dialogService.ShowMessage("註冊成功", "帳號建立完成，請登入！");
                Email = string.Empty;
                IsLoginMode = true;
                RequestClearPasswords?.Invoke();
            }
            else
            {
                ErrorMessage = result.ErrorMessage ?? "註冊失敗";
                _logger.LogWarning("使用者註冊失敗，Email：{Email}，原因：{Reason}", Email, ErrorMessage);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ForgotPasswordAsync()
    {
        if (string.IsNullOrWhiteSpace(Email))
        {
            ErrorMessage = "請先輸入電子郵件。";
            return;
        }

        if (!IsValidEmail(Email))
        {
            ErrorMessage = "電子郵件格式不正確。";
            return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var result = await _authService.ForgotPasswordAsync(Email);

            if (result.IsSuccess)
                _dialogService.ShowMessage("寄送成功", "密碼重設信已寄出，請檢查您的信箱。");
            else
                ErrorMessage = result.ErrorMessage ?? "發送失敗";
        }
        finally
        {
            IsLoading = false;
        }
    }
    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }
}
