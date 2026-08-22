using KnowledgePortal.Api.Helpers;

namespace KnowledgePortal.Api.Services;

public sealed record RerankCandidate(string ArticleId, string Title, string? Excerpt,
    string? Content, double RetrievalScore, DateTime? UpdatedAt = null,
    DateTime? ApprovedAt = null, string? ContentType = null);
public sealed record RerankedResult(string ArticleId, double Score);

public interface ISearchReranker
{
    IReadOnlyList<RerankedResult> Rerank(string query, IReadOnlyList<RerankCandidate> candidates);
}

/// <summary>
/// Zero-dependency second-stage reranker. It combines normalized retrieval score with exact
/// query coverage and title/phrase evidence. The interface can later be replaced by a local
/// cross-encoder without changing retrieval or controller contracts.
/// </summary>
public sealed class LocalSearchReranker(IConfiguration? config = null) : ISearchReranker
{
    public IReadOnlyList<RerankedResult> Rerank(string query, IReadOnlyList<RerankCandidate> candidates)
    {
        if (candidates.Count == 0) return [];
        var tokens = Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
        var maxRetrieval = Math.Max(candidates.Max(c => c.RetrievalScore), double.Epsilon);

        return candidates.Select(c =>
        {
            var title = Fold(c.Title);
            var excerpt = Fold(c.Excerpt ?? "");
            var body = Fold(ContentExtractor.ExtractPlainText(c.Content));
            var haystack = $"{title} {excerpt} {body}";
            var coverage = tokens.Length == 0 ? 0 : tokens.Count(haystack.Contains) / (double)tokens.Length;
            var titleCoverage = tokens.Length == 0 ? 0 : tokens.Count(title.Contains) / (double)tokens.Length;
            var phrase = query.Length > 1 && haystack.Contains(Fold(query), StringComparison.Ordinal) ? 1d : 0d;
            var retrieval = c.RetrievalScore / maxRetrieval;
            var ageDays = c.UpdatedAt == null ? 365 : Math.Max(0, (DateTime.UtcNow - c.UpdatedAt.Value).TotalDays);
            var halfLife = Math.Max(1, config?.GetValue("Ollama:Ranking:FreshnessHalfLifeDays", 365) ?? 365);
            var freshness = Math.Pow(.5, ageDays / halfLife);
            var freshIntent = new[] { "guncel", "en yeni", "son surum", "latest", "newest", "current" }
                .Any(x => Fold(query).Contains(x));
            var freshnessWeight = (config?.GetValue("Ollama:Ranking:FreshnessWeight", .05) ?? .05)
                * (freshIntent ? config?.GetValue("Ollama:Ranking:FreshnessIntentMultiplier", 3d) ?? 3d : 1d);
            var authority = config?.GetValue($"Ollama:Ranking:Authority:{c.ContentType}", .5) ?? .5;
            if (c.ApprovedAt != null) authority += config?.GetValue("Ollama:Ranking:ApprovedBoost", .2) ?? .2;
            var authorityWeight = config?.GetValue("Ollama:Ranking:AuthorityWeight", .05) ?? .05;
            var score = 0.45 * retrieval + 0.23 * coverage + 0.17 * titleCoverage + 0.05 * phrase
                + freshnessWeight * freshness + authorityWeight * Math.Clamp(authority, 0, 1);
            return new RerankedResult(c.ArticleId, score);
        }).OrderByDescending(x => x.Score).ThenBy(x => x.ArticleId).ToList();
    }

    private static string Fold(string? text) => SlugHelper.Transliterate(text ?? "").ToLowerInvariant();
}
