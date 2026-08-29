using System.Diagnostics;
using System.Security.Claims;
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
    IEnumerable<string>? ContentTypes = null,
    IReadOnlyDictionary<string, string[]>? Facets = null);

public enum SearchFailureKind
{
    None,
    AiUnavailable,
    AiFailed
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
    KnowledgeQueryScopeService scopeResolver,
    IServiceProvider services,
    ILogger<SearchExecutionService> logger)
{
    private static readonly HashSet<string> ValidTypes = ["fulltext", "semantic", "hybrid"];

    public async Task<(PortalSearchResult? Result, ServiceError? Error)> ExecuteAsync(
        PortalSearchRequest request,
        ClaimsPrincipal principal,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return (null, new ServiceError(400, "Query parameter 'q' is required"));

        var type = request.Type.Trim().ToLowerInvariant();
        if (!ValidTypes.Contains(type))
            return (null, new ServiceError(400, "Search type must be one of: fulltext, semantic, hybrid"));

        var page = Math.Max(1, request.Page);
        var limit = Math.Clamp(request.Limit, 1, 50);
        var stopwatch = Stopwatch.StartNew();
        var scope = await scopeResolver.ResolveAsync(new KnowledgeQueryScopeRequest(
            request.Query,
            request.OnlyOwnContent,
            request.Tags,
            request.Authors,
            request.ContentTypes,
            request.Facets), principal, ct);

        if (scope.HasUnknownTags || scope.HasUnknownFacets)
        {
            return (await CompleteAsync(request.Query, "tag", [], 0, 1, 0, stopwatch, principal,
                ct, tags: scope.Tags), null);
        }

        var filter = scope.Filter;
        var snippetTokens = scope.QueryText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if ((scope.Tags.Count > 0 || scope.ContentTypes.Count > 0 || scope.Authors.Count > 0
                || scope.Facets.Count > 0)
            && string.IsNullOrWhiteSpace(scope.QueryText))
        {
            var query = ArticleService.ApplyFilter(db.Articles.WherePublished(), filter);
            var total = await query.CountAsync(ct);
            var found = await query.OrderByDescending(article => article.UpdatedAt)
                .Skip((page - 1) * limit).Take(limit).ToListAsync(ct);
            var results = await BuildResultsAsync(found, request.IncludeContent,
                request.IncludeAttachments, snippetTokens, ct: ct);
            var filterType = scope.ContentTypes.Count == 0 && scope.Authors.Count == 0 ? "tag" : "filter";
            return (await CompleteAsync(request.Query, filterType, results, total, page,
                (int)Math.Ceiling(total / (double)limit), stopwatch, principal, ct,
                tags: scope.Tags), null);
        }

        var coverage = await articles.GetSearchIndexCoverageAsync(type, filter, ct);
        var ollamaEnabled = config.GetValue("Ollama:Enabled", false);
        var vectors = ollamaEnabled ? services.GetService<IVectorSearchService>() : null;

        if (type == "semantic")
        {
            if (!ollamaEnabled || vectors == null)
                return (await CompleteAsync(request.Query, type, [], 0, 1, 1, stopwatch, principal,
                    ct, coverage, warning: "Semantic search unavailable — Ollama disabled",
                    failure: SearchFailureKind.AiUnavailable), null);
            try
            {
                var hits = await vectors.SearchAsync(scope.QueryText, limit, ct, filter: filter);
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
            var fulltextIds = (await articles.SearchPublishedAsync(scope.QueryText, candidateLimit, filter))
                .Select(article => article.Id).ToList();
            List<VectorSearchResult>? semanticHits = null;
            if (ollamaEnabled && vectors != null)
            {
                try { semanticHits = await vectors.SearchAsync(scope.QueryText, candidateLimit, ct, filter: filter); }
                catch (Exception exception) { logger.LogWarning(exception, "Hybrid semantic leg failed"); }
            }

            var scores = RrfHelper.Merge(fulltextIds, semanticHits?.Select(hit => hit.ArticleId).ToList());
            var ids = scores.Keys.ToList();
            var found = await ArticleService.ApplyFilter(
                    db.Articles.WherePublished().Where(article => ids.Contains(article.Id)), filter)
                .ToListAsync(ct);
            var authorityByArticle = await ContentGovernanceService.ResolveAuthorityWeightsAsync(db, found, ct);
            var reranked = reranker.Rerank(scope.QueryText, found.Select(article => new RerankCandidate(
                article.Id, article.Title, article.Excerpt, article.Content, scores[article.Id].Score,
                article.UpdatedAt, article.ApprovedAt, article.ContentType,
                authorityByArticle.GetValueOrDefault(article.Id, 50))).ToList()).Take(limit).ToList();
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

        var pageResult = await articles.SearchPublishedPagedAsync(scope.QueryText, page, limit, filter);
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
        var classifications = await articles.GetClassificationsAsync(ids, ct);
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
                SearchSnippetHelper.Build(plainText, snippetTokens),
                classifications.GetValueOrDefault(article.Id));
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
        SearchFailureKind failure = SearchFailureKind.None)
    {
        stopwatch.Stop();
        var record = new SearchQuery
        {
            Query = query.Trim(),
            UserId = principal.Identity?.IsAuthenticated == true ? principal.GetUserId() : null,
            ResultsCount = total,
            SearchType = type,
            ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
        };
        db.SearchQueries.Add(record);
        await db.SaveChangesAsync(ct);

        return new PortalSearchResult(query, type, results, total, page, totalPages,
            stopwatch.ElapsedMilliseconds, record.Id, coverage, tags, warning, failure);
    }
}
