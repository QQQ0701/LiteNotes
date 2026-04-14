using System.Text;
using System.Text.RegularExpressions;

namespace LiteNotes.Helpers;

/// <summary>
/// 文字處理與搜尋斷詞共用工具
/// </summary>
public static class TextHelper
{
    /// <summary>
    /// 移除 RTF 控制碼，擷取純文字內容。
    /// 非 RTF 格式的字串直接回傳。
    /// </summary>
    public static string RtfToPlainText(string rtf)
    {
        if (string.IsNullOrEmpty(rtf)) return string.Empty;
        if (!rtf.TrimStart().StartsWith("{\\rtf")) return rtf;

        try
        {
            var text = rtf;

            // 移除 RTF 表頭區塊
            string[] headersToRemove = { "fonttbl", "colortbl", "stylesheet", "info", "listtable" };
            foreach (var header in headersToRemove)
            {
                text = Regex.Replace(text, @"\{\\" + header + @"(?>[^{}]+|\{(?<c>)|\}(?<-c>))*(?(c)(?!))\}", "",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase);
            }

            text = Regex.Replace(text, @"\{\\\*(?>[^{}]+|\{(?<c>)|\}(?<-c>))*(?(c)(?!))\}", "",
                RegexOptions.Singleline);

            // ✅ 新增：先把 Unicode 跳脫序列轉成實際字元
            // \u21488? → 台、\u31309? → 積、\u-26885? → 電
            text = Regex.Replace(text, @"\\u(-?\d+)\?", match =>
            {
                var code = int.Parse(match.Groups[1].Value);
                // RTF 的負數 Unicode：-26885 實際是 65536 + (-26885) = 38651
                if (code < 0) code += 65536;
                return ((char)code).ToString();
            });

            // 移除其他 RTF 控制碼
            text = Regex.Replace(text, @"\\[a-zA-Z]+(-?\d+)?[ ]?", " ");
            text = Regex.Replace(text, @"\\'[0-9a-fA-F]{2}", "");
            text = text.Replace("{", "").Replace("}", "");
            text = Regex.Replace(text, @"\s+", " ").Trim();

            return text;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 將中文字元之間插入空格，供 SQLite FTS5 進行斷詞。
    /// </summary>
    public static string TokenizeForChinese(string input)
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

    /// <summary>
    /// 還原 tokenized 中文：移除漢字間的空格，保留英文空格。
    /// </summary>
    public static string RestoreFromTokenized(string tokenized)
    {
        if (string.IsNullOrEmpty(tokenized)) return string.Empty;

        var sb = new StringBuilder(tokenized.Length);
        for (int i = 0; i < tokenized.Length; i++)
        {
            if (tokenized[i] == ' ')
            {
                bool prevChinese = i > 0 && IsChinese(tokenized[i - 1]);
                bool nextChinese = i < tokenized.Length - 1 && IsChinese(tokenized[i + 1]);

                if (prevChinese && nextChinese)
                    continue;
            }
            sb.Append(tokenized[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 判斷是否為中文字元（CJK 統一漢字）
    /// </summary>
    public static bool IsChinese(char c)
    {
        return c >= 0x4E00 && c <= 0x9FFF
            || c >= 0x3400 && c <= 0x4DBF
            || c >= 0xF900 && c <= 0xFAFF;
    }
}
