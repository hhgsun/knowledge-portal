using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public sealed record PortalSearchRequest(
    string Query,
    string Type = "fulltext",
    int Limit = 20,
    int Page = 1,
    bool OnlyOwnContent = false,
    bool IncludeContent = false,
    bool IncludeAttachments = false,
    IEnumerable<string>? Tags = null,
    IEnumerable<string>? Authors = null,
    IEnumerable<string>? ContentTypes = null);

public enum SearchFailureKind
{
    None,
    AiUnavailable,
    AiFailed,
    RagBusy,
    RagCircuitOpen,
    RagTimeout
}

public sealed record PortalSearchResult(
    string Query,
    string Type,
    List<ArticleSummaryDto> Results,
    int Total,
    int Page,
    int TotalPages,
    long ResponseTimeMs,
    string SearchQueryId,
    SearchIndexCoverage? IndexCoverage = null,
    IReadOnlyList<string>? Tags = null,
    string? Warning = null,
    RagService.RagResult? Rag = null,
    SearchFailureKind Failure = SearchFailureKind.None)
{
    public bool IndexingPending => IndexCoverage?.RelevantPending > 0;
}

/// <summary>
/// Canonical search pipeline for REST and MCP. Transport adapters only translate this result
/// into HTTP or JSON-RPC response shapes; parsing, filtering, ranking and analytics stay here.
/// </summary>
public sealed class SearchExecutionService(
    AppDbContext db,
    IConfiguration config,
    ArticleService articles,
    ISearchReranker reranker,
    IServiceProvider services,
    ILogger<SearchExecutionService> logger)
{
    private static readonly HashSet<string> ValidTypes = ["fulltext", "semantic", "hybrid", "rag"];

    public async Task<(PortalSearchResult? Result, ServiceError? Error)> ExecuteAsync(
        PortalSearchRequest request,
        ClaimsPrincipal principal,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return (null, new ServiceError(400, "Query parameter 'q' is required"));

        var type = request.Type.Trim().ToLowerInvariant();
        if (!ValidTypes.Contains(type))
            return (null, new ServiceError(400, "Search type must be one of: fulltext, semantic, hybrid, rag"));

        var page = Math.Max(1, request.Page);
        var limit = Math.Clamp(request.Limit, 1, 50);
        var stopwatch = Stopwatch.StartNew();
        var parsed = Parse(request);

        var authorIds = parsed.AuthorSlugs.Count > 0
            ? await db.ResolveAuthorIdsAsync(parsed.AuthorSlugs)
            : null;

        List<string>? resolvedTags = null;
        if (parsed.TagSlugs.Count > 0)
        {
            resolvedTags = await db.Tags
                .Where(tag => parsed.TagSlugs.Contains(tag.Slug))
                .Select(tag => tag.Slug)
                .ToListAsync(ct);
            if (resolvedTags.Count != parsed.TagSlugs.Count)
            {
                return (await CompleteAsync(request.Query, "tag", [], 0, 1, 0, stopwatch, principal,
                    ct, tags: parsed.TagSlugs), null);
            }
        }

        var apiKeyId = request.OnlyOwnContent ? principal.GetApiKeyId() : null;
        var filter = new ArticleFilter(
            OwnerIds: parsed.AuthorSlugs.Count > 0 ? authorIds : null,
            ContentTypes: parsed.ContentTypes.Count > 0 ? parsed.ContentTypes : null,
            ApiKeyId: apiKeyId,
            TagSlugs: resolvedTags);
        var snippetTokens = parsed.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if ((parsed.TagSlugs.Count > 0 || parsed.ContentTypes.Count > 0 || parsed.AuthorSlugs.Count > 0)
            && string.IsNullOrWhiteSpace(parsed.Text))
        {
            var query = ArticleService.ApplyFilter(db.Articles.WherePublished(), filter);
            var total = await query.CountAsync(ct);
            var found = await query.OrderByDescending(article => article.UpdatedAt)
                .Skip((page - 1) * limit).Take(limit).ToListAsync(ct);
            var results = await BuildResultsAsync(found, request.IncludeContent,
                request.IncludeAttachments, snippetTokens, ct: ct);
            var filterType = parsed.ContentTypes.Count == 0 && parsed.AuthorSlugs.Count == 0 ? "tag" : "filter";
            return (await CompleteAsync(request.Query, filterType, results, total, page,
                (int)Math.Ceiling(total / (double)limit), stopwatch, principal, ct,
                tags: parsed.TagSlugs), null);
        }

        var coverage = await articles.GetSearchIndexCoverageAsync(type, filter, ct);
        var ollamaEnabled = config.GetValue("Ollama:Enabled", false);
        var vectors = ollamaEnabled ? services.GetService<IVectorSearchService>() : null;

        if (type == "rag")
        {
            if (!ollamaEnabled || vectors == null || services.GetService<RagService>() is not { } ragService)
                return (await CompleteAsync(request.Query, type, [], 0, 1, 1, stopwatch, principal,
                    ct, coverage, warning: "AI search is unavailable because Ollama is disabled.",
                    failure: SearchFailureKind.AiUnavailable), null);
            try
            {
                var rag = await ragService.AskAsync(parsed.Text, filter, ct);
                return (await CompleteAsync(request.Query, type, [], rag.Sources.Count, 1, 1,
                    stopwatch, principal, ct, coverage, rag: rag), null);
            }
            catch (RagBusyException)
            {
                return (await CompleteAsync(request.Query, type, [], 0, 1, 1, stopwatch, principal,
                    ct, coverage, failure: SearchFailureKind.RagBusy), null);
            }
            catch (RagCircuitOpenException)
            {
                return (await CompleteAsync(request.Query, type, [], 0, 1, 1, stopwatch, principal,
                    ct, coverage, failure: SearchFailureKind.RagCircuitOpen), null);
            }
            catch (RagStageTimeoutException)
            {
                return (await CompleteAsync(request.Query, type, [], 0, 1, 1, stopwatch, principal,
                    ct, coverage, failure: SearchFailureKind.RagTimeout), null);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "RAG search failed");
                return (await CompleteAsync(request.Query, type, [], 0, 1, 1, stopwatch, principal,
                    ct, coverage, warning: "RAG search failed.", failure: SearchFailureKind.AiFailed), null);
            }
        }

        if (type == "semantic")
        {
            if (!ollamaEnabled || vectors == null)
                return (await CompleteAsync(request.Query, type, [], 0, 1, 1, stopwatch, principal,
                    ct, coverage, warning: "Semantic search unavailable — Ollama disabled",
                    failure: SearchFailureKind.AiUnavailable), null);
            try
            {
                var hits = await vectors.SearchAsync(parsed.Text, limit, ct, filter: filter);
                var ids = hits.Select(hit => hit.ArticleId).ToList();
                var found = await ArticleService.ApplyFilter(
                        db.Articles.WherePublished().Where(article => ids.Contains(article.Id)), filter)
                    .ToListAsync(ct);
                var byId = found.ToDictionary(article => article.Id);
                var ordered = hits.Where(hit => byId.ContainsKey(hit.ArticleId))
                    .Select(hit => byId[hit.ArticleId]).ToList();
                var results = await BuildResultsAsync(ordered, request.IncludeContent,
                    request.IncludeAttachments, snippetTokens,
                    hits.ToDictionary(hit => hit.ArticleId, hit => Math.Round(hit.Score, 4)), ct: ct);
                return (await CompleteAsync(request.Query, type, results, results.Count, 1, 1,
                    stopwatch, principal, ct, coverage), null);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Semantic search failed");
                return (await CompleteAsync(request.Query, type, [], 0, 1, 1, stopwatch, principal,
                    ct, coverage, warning: "Semantic search failed", failure: SearchFailureKind.AiFailed), null);
            }
        }

        if (type == "hybrid")
        {
            var candidateLimit = Math.Clamp(config.GetValue("Search:HybridCandidateLimit", 200), limit, 500);
            var fulltextIds = (await articles.SearchPublishedAsync(parsed.Text, candidateLimit, filter))
                .Select(article => article.Id).ToList();
            List<VectorSearchResult>? semanticHits = null;
            if (ollamaEnabled && vectors != null)
            {
                try { semanticHits = await vectors.SearchAsync(parsed.Text, candidateLimit, ct, filter: filter); }
                catch (Exception exception) { logger.LogWarning(exception, "Hybrid semantic leg failed"); }
            }

            var scores = RrfHelper.Merge(fulltextIds, semanticHits?.Select(hit => hit.ArticleId).ToList());
            var ids = scores.Keys.ToList();
            var found = await ArticleService.ApplyFilter(
                    db.Articles.WherePublished().Where(article => ids.Contains(article.Id)), filter)
                .ToListAsync(ct);
            var reranked = reranker.Rerank(parsed.Text, found.Select(article => new RerankCandidate(
                article.Id, article.Title, article.Excerpt, article.Content, scores[article.Id].Score,
                article.UpdatedAt, article.ApprovedAt, article.ContentType)).ToList()).Take(limit).ToList();
            var byId = found.ToDictionary(article => article.Id);
            var ordered = reranked.Where(hit => byId.ContainsKey(hit.ArticleId))
                .Select(hit => byId[hit.ArticleId]).ToList();
            var results = await BuildResultsAsync(ordered, request.IncludeContent,
                request.IncludeAttachments, snippetTokens,
                reranked.ToDictionary(hit => hit.ArticleId, hit => Math.Round(hit.Score, 4)),
                scores.ToDictionary(score => score.Key, score => score.Value.MatchType), ct);
            var warning = semanticHits == null && ollamaEnabled
                ? "Semantic search unavailable — using fulltext only"
                : null;
            return (await CompleteAsync(request.Query, type, results, results.Count, 1, 1,
                stopwatch, principal, ct, coverage, warning: warning), null);
        }

        var pageResult = await articles.SearchPublishedPagedAsync(parsed.Text, page, limit, filter);
        var fulltextResults = await BuildResultsAsync(pageResult.Articles, request.IncludeContent,
            request.IncludeAttachments, snippetTokens, ct: ct);
        return (await CompleteAsync(request.Query, type, fulltextResults, pageResult.Total, page,
            (int)Math.Ceiling(pageResult.Total / (double)limit), stopwatch, principal, ct, coverage), null);
    }

    private async Task<List<ArticleSummaryDto>> BuildResultsAsync(
        IReadOnlyList<Article> found,
        bool includeContent,
        bool includeAttachments,
        string[] snippetTokens,
        Dictionary<string, double>? scores = null,
        Dictionary<string, string>? matchTypes = null,
        CancellationToken ct = default)
    {
        var ids = found.Select(article => article.Id).ToList();
        var attachments = includeAttachments
            ? await AttachmentHelper.GetAttachmentMapAsync(db, ids)
            : null;
        var enrichment = await articles.GetEnrichmentAsync(ids);
        return found.Select(article =>
        {
            var plainText = includeContent || snippetTokens.Length > 0
                ? ContentExtractor.ExtractPlainText(article.Content)
                : null;
            return ArticleService.BuildSummary(
                article.Id, article.Title, article.Slug, article.Excerpt, article.ContentType,
                article.UpdatedAt.ToString("o"), enrichment.GetValueOrDefault(article.Id),
                includeContent ? article.Content : null,
                attachments?.GetValueOrDefault(article.Id),
                scores?.GetValueOrDefault(article.Id),
                matchTypes?.GetValueOrDefault(article.Id),
                SearchSnippetHelper.Build(plainText, snippetTokens));
        }).ToList();
    }

    private async Task<PortalSearchResult> CompleteAsync(
        string query,
        string type,
        List<ArticleSummaryDto> results,
        int total,
        int page,
        int totalPages,
        Stopwatch stopwatch,
        ClaimsPrincipal principal,
        CancellationToken ct,
        SearchIndexCoverage? coverage = null,
        IReadOnlyList<string>? tags = null,
        string? warning = null,
        RagService.RagResult? rag = null,
        SearchFailureKind failure = SearchFailureKind.None)
    {
        stopwatch.Stop();
        var record = new SearchQuery
        {
            Query = query.Trim(),
            UserId = principal.Identity?.IsAuthenticated == true ? principal.GetUserId() : null,
            ResultsCount = total,
            SearchType = type,
            ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
            RagTraceId = rag == null ? null : Activity.Current?.TraceId.ToString(),
            RagPromptVersion = rag == null ? null : RagService.PromptVersion,
            RagRetrievalVersion = rag == null ? null : RagService.RetrievalVersion,
            RagReranker = rag == null ? null : config.GetValue("Reranking:External:Enabled", false)
                ? $"external:{config["Reranking:External:Model"] ?? "unspecified"}"
                : "local-deterministic-v1",
            RagIndexProfile = rag == null ? null : EmbeddingService.ComputeIndexProfile(config),
            RagGroundingStatus = rag?.GroundingStatus,
            RagAnswerHash = rag == null ? null : Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(rag.Answer))).ToLowerInvariant()
        };
        db.SearchQueries.Add(record);
        await db.SaveChangesAsync(ct);

        return new PortalSearchResult(query, type, results, total, page, totalPages,
            stopwatch.ElapsedMilliseconds, record.Id, coverage, tags, warning, rag, failure);
    }

    private static ParsedSearch Parse(PortalSearchRequest request)
    {
        var tags = Split(request.Tags);
        var authors = Split(request.Authors);
        var contentTypes = Split(request.ContentTypes);
        var remaining = new List<string>();
        foreach (var word in request.Query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.StartsWith("##") && word.Length > 2) contentTypes.Add(word[2..]);
            else if (word.StartsWith('#') && word.Length > 1) tags.Add(word[1..]);
            else if (word.StartsWith('@') && word.Length > 1) authors.Add(word[1..]);
            else remaining.Add(word);
        }
        return new ParsedSearch(string.Join(' ', remaining).Trim(),
            tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            authors.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            contentTypes.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static List<string> Split(IEnumerable<string>? values) => values?
        .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .Where(value => !string.IsNullOrWhiteSpace(value)).ToList() ?? [];

    private sealed record ParsedSearch(string Text, List<string> TagSlugs,
        List<string> AuthorSlugs, List<string> ContentTypes);
}
