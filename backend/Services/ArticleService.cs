using System.Text.Json;
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
    IEnumerable<string>? TagSlugs = null);

/// <summary>Presentation metadata shared by article list and search responses.</summary>
public record ArticleEnrichment(
    string Status, string OwnerName, string? OwnerSlug, string? ApiKeyName,
    List<object> Tags, int ViewCount, double WilsonScore,
    string CreatedAt, int? ReadTimeMinutes);

/// <summary>
/// Shared article operations: search/listing queries, versioning, tag linking,
/// search-index maintenance, and response enrichment. Controllers and MCP tools
/// call these instead of duplicating the logic.
/// </summary>
public class ArticleService(AppDbContext db, FullTextSearchService ftsService, TagService tagService, IndexJobQueue indexJobs)
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
        return query;
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
        var maxVersion = await db.ArticleVersions
            .Where(v => v.ArticleId == articleId)
            .MaxAsync(v => (int?)v.Version) ?? 0;

        db.ArticleVersions.Add(new ArticleVersion
        {
            ArticleId = articleId,
            Title = title,
            Content = content,
            ChangedBy = changedBy,
            ChangeSummary = changeSummary,
            Version = maxVersion + 1
        });
        return maxVersion + 1;
    }

    /// <summary>
    /// Resolves each input as tag ID, name, or slug and links it to the article.
    /// Missing tags are staged when allowCreate is set. Caller saves article, tags and links atomically.
    /// </summary>
    public async Task AttachTagsAsync(string articleId, string[] tags, bool allowCreate)
    {
        foreach (var tagInput in tags)
        {
            var tag = await tagService.ResolveAsync(tagInput);
            if (tag == null && allowCreate && !string.IsNullOrWhiteSpace(tagInput))
                (tag, _) = await tagService.FindOrCreateAsync(tagInput, saveChanges: false);
            if (tag != null)
                db.ArticleTags.Add(new ArticleTag { ArticleId = articleId, TagId = tag.Id });
        }
    }

    /// <summary>
    /// Content of an article changed: mark semantic state dirty and durably coalesce an index job.
    /// </summary>
    public async Task QueueReindexAsync(Article article)
    {
        if (article.Status == "published") article.IndexedAt = null;
        await db.SaveChangesAsync();
        await indexJobs.EnqueueAsync(article.Id);
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

    /// <summary>Loads an article by ID or slug with Owner and Tags included (tracked).</summary>
    public Task<Article?> GetByIdOrSlugAsync(string idOrSlug)
        => db.Articles
            .Include(a => a.Owner)
            .Include(a => a.ArticleTags).ThenInclude(at => at.Tag)
            .FirstOrDefaultAsync(a => a.Id == idOrSlug || a.Slug == idOrSlug);

    /// <summary>
    /// Builds the full article detail shared by the REST detail endpoint and the MCP get_article tool.
    /// The article must have Owner and Tags loaded (see <see cref="GetByIdOrSlugAsync"/>).
    /// </summary>
    public async Task<ArticleDetailDto> BuildDetailAsync(Article article)
    {
        var apiKeyName = article.CreatedViaApiKeyId != null
            ? await db.ApiKeys.Where(k => k.Id == article.CreatedViaApiKeyId).Select(k => k.Name).FirstOrDefaultAsync()
            : null;
        var viewCount = await db.ArticleViews.CountAsync(v => v.ArticleId == article.Id);
        var attachmentMap = await AttachmentHelper.GetAttachmentMapAsync(db, [article.Id]);

        return new ArticleDetailDto(
            article.Id, article.Title, article.Slug, article.Excerpt,
            article.Content,
            ContentExtractor.ExtractPlainText(article.Content),
            article.Status, article.ContentType,
            article.OwnerId, article.Owner?.Name, article.Owner?.Slug, apiKeyName,
            article.ReadTimeMinutes,
            article.CreatedAt.ToString("o"), article.UpdatedAt.ToString("o"),
            article.PublishedAt?.ToString("o"), article.LastReviewedAt?.ToString("o"),
            article.ArticleTags.Select(at => (object)new { at.Tag.Id, at.Tag.Name, at.Tag.Slug }).ToList(),
            viewCount,
            attachmentMap.GetValueOrDefault(article.Id) ?? []);
    }

    /// <summary>Builds enriched summaries for already-loaded articles, preserving their order (e.g. search rank).</summary>
    public async Task<List<ArticleSummaryDto>> BuildSummariesAsync(IReadOnlyList<Article> articles, bool includeContent = false, bool includeAttachments = false)
    {
        var ids = articles.Select(a => a.Id).ToList();
        var enrichment = await GetEnrichmentAsync(ids);
        var attachmentMap = includeAttachments ? await AttachmentHelper.GetAttachmentMapAsync(db, ids) : null;

        return articles.Select(a => BuildSummary(
                a.Id, a.Title, a.Slug, a.Excerpt, a.ContentType, a.UpdatedAt.ToString("o"),
                enrichment.GetValueOrDefault(a.Id),
                includeContent ? a.Content : null,
                attachmentMap?.GetValueOrDefault(a.Id)))
            .ToList();
    }

    /// <summary>Single construction point for the shared article summary shape.</summary>
    public static ArticleSummaryDto BuildSummary(
        string id, string title, string slug, string? excerpt, string contentType, string updatedAt,
        ArticleEnrichment? enrichment, string? content = null, List<object>? attachments = null,
        double? score = null, string? matchType = null, string? snippet = null)
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
            snippet);
    }

    /// <summary>
    /// Pages an article query and returns enriched summaries plus the total count.
    /// Shared by the REST article list and the MCP list_articles tool.
    /// </summary>
    public async Task<(List<ArticleSummaryDto> Articles, int Total)> ListAsync(
        IQueryable<Article> query, int page, int limit, string sort = "updated",
        bool includeContent = false, bool includeAttachments = false)
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

        var articles = rows.Select(r => BuildSummary(
                r.Id, r.Title, r.Slug, r.Excerpt, r.ContentType, r.UpdatedAt,
                enrichment.GetValueOrDefault(r.Id),
                includeContent ? r.Content : null,
                attachmentMap?.GetValueOrDefault(r.Id)))
            .ToList();

        return (articles, total);
    }
}
