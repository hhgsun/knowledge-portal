using System.Text.RegularExpressions;
using KnowledgePortal.Api.Helpers;

namespace KnowledgePortal.Api.Services;

internal sealed record KnowledgeChunk(string Content, string Location);
internal sealed record KnowledgeParentChunk(string Content, string Location,
    IReadOnlyList<KnowledgeChunk> Children);

/// <summary>
/// Structure-aware text chunking for knowledge sources. Markdown sections and paragraph-like
/// blocks remain intact whenever they fit; only an oversized block falls back to a sliding word
/// window. The word budget is an intentionally provider-neutral approximation until the selected
/// embedding provider exposes a reliable tokenizer.
/// </summary>
internal static partial class KnowledgeChunker
{
    internal const int DefaultTargetWords = 500;
    internal const int DefaultOverlapWords = 50;
    internal const int DefaultParentTargetWords = 1000;
    internal const int DefaultChildTargetWords = 220;
    internal const int DefaultChildOverlapWords = 40;

    /// <summary>
    /// Builds true parent-child chunks. Search vectors are generated only for the compact
    /// children; the larger, structure-bounded parent is persisted once and supplied to RAG
    /// after a child match. A parent never crosses a Markdown heading boundary.
    /// </summary>
    public static List<KnowledgeParentChunk> BuildMarkdownHierarchy(string title, string? excerpt,
        string? markdown, int parentTargetWords, int childTargetWords, int childOverlapWords)
    {
        var sections = ParseMarkdownSections(markdown);
        if (sections.Count == 0)
        {
            var fallback = ContentExtractor.ExtractSearchableText(title, excerpt, markdown, "");
            return BuildTextHierarchy(fallback, "article", parentTargetWords, childTargetWords,
                childOverlapWords);
        }

        var result = new List<KnowledgeParentChunk>();
        foreach (var section in sections)
        {
            var header = string.Join(". ", new[] { title, excerpt, section.Heading }
                .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
            var location = section.Heading == null ? "article" : $"section:{LocationPart(section.Heading)}";
            result.AddRange(BuildHierarchy(section.Blocks, header, location, parentTargetWords,
                childTargetWords, childOverlapWords));
        }
        return result;
    }

    /// <summary>
    /// Builds a hierarchy for a layout segment such as one PDF page, workbook sheet or slide.
    /// Callers invoke this once per segment, so parents cannot leak across provenance boundaries.
    /// </summary>
    public static List<KnowledgeParentChunk> BuildTextHierarchy(string text, string location,
        int parentTargetWords, int childTargetWords, int childOverlapWords)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var normalized = text.Replace("\r", "").Trim();
        var blocks = ParagraphBreak().Split(normalized)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToList();
        return BuildHierarchy(blocks.Count == 0 ? [normalized] : blocks, null, location,
            parentTargetWords, childTargetWords, childOverlapWords);
    }

    private static List<KnowledgeParentChunk> BuildHierarchy(IReadOnlyList<string> blocks,
        string? header, string location, int parentTargetWords, int childTargetWords,
        int childOverlapWords)
    {
        parentTargetWords = Math.Max(2, parentTargetWords);
        childTargetWords = Math.Clamp(childTargetWords, 1, parentTargetWords);
        childOverlapWords = Math.Clamp(childOverlapWords, 0, childTargetWords - 1);

        // Repeating a bounded title/section header in every child makes an independently
        // retrieved passage self-identifying without allowing metadata to crowd out its body.
        var headerWords = Words(header ?? "");
        var headerBudget = Math.Min(headerWords.Length, Math.Max(0, childTargetWords / 3));
        var safeHeader = string.Join(' ', headerWords.Take(headerBudget));
        var parentBodyTarget = Math.Max(1, parentTargetWords - headerBudget);
        var childBodyTarget = Math.Max(1, childTargetWords - headerBudget);
        var childBodyOverlap = Math.Min(childOverlapWords, Math.Max(0, childBodyTarget - 1));

        var parentBodies = PackBlocks(blocks, parentBodyTarget, 0, location);
        var parents = new List<KnowledgeParentChunk>(parentBodies.Count);
        for (var parentIndex = 0; parentIndex < parentBodies.Count; parentIndex++)
        {
            var body = parentBodies[parentIndex].Content;
            var parentLocation = $"{location}:parent:{parentIndex}";
            var parentContent = Prefix(safeHeader, body);
            var childBodies = PackBlocks([body], childBodyTarget, childBodyOverlap, parentLocation);
            var children = childBodies.Select((chunk, childIndex) => new KnowledgeChunk(
                Prefix(safeHeader, chunk.Content), $"{parentLocation}:child:{childIndex}")).ToList();
            if (children.Count > 0)
                parents.Add(new(parentContent, parentLocation, children));
        }
        return parents;
    }

    private static string Prefix(string header, string body) =>
        string.IsNullOrWhiteSpace(header) ? body : $"{header} {body}";

