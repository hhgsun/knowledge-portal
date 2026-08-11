using KnowledgePortal.Api.Helpers;

namespace KnowledgePortal.Api.Services;

public sealed record RerankCandidate(string ArticleId, string Title, string? Excerpt,
    string? Content, double RetrievalScore);
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
public sealed class LocalSearchReranker : ISearchReranker
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
            var score = 0.50 * retrieval + 0.25 * coverage + 0.20 * titleCoverage + 0.05 * phrase;
            return new RerankedResult(c.ArticleId, score);
        }).OrderByDescending(x => x.Score).ThenBy(x => x.ArticleId).ToList();
    }

    private static string Fold(string? text) => SlugHelper.Transliterate(text ?? "").ToLowerInvariant();
}
