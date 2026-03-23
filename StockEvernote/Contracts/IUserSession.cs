namespace StockEvernote.Contracts;

/// <summary>
/// 全域使用者狀態合約：負責保管登入後的 Token 與識別碼。
/// </summary>
public interface IUserSession
{
    string? LocalId { get; set; }
    string? IdToken { get; set; }

    // 判斷當前是否處於登入狀態
    bool IsLoggedIn { get; }

    // 登出時呼叫，清空所有機密資料
    void Clear();
}