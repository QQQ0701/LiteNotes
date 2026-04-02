using StockEvernote.Model;

namespace StockEvernote.Contracts;

/// <summary>
/// 全文檢索服務介面：負責管理 SQLite FTS5 (Full-Text Search) 虛擬資料表，並提供高效能的筆記內容搜尋。
/// </summary>
public interface ISearchService
{
    /// <summary>建立或重建 FTS5 索引資料表</summary>
    Task InitializeAsync();

    /// <summary>新增或更新單筆筆記的索引</summary>
    Task IndexNoteAsync(string noteId, string noteName, string content, string notebookId, string notebookName);

    /// <summary>刪除單筆筆記的索引</summary>
    Task RemoveNoteIndexAsync(string noteId);

    /// <summary>全文搜尋</summary>
    Task<List<SearchResult>> SearchAsync(string keyword, string userId, string? notebookId = null);

    /// <summary>重建所有索引（啟動時或資料還原後呼叫）</summary>
    Task RebuildIndexAsync(string userId);
}
