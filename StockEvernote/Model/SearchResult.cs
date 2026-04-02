namespace StockEvernote.Model;

/// <summary>
/// 搜尋結果 DTO，用來顯示在搜尋結果列表
/// </summary>
public class SearchResult
{
    public string NoteId { get; set; } = string.Empty;
    public string NoteName { get; set; } = string.Empty;
    public string NotebookName { get; set; } = string.Empty;
    public string NotebookId { get; set; } = string.Empty;

    /// <summary>FTS5 產生的高亮預覽文字，包含前後文與匹配標籤</summary>
    public string MatchSnippet { get; set; } = string.Empty;
}