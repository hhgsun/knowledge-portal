namespace KnowledgePortal.Api.Helpers;

/// <summary>
/// Builds a match-context snippet for search results: a window of the article's plain text
/// around the earliest query-term occurrence. Matching folds Turkish accents and case
/// symmetrically with the FTS index (via SlugHelper.Transliterate), so "is" matches "İş".
/// Returns null when no term occurs in the text (e.g. title-only or stemmed-form matches) —
/// callers fall back to the stored excerpt.
/// </summary>
public static class SearchSnippetHelper
{
    private const int ContextBefore = 60;
    private const int MaxSnippetLength = 240;

    public static string? Build(string? text, IReadOnlyList<string> queryTokens)
    {
        if (string.IsNullOrWhiteSpace(text) || queryTokens.Count == 0) return null;

        var normalizedText = Normalize(text);
        var matchIndex = -1;

        foreach (var token in queryTokens)
        {
            var normalizedToken = Normalize(token.Trim());
            if (normalizedToken.Length == 0) continue;

            var idx = normalizedText.IndexOf(normalizedToken, StringComparison.Ordinal);
            // Stemmed FTS matches (e.g. query "politikası" vs. text "politika") won't contain
            // the full token — retry with a prefix to approximate the stem
            if (idx < 0 && normalizedToken.Length >= 6)
                idx = normalizedText.IndexOf(normalizedToken[..Math.Max(4, normalizedToken.Length * 3 / 5)], StringComparison.Ordinal);

            if (idx >= 0 && (matchIndex < 0 || idx < matchIndex))
                matchIndex = idx;
        }

        if (matchIndex < 0) return null;

        var start = Math.Max(0, matchIndex - ContextBefore);
        if (start > 0)
        {
            // Don't start mid-word: advance to the next word boundary before the match
            var boundary = text.IndexOf(' ', start);
            if (boundary >= 0 && boundary < matchIndex) start = boundary + 1;
        }

        var end = Math.Min(text.Length, start + MaxSnippetLength);
        if (end < text.Length)
        {
            // Don't end mid-word, but never cut back past the matched term itself
            var boundary = text.LastIndexOf(' ', end - 1);
            if (boundary > matchIndex) end = boundary;
        }

        var snippet = text[start..end].Trim();
        if (snippet.Length == 0) return null;

        return (start > 0 ? "…" : "") + snippet + (end < text.Length ? "…" : "");
    }

    /// <summary>Case- and accent-folds text 1:1 per character, preserving indices.</summary>
    private static string Normalize(string text)
    {
        var chars = SlugHelper.Transliterate(text).ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            chars[i] = char.ToLowerInvariant(chars[i]);
        return new string(chars);
    }
}
