namespace LiteNotes.Contracts;

/// <summary>
/// 全域使用者狀態合約：負責保管登入後的 Token 與識別碼。
/// </summary>
public interface IUserSession
{
    string? LocalId { get; set; }
    string? IdToken { get; set; }
    bool IsLoggedIn { get; }
    void Clear();
}