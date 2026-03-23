namespace StockEvernote.Model
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
        //要把時間顯示在 UI 上給客人看的時候，才轉回他當地的時間（例如 ToLocalTime()

        public string? ProfileImageUrl { get; set; }
    }
}
