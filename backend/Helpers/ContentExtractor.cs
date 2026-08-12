using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace KnowledgePortal.Api.Helpers;

public static class ContentExtractor
{
    /// <summary>
    /// Estimates read time based on ~200 words per minute.
    /// Markdown is canonical. Formatting is removed only for derived search/read-time text.
    /// </summary>
    public static int? CalculateReadTime(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return null;
        var wordCount = (ExtractPlainText(markdown) ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return wordCount == 0 ? null : Math.Max(1, (int)Math.Ceiling(wordCount / 200.0));
    }

    public static string ExtractSearchableText(string title, string? excerpt, string? contentJson)
    {
        return ExtractSearchableText(title, excerpt, contentJson, null);
    }

    public static string ExtractSearchableText(string title, string? excerpt, string? markdown, string? attachmentText)
    {
        var sb = new StringBuilder();
        sb.Append(title);

        if (!string.IsNullOrWhiteSpace(excerpt))
        {
            sb.Append(". ");
            sb.Append(excerpt);
        }

        if (!string.IsNullOrWhiteSpace(markdown))
        {
            var text = ExtractPlainText(markdown);
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.Append(". ");
                sb.Append(text);
            }
        }

        if (!string.IsNullOrWhiteSpace(attachmentText))
        {
            sb.Append(". ");
            sb.Append(attachmentText.Trim());
        }

        return sb.ToString();
    }

    /// <summary>
    /// Extracts readable text while retaining headings, table cells, code and link labels.
    /// </summary>
    public static string? ExtractPlainText(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return null;
        var text = markdown.Replace("\r", "");
        text = Regex.Replace(text, "<!--[\\s\\S]*?-->", " ");
        text = Regex.Replace(text, "!\\[([^]]*)\\]\\([^)]*\\)", "$1");
        text = Regex.Replace(text, "\\[([^]]+)\\]\\([^)]*\\)", "$1");
        text = Regex.Replace(text, "^\\s{0,3}(#{1,6}|>|[-+*]|\\d+[.)])\\s+", "", RegexOptions.Multiline);
        text = Regex.Replace(text, "[`*_~]", "");
        text = Regex.Replace(text, "[ \\t]+", " ");
        text = Regex.Replace(text, "\\n{3,}", "\n\n").Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(bytes);
    }
}
