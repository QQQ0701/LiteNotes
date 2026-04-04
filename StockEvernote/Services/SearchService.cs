using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StockEvernote.Contracts;
using StockEvernote.Data;
using StockEvernote.Model;
using System.Text;
using System.Text.RegularExpressions;

namespace StockEvernote.Services;

public class SearchService : ISearchService
{
    private readonly EvernoteDbContext _dbContext;
    private readonly ILogger<SearchService> _logger;

    public SearchService(EvernoteDbContext dbContext, ILogger<SearchService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }
    #region 索引生命週期管理
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

    /// <summary>
    /// FTS5 不支援 UPSERT，必須先刪再插確保不重複。
    /// </summary>
    public async Task IndexNoteAsync(string noteId, string noteName, string content, string notebookId, string notebookName)
    {
        var plainText = RtfToPlainText(content);
        var tokenized = TokenizeForChinese(plainText);
        var tokenizedName = TokenizeForChinese(noteName);
        var tokenizedNotebookName = TokenizeForChinese(notebookName);

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

        // 撈所有未刪除的筆記，連帶筆記本名稱
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
    #endregion

    #region 全文搜尋
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

        // 把搜尋關鍵字也做中文字元拆分，才能跟索引匹配
        var tokenizedKeyword = TokenizeForChinese(safeKeyword);
        // 用 FTS5 的 MATCH 語法搜尋，* 做前綴匹配
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
                    NoteName = RestoreFromTokenized(reader.GetString(2)),
                    NotebookName = RestoreFromTokenized(reader.GetString(3)),
                    MatchSnippet = RestoreFromTokenized(reader.GetString(4))
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

#endregion

    #region 文字處理工具

    /// <summary>
    /// 移除 RTF 控制碼，擷取純文字內容供 FTS5 索引使用。
    /// 非 RTF 格式的字串直接回傳。
    /// </summary>
    private static string RtfToPlainText(string rtf)
    {
        if (string.IsNullOrEmpty(rtf)) return string.Empty;

        // 如果不是 RTF 格式，直接回傳
        if (!rtf.TrimStart().StartsWith("{\\rtf")) return rtf;

        try
        {
            // 移除 RTF 控制碼的簡易正則
            var text = rtf;

            // 移除 {\*...} 群組
            text = Regex.Replace(text, @"\{\\\*[^}]*\}", "", RegexOptions.Singleline);

            // 移除 RTF 控制字（如 \par, \b, \i 等），保留文字
            text = Regex.Replace(text, @"\\[a-z]+(-?\d+)?[ ]?", " ");

            // 移除 Unicode 轉義 \'XX
            text = Regex.Replace(text, @"\\'[0-9a-fA-F]{2}", "");

            // 移除大括號
            text = text.Replace("{", "").Replace("}", "");

            // 清理多餘空白
            text = Regex.Replace(text, @"\s+", " ").Trim();

            return text;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 將中文字元之間插入空格，讓 FTS5 的 unicode61 tokenizer 能正確斷詞。
    /// 英文單字保持原樣（本身就有空格分隔）。
    /// 例如："股票分析report" → "股 票 分 析 report"
    /// </summary>
    private static string TokenizeForChinese(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        var sb = new StringBuilder(input.Length * 2);
        bool prevWasChinese = false;

        foreach (var ch in input)
        {
            bool isChinese = IsChinese(ch);

            if (isChinese)
            {
                if (sb.Length > 0 && !prevWasChinese && sb[sb.Length - 1] != ' ')
                    sb.Append(' ');

                if (prevWasChinese)
                    sb.Append(' ');

                sb.Append(ch);
                prevWasChinese = true;
            }
            else
            {
                if (prevWasChinese && ch != ' ')
                    sb.Append(' ');

                sb.Append(ch);
                prevWasChinese = false;
            }
        }

        return sb.ToString();
    }

    /// <summary>還原 tokenized 中文：移除漢字間的空格，保留英文空格。</summary>
    private static string RestoreFromTokenized(string tokenized)
    {
        if (string.IsNullOrEmpty(tokenized)) return string.Empty;

        var sb = new StringBuilder(tokenized.Length);
        for (int i = 0; i < tokenized.Length; i++)
        {
            if (tokenized[i] == ' ')
            {
                // 如果前後都是中文字，跳過這個空格
                bool prevChinese = i > 0 && IsChinese(tokenized[i - 1]);
                bool nextChinese = i < tokenized.Length - 1 && IsChinese(tokenized[i + 1]);

                if (prevChinese && nextChinese)
                    continue;
            }
            sb.Append(tokenized[i]);
        }
        return sb.ToString();
    }

    /// <summary>判斷是否為中文字元（CJK 統一漢字）</summary>
    private static bool IsChinese(char c)
    {
        return c >= 0x4E00 && c <= 0x9FFF  // CJK 統一漢字
            || c >= 0x3400 && c <= 0x4DBF  // CJK 統一漢字擴展 A
            || c >= 0xF900 && c <= 0xFAFF; // CJK 相容漢字
    }
    #endregion
}