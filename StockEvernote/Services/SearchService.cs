using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockEvernote.Contracts;
using StockEvernote.Data;
using StockEvernote.Helpers;
using StockEvernote.Model;

namespace StockEvernote.Services;

/// <summary>
/// FTS5 全文搜尋服務：管理搜尋索引的生命週期（初始化、建立、刪除、重建），
/// 並提供中文友善的全文檢索查詢。
/// </summary>
/// <remarks>
/// 中文處理策略：在漢字間插入空格，讓 FTS5 的 unicode61 tokenizer 能逐字斷詞。
/// 搜尋結果顯示時再還原回正常中文。
/// </remarks>
public class SearchService : ISearchService
{
    private readonly EvernoteDbContext _dbContext;
    private readonly ILogger<SearchService> _logger;

    public SearchService(EvernoteDbContext dbContext, ILogger<SearchService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>建立 FTS5 虛擬資料表（若不存在）。啟動時呼叫一次。</summary>
    public async Task InitializeAsync()
    {
        var sql = @"
            CREATE VIRTUAL TABLE IF NOT EXISTS NotesIndex USING fts5(
                note_id UNINDEXED,
                notebook_id UNINDEXED,
                note_name,
                notebook_name,
                content,
                tokenize='unicode61'
            );";

        await _dbContext.Database.ExecuteSqlRawAsync(sql);
        _logger.LogInformation("FTS5 索引資料表初始化完成");
    }

    /// <summary>FTS5 不支援 UPSERT，必須先刪再插確保不重複。</summary>
    public async Task IndexNoteAsync(string noteId, string noteName, string content, string notebookId, string notebookName)
    {
        var plainText = TextHelper.RtfToPlainText(content);
        var tokenized = TextHelper.TokenizeForChinese(plainText);
        var tokenizedName = TextHelper.TokenizeForChinese(noteName);
        var tokenizedNotebookName = TextHelper.TokenizeForChinese(notebookName);

        await _dbContext.Database.ExecuteSqlRawAsync(
       "DELETE FROM NotesIndex WHERE note_id = {0}", noteId);

        await _dbContext.Database.ExecuteSqlRawAsync(
            @"INSERT INTO NotesIndex (note_id, notebook_id, note_name, notebook_name, content)
            VALUES (@noteId, @notebookId, @noteName, @notebookName, @content)",
             new SqliteParameter("@noteId", noteId),
             new SqliteParameter("@notebookId", notebookId),
             new SqliteParameter("@noteName", tokenizedName),
             new SqliteParameter("@notebookName", tokenizedNotebookName),
             new SqliteParameter("@content", tokenized));
    }

    /// <summary>移除指定筆記的搜尋索引。</summary>
    public async Task RemoveNoteIndexAsync(string noteId)
    {
        await _dbContext.Database.ExecuteSqlRawAsync(
            "DELETE FROM NotesIndex WHERE note_id = {0}", noteId);
    }

    /// <summary>
    /// 清空索引後全量重建，以 Transaction 保護避免中途失敗導致索引殘缺。
    /// TODO: 筆記量超過萬筆時，改為 Skip/Take 分頁批次讀取控制記憶體。
    /// </summary>
    public async Task RebuildIndexAsync(string userId)
    {
        _logger.LogInformation("開始重建搜尋索引，UserId：{UserId}", userId);

        var notes = await _dbContext.Notes
            .Where(n => !n.IsDeleted)
            .Join(_dbContext.Notebooks.Where(nb => nb.UserId == userId && !nb.IsDeleted),
                note => note.NotebookId,
                notebook => notebook.Id,
                (note, notebook) => new { note, notebook })
            .ToListAsync();

        using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM NotesIndex");

            foreach (var item in notes)
            {
                await IndexNoteAsync(
                    item.note.Id,
                    item.note.Name,
                    item.note.Content ?? string.Empty,
                    item.notebook.Id,
                    item.notebook.Name);
            }
            await transaction.CommitAsync();
            _logger.LogInformation("搜尋索引重建完成，共索引 {Count} 篇筆記", notes.Count);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "搜尋索引重建失敗，已 Rollback 交易");
            throw;
        }
    }

    /// <summary>
    /// 對使用者輸入進行消毒、中文斷詞處理後執行 FTS5 MATCH 查詢。
    /// 搜尋結果的 tokenized 中文會還原為正常顯示格式。
    /// </summary>
    public async Task<List<SearchResult>> SearchAsync(string keyword, string userId, string? notebookId = null)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return new List<SearchResult>();

        // 移除雙引號防止 FTS5 MATCH 語法崩潰
        var safeKeyword = keyword.Replace("\"", "").Trim();
        if (string.IsNullOrWhiteSpace(safeKeyword))
            return new List<SearchResult>();

        var tokenizedKeyword = TextHelper.TokenizeForChinese(safeKeyword);
        var ftsQuery = string.Join(" ", tokenizedKeyword.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => $"\"{word}\"*"));

        var results = new List<SearchResult>();

        var sql = @"
        SELECT 
            idx.note_id,
            idx.notebook_id,
            idx.note_name,
            idx.notebook_name,
            snippet(NotesIndex, 4, '【', '】', '...', 20) as match_snippet
        FROM NotesIndex idx
        INNER JOIN Notes n ON n.Id = idx.note_id
        INNER JOIN Notebooks nb ON nb.Id = idx.notebook_id
        WHERE NotesIndex MATCH @keyword
          AND n.IsDeleted = 0
          AND nb.IsDeleted = 0
          AND nb.UserId = @userId";

        if (!string.IsNullOrEmpty(notebookId))
        {
            sql += " AND idx.notebook_id = @notebookId";
        }

        sql += " ORDER BY rank LIMIT 50";

        var connection = _dbContext.Database.GetDbConnection();

        bool isConnectionClosed = connection.State == System.Data.ConnectionState.Closed;
        if (isConnectionClosed)
        {
            await connection.OpenAsync();
        }

        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new SqliteParameter("@keyword", ftsQuery));
            cmd.Parameters.Add(new SqliteParameter("@userId", userId));

            if (!string.IsNullOrEmpty(notebookId))
            {
                cmd.Parameters.Add(new SqliteParameter("@notebookId", notebookId));
            }

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add(new SearchResult
                {
                    NoteId = reader.GetString(0),
                    NotebookId = reader.GetString(1),
                    NoteName = TextHelper.RestoreFromTokenized(reader.GetString(2)),
                    NotebookName = TextHelper.RestoreFromTokenized(reader.GetString(3)),
                    MatchSnippet = TextHelper.RestoreFromTokenized(reader.GetString(4))
                });
            }
        }
        finally
        {
            if (isConnectionClosed)
            {
                await connection.CloseAsync();
            }
        }

        _logger.LogInformation("搜尋「{Keyword}」，命中 {Count} 筆", keyword, results.Count);
        return results;
    }
}