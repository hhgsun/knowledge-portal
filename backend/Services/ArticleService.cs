using System.Text.Json;
using System.Security.Claims;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

/// <summary>Composable filter for published-article queries (REST, search, MCP).</summary>
public record ArticleFilter(
    List<string>? OwnerIds = null,
    ICollection<string>? ContentTypes = null,
    string? ApiKeyId = null,
    List<string>? ArticleIds = null,
    IEnumerable<string>? TagSlugs = null,
    IReadOnlyDictionary<string, string[]>? Facets = null);

/// <summary>Presentation metadata shared by article list and search responses.</summary>
public record ArticleEnrichment(
    string Status, string OwnerName, string? OwnerSlug, string? ApiKeyName,
    List<object> Tags, int ViewCount, double WilsonScore,
    string CreatedAt, int? ReadTimeMinutes);

/// <summary>Index gaps within the filters of one search request. RelevantPending is mode-aware
/// and counts distinct articles, so a hybrid request never double-counts an article missing both indexes.</summary>
public record SearchIndexCoverage(string Mode, int FullTextPending, int SemanticPending, int RelevantPending);

/// <summary>
/// Shared article operations: search/listing queries, versioning, tag linking,
/// search-index maintenance, and response enrichment. Controllers and MCP tools
/// call these instead of duplicating the logic.
/// </summary>
public class ArticleService(AppDbContext db, FullTextSearchService ftsService, TagService tagService,
    IndexJobQueue indexJobs, IConfiguration config, ILogger<ArticleService> logger)
{
    public static IQueryable<Article> ApplyFilter(IQueryable<Article> query, ArticleFilter? filter)
    {
        if (filter == null) return query;
        // A non-null empty list means the caller requested author filtering but none of
        // the supplied slugs resolved. It must match nothing, not silently remove the filter.
        if (filter.OwnerIds is not null)
            query = query.WhereOwnedByAny(filter.OwnerIds);
        if (filter.ContentTypes is { Count: > 0 })
            query = query.WhereContentTypeIn(filter.ContentTypes);
        if (!string.IsNullOrWhiteSpace(filter.ApiKeyId))
            query = query.Where(a => a.CreatedViaApiKeyId == filter.ApiKeyId);
        if (filter.ArticleIds != null)
            query = query.Where(a => filter.ArticleIds.Contains(a.Id));
        if (filter.TagSlugs != null)
            query = query.WhereHasAllTags(filter.TagSlugs);
        if (filter.Facets != null)
        {
            foreach (var (category, values) in filter.Facets)
            {
                var categoryValue = category;
                var requestedValues = values;
                query = query.Where(article => article.ArticleLookupValues.Any(assignment =>
                    assignment.LookupValue.IsActive
                    && assignment.LookupValue.CategoryDefinition.IsActive
                    && assignment.LookupValue.Category == categoryValue
                    && requestedValues.Contains(assignment.LookupValue.Value)));
            }
        }
        return query;
    }

    /// <summary>
    /// Calculates index coverage only for articles eligible under the request filters. Full-text
    /// does not depend on semantic embeddings; semantic does not depend on FTS; hybrid/RAG use both.
    /// </summary>
    public async Task<SearchIndexCoverage> GetSearchIndexCoverageAsync(
        string searchType, ArticleFilter? filter = null, CancellationToken ct = default)
    {
        var mode = searchType is "semantic" or "hybrid" or "rag" ? searchType : "fulltext";
        var semanticEnabled = config.GetValue("Ollama:Enabled", false);
        var query = ApplyFilter(db.Articles.WherePublished(), filter);
        var counts = await query
            .GroupBy(_ => 1)
            .Select(group => new
            {
                FullTextPending = group.Count(a => a.FtsIndexedAt == null),
                SemanticPending = semanticEnabled ? group.Count(a => a.IndexedAt == null) : 0,
                CombinedPending = group.Count(a => a.FtsIndexedAt == null
                    || (semanticEnabled && a.IndexedAt == null))
            })
            .SingleOrDefaultAsync(ct);

        var fullTextPending = counts?.FullTextPending ?? 0;
        var semanticPending = counts?.SemanticPending ?? 0;
        var relevantPending = mode switch
        {
            "semantic" => semanticPending,
            "hybrid" or "rag" => counts?.CombinedPending ?? 0,
            _ => fullTextPending
        };
        return new SearchIndexCoverage(mode, fullTextPending, semanticPending, relevantPending);
    }

    /// <summary>
    /// Cap on ranked candidates for the non-relational (InMemory test) search path only, which
    /// scores every published article in memory. Postgres ranks, filters and pages inside a
    /// single statement and needs no cap — see <see cref="FullTextSearchService.SearchPagedAsync"/>.
    /// </summary>
    private const int MaxSearchCandidates = 1000;

    /// <summary>Page of search results plus the true total match count (post-filter).</summary>
    public record SearchPage(List<Article> Articles, int Total);

    /// <summary>
    /// Full-text search over published articles (rank order preserved), falling back to
    /// LIKE matching on title/excerpt when FTS finds nothing (handles special chars better).
    /// Returned entities include Owner and Tags.
    /// </summary>
    public async Task<List<Article>> SearchPublishedAsync(string query, int limit, ArticleFilter? filter = null)
        => (await SearchPublishedPagedAsync(query, 1, limit, filter)).Articles;

    /// <summary>
    /// Paged full-text search over published articles. Matching, filtering (author/tag/
    /// contentType/API key), ranking and paging all happen in one database statement, so
    /// <see cref="SearchPage.Total"/> is the true post-filter match count at any corpus size and
    /// no page under-fills while matches remain.
    /// </summary>
    public async Task<SearchPage> SearchPublishedPagedAsync(string query, int page, int limit, ArticleFilter? filter = null)
    {
        if (db.Database.IsRelational())
        {
            var ftsPage = await ftsService.SearchPagedAsync(query, filter, page, limit);
            if (ftsPage.ArticleIds.Count == 0)
                return new SearchPage([], ftsPage.Total);

            var ranked = await db.Articles
                .Include(a => a.Owner)
                .Include(a => a.ArticleTags).ThenInclude(at => at.Tag)
                .Where(a => ftsPage.ArticleIds.Contains(a.Id))
                .ToListAsync();

            return new SearchPage(
                ArticleQueryExtensions.OrderByRankedIds(ftsPage.ArticleIds, ranked, a => a.Id),
                ftsPage.Total);
        }

        // ── Non-relational (Docker-free InMemory tests): score in memory, then filter and page
        // with EF. Equivalent semantics on a corpus small enough for the candidate cap to be moot.
        var filteredQuery = ApplyFilter(db.Articles.WherePublished(), filter);

        var ftsResults = await ftsService.SearchInMemoryAsync(query, MaxSearchCandidates);
        if (ftsResults.Count > 0)
        {
            var rankedIds = ftsResults.Select(r => r.ArticleId).ToList();
            var matchingIds = (await filteredQuery
                .Where(a => rankedIds.Contains(a.Id))
                .Select(a => a.Id)
                .ToListAsync()).ToHashSet();

            var orderedIds = rankedIds.Where(matchingIds.Contains).ToList();
            var pageIds = orderedIds.Skip((page - 1) * limit).Take(limit).ToList();
            var articles = await db.Articles
                .Include(a => a.Owner)
                .Include(a => a.ArticleTags).ThenInclude(at => at.Tag)
                .Where(a => pageIds.Contains(a.Id))
                .ToListAsync();

            return new SearchPage(
                ArticleQueryExtensions.OrderByRankedIds(pageIds, articles, a => a.Id).ToList(),
                orderedIds.Count);
        }

        // Substring fallback, mirroring the ILIKE rung Postgres runs inside SearchPagedAsync.
        // EF.Functions.Like needs a relational provider, so this path uses literal Contains
        // (equivalent for the plain substrings the tests exercise).
        var likeQuery = filteredQuery
            .Where(a => a.Title.Contains(query) || (a.Excerpt != null && a.Excerpt.Contains(query)));

        var total = await likeQuery.CountAsync();
        var likeArticles = await likeQuery
            .Include(a => a.Owner)
            .Include(a => a.ArticleTags).ThenInclude(at => at.Tag)
            .OrderByDescending(a => a.UpdatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

        return new SearchPage(likeArticles, total);
    }

    /// <summary>Appends the next version snapshot for an article. Caller saves.</summary>
    public async Task<int> AddVersionAsync(string articleId, string title, string? content, string changedBy, string? changeSummary)
    {
        int nextVersion;
        var addedArticle = db.Articles.Local.FirstOrDefault(a => a.Id == articleId
            && db.Entry(a).State == EntityState.Added);
        if (addedArticle != null)
        {
            nextVersion = ++addedArticle.VersionCounter;
        }
        else if (db.Database.IsRelational())
        {
            // Materialize the command directly. SingleAsync() composes LIMIT over the raw SQL,
            // which turns PostgreSQL's UPDATE ... RETURNING into an invalid derived table.
            var allocatedVersions = await db.Database.SqlQueryRaw<int>(
                """
                UPDATE articles
                SET version_counter = version_counter + 1
                WHERE id = {0}
                RETURNING version_counter AS "Value"
                """,
                articleId).ToListAsync();
            nextVersion = allocatedVersions.Single();
        }
        else
        {
            var article = await db.Articles.FindAsync(articleId)
                ?? throw new InvalidOperationException("Article not found while allocating a version");
            nextVersion = ++article.VersionCounter;
        }

        db.ArticleVersions.Add(new ArticleVersion
        {
            ArticleId = articleId,
            Title = title,
            Content = content,
            ChangedBy = changedBy,
            ChangeSummary = changeSummary,
            Version = nextVersion
        });
        return nextVersion;
    }

    public static void InvalidateApproval(Article article)
    {
        article.ApprovedById = null;
        article.ApprovedAt = null;
        article.LastReviewedAt = null;
    }

    /// <summary>
    /// Resolves each input as tag ID, name, or slug and links it to the article.
    /// Missing tags are staged when allowCreate is set. Caller saves article, tags and links atomically.
    /// </summary>
    public async Task AttachTagsAsync(string articleId, string[] tags, bool allowCreate)
    {
        var resolvedIds = new HashSet<string>();
        foreach (var tagInput in tags)
        {
            var tag = await tagService.ResolveAsync(tagInput);
            if (tag == null && allowCreate && !string.IsNullOrWhiteSpace(tagInput))
                (tag, _) = await tagService.FindOrCreateAsync(tagInput, saveChanges: false);
            if (tag != null && resolvedIds.Add(tag.Id))
                db.ArticleTags.Add(new ArticleTag { ArticleId = articleId, TagId = tag.Id });
        }
    }

    /// <summary>
    /// Marks both indexes dirty, durably coalesces an index job, then best-effort refreshes the
    /// local FTS index before returning. Semantic embedding remains exclusively asynchronous.
    /// The worker still re-runs FTS, preserving generation/race recovery guarantees.
    /// </summary>
    public async Task QueueReindexAsync(Article article, CancellationToken ct = default)
    {
        article.FtsIndexedAt = null;
        article.IndexedAt = null;
        await db.SaveChangesAsync(ct);

        // Persist the recovery path before attempting eager FTS. If extraction/PostgreSQL fails,
        // the article mutation remains valid and the durable worker retries the complete job.
        await indexJobs.EnqueueAsync(article.Id, ct: ct);
        await TrySyncFullTextEagerlyAsync(article, ct);
    }

    private async Task TrySyncFullTextEagerlyAsync(Article article, CancellationToken ct)
    {
        var transaction = db.Database.CurrentTransaction;
        string? savepoint = null;
        if (transaction != null)
        {
            // Bulk/source imports own a wider transaction. A failed PostgreSQL statement aborts
            // that transaction unless the eager attempt is isolated behind a savepoint.
            if (!transaction.SupportsSavepoints)
            {
                logger.LogDebug(
                    "Skipping eager full-text sync for article {ArticleId}: ambient transaction does not support savepoints",
                    article.Id);
                return;
            }

            savepoint = $"eager_fts_{Guid.NewGuid():N}";
            await transaction.CreateSavepointAsync(savepoint, ct);
        }

        try
        {
            await ftsService.SyncArticleAsync(article, ct);
            if (savepoint != null)
                await transaction!.ReleaseSavepointAsync(savepoint, ct);
        }
        catch (Exception ex)
        {
            if (savepoint != null)
            {
                try
                {
                    await transaction!.RollbackToSavepointAsync(savepoint, CancellationToken.None);
                }
                catch (Exception rollbackException)
                {
                    logger.LogError(rollbackException,
                        "Could not roll back eager full-text savepoint for article {ArticleId}", article.Id);
                    throw;
                }
            }

            if (ex is OperationCanceledException && ct.IsCancellationRequested)
                throw;

            logger.LogWarning(ex,
                "Eager full-text sync failed for article {ArticleId}; durable index job will retry", article.Id);
        }
    }

    public Task RemoveFromIndexAsync(string articleId) => ftsService.RemoveArticleAsync(articleId);

    /// <summary>Recomputes every published article's search vector in place. Long-running on a
    /// large corpus — see <see cref="FullTextSearchService.RebuildAsync"/>.</summary>
    public Task<int> RebuildIndexAsync(CancellationToken ct = default) => ftsService.RebuildAsync(ct);

    /// <summary>Loads presentation metadata (owner, API key, tags, views, votes) for a set of articles.</summary>
    public async Task<Dictionary<string, ArticleEnrichment>> GetEnrichmentAsync(IEnumerable<string> articleIds)
    {
        var ids = articleIds.ToList();
        if (ids.Count == 0) return new();

        var data = await db.Articles
            .Where(a => ids.Contains(a.Id))
            .Select(a => new
            {
                a.Id,
                a.Status,
                OwnerName = a.Owner.Name,
                OwnerSlug = a.Owner.Slug,
                ApiKeyName = a.CreatedViaApiKeyId != null
                    ? db.ApiKeys.Where(k => k.Id == a.CreatedViaApiKeyId).Select(k => k.Name).FirstOrDefault()
                    : null,
                Tags = a.ArticleTags.Select(at => new { at.Tag.Id, at.Tag.Name, at.Tag.Slug }).ToList(),
                ViewCount = db.ArticleViews.Count(v => v.ArticleId == a.Id),
                HelpfulCount = db.ArticleVotes.Count(v => v.ArticleId == a.Id && v.IsHelpful),
                NotHelpfulCount = db.ArticleVotes.Count(v => v.ArticleId == a.Id && !v.IsHelpful),
                a.CreatedAt,
                a.ReadTimeMinutes
            })
            .ToListAsync();

        return data.ToDictionary(
            a => a.Id,
            a => new ArticleEnrichment(
                a.Status,
                a.OwnerName,
                a.OwnerSlug,
                a.ApiKeyName,
                a.Tags.Select(t => (object)new { t.Id, t.Name, t.Slug }).ToList(),
                a.ViewCount,
                SlugHelper.WilsonScore(a.HelpfulCount, a.NotHelpfulCount),
                a.CreatedAt.ToString("o"),
                a.ReadTimeMinutes));
    }

    /// <summary>Returns canonical value keys grouped by dynamic classification category.</summary>
    public async Task<Dictionary<string, Dictionary<string, string[]>>> GetClassificationsAsync(
        IEnumerable<string> articleIds, CancellationToken ct = default)
    {
        var ids = articleIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        var rows = await db.ArticleLookupValues.AsNoTracking()
            .Where(assignment => ids.Contains(assignment.ArticleId))
            .Select(assignment => new
            {
                assignment.ArticleId,
                assignment.LookupValue.Category,
                assignment.LookupValue.Value
            }).ToListAsync(ct);
        return rows.GroupBy(row => row.ArticleId).ToDictionary(group => group.Key,
            group => group.GroupBy(row => row.Category).ToDictionary(
                category => category.Key,
                category => category.Select(row => row.Value).Distinct().Order().ToArray(),
                StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Loads an article by ID or slug with Owner and Tags included (tracked).</summary>
    public Task<Article?> GetByIdOrSlugAsync(string idOrSlug)
        => db.Articles
            .Include(a => a.Owner)
            .Include(a => a.ArticleTags).ThenInclude(at => at.Tag)
            .FirstOrDefaultAsync(a => a.Id == idOrSlug || a.Slug == idOrSlug);

    /// <summary>Loads an article only when the current principal may view it.</summary>
    public async Task<Article?> GetViewableByIdAsync(string articleId, ClaimsPrincipal principal)
    {
        var article = await db.Articles.FindAsync(articleId);
        if (article == null) return null;
        return RbacService.CanViewArticle(principal, article.Status,
            article.OwnerId == principal.GetUserId()) ? article : null;
    }

    /// <summary>
    /// Builds the full article detail shared by the REST detail endpoint and the MCP get_article tool.
    /// The article must have Owner and Tags loaded (see <see cref="GetByIdOrSlugAsync"/>).
    /// </summary>
    public async Task<ArticleDetailDto> BuildDetailAsync(Article article, bool includeIndexingStatus = false)
    {
        var apiKeyName = article.CreatedViaApiKeyId != null
            ? await db.ApiKeys.Where(k => k.Id == article.CreatedViaApiKeyId).Select(k => k.Name).FirstOrDefaultAsync()
            : null;
        var viewCount = await db.ArticleViews.CountAsync(v => v.ArticleId == article.Id);
        var attachmentMap = await AttachmentHelper.GetAttachmentMapAsync(db, [article.Id]);
        var approvedBy = article.ApprovedById == null ? null
            : await db.Users.Where(u => u.Id == article.ApprovedById).Select(u => u.Name).FirstOrDefaultAsync();
        var indexingStatus = includeIndexingStatus
            ? (await GetIndexingStatusesAsync([article.Id])).GetValueOrDefault(article.Id)
            : null;
        var classifications = (await GetClassificationsAsync([article.Id]))
            .GetValueOrDefault(article.Id);

        return new ArticleDetailDto(
            article.Id, article.Title, article.Slug, article.Excerpt,
            article.Content,
            ContentExtractor.ExtractPlainText(article.Content),
            article.Status, article.ContentType,
            article.OwnerId, article.Owner?.Name, article.Owner?.Slug, apiKeyName,
            article.ReadTimeMinutes,
            article.CreatedAt.ToString("o"), article.UpdatedAt.ToString("o"),
            article.PublishedAt?.ToString("o"), article.LastReviewedAt?.ToString("o"),
            article.ReviewIntervalDays,
            article.ApprovedAt?.ToString("o"), approvedBy,
            article.ArticleTags.Select(at => (object)new { at.Tag.Id, at.Tag.Name, at.Tag.Slug }).ToList(),
            viewCount,
            attachmentMap.GetValueOrDefault(article.Id) ?? [],
            indexingStatus,
            classifications);
    }

    /// <summary>Builds enriched summaries for already-loaded articles, preserving their order (e.g. search rank).</summary>
    public async Task<List<ArticleSummaryDto>> BuildSummariesAsync(IReadOnlyList<Article> articles, bool includeContent = false, bool includeAttachments = false)
    {
        var ids = articles.Select(a => a.Id).ToList();
        var enrichment = await GetEnrichmentAsync(ids);
        var attachmentMap = includeAttachments ? await AttachmentHelper.GetAttachmentMapAsync(db, ids) : null;
        var classifications = await GetClassificationsAsync(ids);

        return articles.Select(a => BuildSummary(
                a.Id, a.Title, a.Slug, a.Excerpt, a.ContentType, a.UpdatedAt.ToString("o"),
                enrichment.GetValueOrDefault(a.Id),
                includeContent ? a.Content : null,
                attachmentMap?.GetValueOrDefault(a.Id),
                classifications: classifications.GetValueOrDefault(a.Id)))
            .ToList();
    }

    /// <summary>
    /// Returns the user-facing state of the combined lexical/semantic index. Article mutations
    /// clear the index timestamps before the durable job is enqueued, so a non-null timestamp
    /// always belongs to the current revision. Existing chunks distinguish a stale semantic
    /// revision from an article that has never completed semantic indexing.
    /// </summary>
    private async Task<Dictionary<string, ArticleIndexingStatusDto>> GetIndexingStatusesAsync(IEnumerable<string> articleIds)
    {
        var ids = articleIds.Distinct().ToList();
        if (ids.Count == 0) return new();

        var semanticEnabled = config.GetValue("Ollama:Enabled", false);
        var articles = await db.Articles
            .Where(a => ids.Contains(a.Id))
            .Select(a => new
            {
                a.Id,
                a.Status,
                a.FtsIndexedAt,
                a.IndexedAt,
                HasEmbeddings = a.ArticleEmbeddings.Any()
            })
            .ToListAsync();
        var jobs = await db.IndexJobs
            .Where(j => ids.Contains(j.ArticleId))
            .ToDictionaryAsync(j => j.ArticleId);

        return articles.ToDictionary(a => a.Id, a =>
        {
            jobs.TryGetValue(a.Id, out var job);
            if (a.Status != "published")
                return new ArticleIndexingStatusDto("not_applicable", null);
            if (job?.Status == "failed")
                return new ArticleIndexingStatusDto("failed", null);
            if (job?.Status == "processing")
                return new ArticleIndexingStatusDto("indexing", null);

            var isCurrent = a.FtsIndexedAt != null && (!semanticEnabled || a.IndexedAt != null);
            if (isCurrent)
            {
                var completedAt = semanticEnabled && a.IndexedAt > a.FtsIndexedAt
                    ? a.IndexedAt
                    : a.FtsIndexedAt;
                return new ArticleIndexingStatusDto("indexed", completedAt?.ToString("o"));
            }

            return new ArticleIndexingStatusDto(
                semanticEnabled && a.HasEmbeddings ? "stale" : "pending",
                null);
        });
    }

    /// <summary>Single construction point for the shared article summary shape.</summary>
    public static ArticleSummaryDto BuildSummary(
        string id, string title, string slug, string? excerpt, string contentType, string updatedAt,
        ArticleEnrichment? enrichment, string? content = null, List<object>? attachments = null,
        double? score = null, string? matchType = null, string? snippet = null,
        Dictionary<string, string[]>? classifications = null)
    {
        return new ArticleSummaryDto(
            id, title, slug, excerpt,
            enrichment?.Status,
            contentType,
            enrichment?.CreatedAt,
            updatedAt,
            enrichment?.OwnerName,
            enrichment?.OwnerSlug,
            enrichment?.ApiKeyName,
            enrichment?.ReadTimeMinutes,
            enrichment?.Tags,
            enrichment?.ViewCount ?? 0,
            enrichment?.WilsonScore ?? 0.0,
            score,
            matchType,
            content,
            attachments,
            snippet,
            null,
            classifications);
    }

    /// <summary>
    /// Pages an article query and returns enriched summaries plus the total count.
    /// Shared by the REST article list and the MCP list_articles tool.
    /// </summary>
    public async Task<(List<ArticleSummaryDto> Articles, int Total)> ListAsync(
        IQueryable<Article> query, int page, int limit, string sort = "updated",
        bool includeContent = false, bool includeAttachments = false,
        bool includeIndexingStatus = false)
    {
        query = sort switch
        {
            "newest" => query.OrderByDescending(a => a.CreatedAt),
            "oldest" => query.OrderBy(a => a.CreatedAt),
            "most_viewed" => query.OrderByDescending(a => a.Views.Count),
            _ => query.OrderByDescending(a => a.UpdatedAt)
        };

        var total = await query.CountAsync();
        var rows = await query
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(a => new
            {
                a.Id, a.Title, a.Slug, a.Excerpt, a.ContentType,
                UpdatedAt = a.UpdatedAt.ToString("o"),
                Content = includeContent ? a.Content : null
            })
            .ToListAsync();

        var enrichment = await GetEnrichmentAsync(rows.Select(r => r.Id));
        var attachmentMap = includeAttachments ? await AttachmentHelper.GetAttachmentMapAsync(db, rows.Select(r => r.Id).ToList()) : null;
        var indexingStatuses = includeIndexingStatus
            ? await GetIndexingStatusesAsync(rows.Select(r => r.Id))
            : null;
        var classifications = await GetClassificationsAsync(rows.Select(row => row.Id));

        var articles = rows.Select(r => BuildSummary(
                r.Id, r.Title, r.Slug, r.Excerpt, r.ContentType, r.UpdatedAt,
                enrichment.GetValueOrDefault(r.Id),
                includeContent ? r.Content : null,
                attachmentMap?.GetValueOrDefault(r.Id),
                classifications: classifications.GetValueOrDefault(r.Id)) with
            {
                IndexingStatus = indexingStatuses?.GetValueOrDefault(r.Id)
            })
            .ToList();

        return (articles, total);
    }
}