    public static List<KnowledgeChunk> ChunkMarkdown(string title, string? excerpt, string? markdown,
        int targetWords, int overlapWords)
    {
        var sections = ParseMarkdownSections(markdown);
        if (sections.Count == 0)
        {
            var fallback = ContentExtractor.ExtractSearchableText(title, excerpt, markdown, "");
            return ChunkText(fallback, targetWords, overlapWords, "article");
        }

        var result = new List<KnowledgeChunk>();
        foreach (var section in sections)
        {
            // Repeating the document title gives independently retrieved section chunks enough
            // identity without requiring parent expansion. The section heading itself remains in
            // each chunk, but is budgeted separately so it can never become a tiny metadata-only
            // chunk when the first real paragraph would cross the target.
            var header = string.Join(". ", new[] { title, excerpt, section.Heading }
                .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
            var headerWords = Words(header);
            var headerBudget = Math.Min(headerWords.Length, Math.Max(1, targetWords / 3));
            var safeHeader = string.Join(' ', headerWords.Take(headerBudget));
            var bodyTarget = Math.Max(1, targetWords - headerBudget);
            var location = section.Heading == null ? "article" : $"section:{LocationPart(section.Heading)}";
            var bodyChunks = PackBlocks(section.Blocks, bodyTarget,
                Math.Min(overlapWords, Math.Max(0, bodyTarget - 1)), location);
            result.AddRange(bodyChunks.Select(chunk => chunk with
            {
                Content = string.IsNullOrWhiteSpace(safeHeader)
                    ? chunk.Content
                    : $"{safeHeader} {chunk.Content}"
            }));
        }
        return result;
    }

    public static List<KnowledgeChunk> ChunkText(string text, int targetWords, int overlapWords,
        string location = "text")
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var normalized = text.Replace("\r", "").Trim();
        var blocks = ParagraphBreak().Split(normalized)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToList();
        return PackBlocks(blocks.Count == 0 ? [normalized] : blocks, targetWords, overlapWords, location);
    }

    private static List<KnowledgeChunk> PackBlocks(IReadOnlyList<string> blocks, int targetWords,
        int overlapWords, string location)
    {
        targetWords = Math.Max(1, targetWords);
        overlapWords = Math.Clamp(overlapWords, 0, targetWords - 1);
        var result = new List<KnowledgeChunk>();
        var current = new List<string>();

        void EmitCurrent()
        {
            if (current.Count == 0) return;
            result.Add(new(string.Join(' ', current), $"{location}:chunk:{result.Count}"));
            current.Clear();
        }

        foreach (var block in blocks)
        {
            var words = Words(block);
            if (words.Length == 0) continue;

            if (words.Length > targetWords)
            {
                EmitCurrent();
                var step = targetWords - overlapWords;
                for (var offset = 0; offset < words.Length; offset += step)
                {
                    var take = Math.Min(targetWords, words.Length - offset);
                    result.Add(new(string.Join(' ', words, offset, take), $"{location}:chunk:{result.Count}"));
                    if (offset + take >= words.Length) break;
                }
                continue;
            }

            if (current.Count > 0 && current.Count + words.Length > targetWords)
            {
                var overlap = overlapWords == 0 ? [] : current.TakeLast(Math.Min(overlapWords, current.Count)).ToList();
                EmitCurrent();
                var allowedOverlap = Math.Max(0, targetWords - words.Length);
                if (overlap.Count > allowedOverlap) overlap = overlap.TakeLast(allowedOverlap).ToList();
                current.AddRange(overlap);
            }
            current.AddRange(words);
        }
        EmitCurrent();
        return result;
    }

    private sealed record MarkdownSection(string? Heading, List<string> Blocks);

    private static List<MarkdownSection> ParseMarkdownSections(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return [];
        var sections = new List<MarkdownSection>();
        string? heading = null;
        var blocks = new List<string>();
        var block = new List<string>();
        var inFence = false;

        void FlushBlock()
        {
            if (block.Count == 0) return;
            var plain = ContentExtractor.ExtractPlainText(string.Join('\n', block));
            if (!string.IsNullOrWhiteSpace(plain)) blocks.Add(plain.Trim());
            block.Clear();
        }

        void FlushSection()
        {
            FlushBlock();
            if (blocks.Count == 0) return;
            sections.Add(new(heading, [.. blocks]));
            blocks.Clear();
        }

        foreach (var line in markdown.Replace("\r", "").Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal) ||
                line.TrimStart().StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                block.Add(line);
                continue;
            }

            var match = inFence ? Match.Empty : MarkdownHeading().Match(line);
            if (match.Success)
            {
                FlushSection();
                heading = match.Groups[1].Value.Trim();
            }
            else if (!inFence && string.IsNullOrWhiteSpace(line))
            {
                FlushBlock();
            }
            else
            {
                block.Add(line);
            }
        }
        FlushSection();
        return sections;
    }

    private static string[] Words(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static string LocationPart(string value)
    {
        var normalized = Regex.Replace(value.Trim(), @"\s+", " ");
        return normalized[..Math.Min(normalized.Length, 150)];
    }

    [GeneratedRegex(@"\n\s*\n+")]
    private static partial Regex ParagraphBreak();

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+(.+?)\s*#*\s*$")]
    private static partial Regex MarkdownHeading();
}
