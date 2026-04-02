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
    public async Task InitializeAsync()
    {
        var conn = _dbContext.Database.GetDbConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        // FTS5 虛擬資料表，content="" 表示不儲存原始內容（節省空間），只存索引
        // tokenize='unicode61' 支援 Unicode 字元斷詞
        cmd.CommandText = @"
            CREATE VIRTUAL TABLE IF NOT EXISTS NotesIndex USING fts5(
                note_id UNINDEXED,
                notebook_id UNINDEXED,
                note_name,
                notebook_name,
                content,
                tokenize='unicode61'
            );";

        await cmd.ExecuteNonQueryAsync();
        _logger.LogInformation("FTS5 索引資料表初始化完成");
    }
    public async Task IndexNoteAsync(string noteId, string noteName, string content, string notebookId, string notebookName)
    {
        var conn = _dbContext.Database.GetDbConnection();
        await conn.OpenAsync();

        // 先刪再插（確保更新時不會重複）
        using (var deleteCmd = conn.CreateCommand())
        {
            deleteCmd.CommandText = "DELETE FROM NotesIndex WHERE note_id = @noteId";
            deleteCmd.Parameters.Add(new SqliteParameter("@noteId", noteId));
            await deleteCmd.ExecuteNonQueryAsync();

            // RTF 轉純文字 → 中文字元拆分
            var plainText = RtfToPlainText(content);
            var tokenized = TokenizeForChinese(plainText);
            var tokenizedName = TokenizeForChinese(noteName);
            var tokenizedNotebookName = TokenizeForChinese(notebookName);

            using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = @"
            INSERT INTO NotesIndex (note_id, notebook_id, note_name, notebook_name, content)
            VALUES (@noteId, @notebookId, @noteName, @notebookName, @content)";

            insertCmd.Parameters.Add(new SqliteParameter("@noteId", noteId));
            insertCmd.Parameters.Add(new SqliteParameter("@notebookId", notebookId));
            insertCmd.Parameters.Add(new SqliteParameter("@noteName", tokenizedName));
            insertCmd.Parameters.Add(new SqliteParameter("@notebookName", tokenizedNotebookName));
            insertCmd.Parameters.Add(new SqliteParameter("@content", tokenized));

            await insertCmd.ExecuteNonQueryAsync();
        }
    }

    public async Task RemoveNoteIndexAsync(string noteId)
    {
        var conn = _dbContext.Database.GetDbConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM NotesIndex WHERE note_id = @noteId";
        cmd.Parameters.Add(new SqliteParameter("@noteId", noteId));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RebuildIndexAsync(string userId)
    {
        _logger.LogInformation("開始重建搜尋索引，UserId：{UserId}", userId);

        var conn = _dbContext.Database.GetDbConnection();
        await conn.OpenAsync();

        // 清空索引
        using (var clearCmd = conn.CreateCommand())
        {
            clearCmd.CommandText = "DELETE FROM NotesIndex";
            await clearCmd.ExecuteNonQueryAsync();
        }

        // 撈所有未刪除的筆記，連帶筆記本名稱
        var notes = await _dbContext.Notes
            .Where(n => !n.IsDeleted)
            .Join(_dbContext.Notebooks.Where(nb => nb.UserId == userId && !nb.IsDeleted),
                note => note.NotebookId,
                notebook => notebook.Id,
                (note, notebook) => new { note, notebook })
            .ToListAsync();

        foreach (var item in notes)
        {
            await IndexNoteAsync(
                item.note.Id,
                item.note.Name,
                item.note.Content ?? string.Empty,
                item.notebook.Id,
                item.notebook.Name);
        }

        _logger.LogInformation("搜尋索引重建完成，共索引 {Count} 篇筆記", notes.Count);
    }

   

    public async Task<List<SearchResult>> SearchAsync(string keyword, string userId, string? notebookId = null)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return new List<SearchResult>();

        // 把搜尋關鍵字也做中文字元拆分，才能跟索引匹配
        var tokenizedKeyword = TokenizeForChinese(keyword.Trim());

        // 用 FTS5 的 MATCH 語法搜尋，* 做前綴匹配
        var ftsQuery = string.Join(" ", tokenizedKeyword.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => $"\"{word}\"*"));

        var conn = _dbContext.Database.GetDbConnection();
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();

        // 搜尋 FTS5 並 JOIN Notes 資料表過濾 UserId 和 IsDeleted
        var sql = @"
            SELECT 
                idx.note_id,
                idx.notebook_id,
                idx.note_name,
                idx.notebook_name,
                snippet(NotesIndex, 4, '【', '】', '...', 20) as match_snippet,
                rank
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
            cmd.Parameters.Add(new SqliteParameter("@notebookId", notebookId));
        }

        sql += " ORDER BY rank LIMIT 50";

        cmd.CommandText = sql;
        cmd.Parameters.Add(new SqliteParameter("@keyword", ftsQuery));
        cmd.Parameters.Add(new SqliteParameter("@userId", userId));

        var results = new List<SearchResult>();
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

        _logger.LogInformation("搜尋「{Keyword}」，命中 {Count} 筆", keyword, results.Count);
        return results;
    
    }

    // ══════════════════════════════════════════════════════════
    //  工具方法：RTF 轉純文字
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// 將 RTF 格式字串轉為純文字（移除所有 RTF 控制碼）
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

    // ══════════════════════════════════════════════════════════
    //  工具方法：中文字元拆分
    // ══════════════════════════════════════════════════════════

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

    /// <summary>還原顯示用：移除中文字元間的空格</summary>
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
}