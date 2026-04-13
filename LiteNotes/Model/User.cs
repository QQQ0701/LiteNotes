namespace LiteNotes.Model
{
    /// <summary>
    /// 使用者領域模型（純資料實體，不含任何 UI 狀態）。
    /// </summary>
    public class User
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? ProfileImageUrl { get; set; }
    }
}
