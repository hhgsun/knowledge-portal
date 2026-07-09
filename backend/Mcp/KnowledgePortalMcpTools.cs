using System.ComponentModel;
using System.Text.Json;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Services;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace KnowledgePortal.Api.Mcp;

[McpServerToolType]
public class KnowledgePortalMcpTools
{
    private static string? ExtractPlainText(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson)) return null;
        try
        {
            var element = JsonDocument.Parse(contentJson).RootElement;
            return ContentExtractor.ExtractTextFromJson(element);
        }
        catch { return null; }
    }

    [McpServerTool, Description("Search articles in the Knowledge Portal. Supports fulltext search. Returns matching articles with title, excerpt, author, tags, and relevance score.")]
    public static async Task<string> SearchArticles(
        [Description("Search query text")] string query,
        [Description("Maximum number of results (1-50, default 20)")] int limit = 20,
        [Description("Tag slugs to filter by (comma-separated)")] string? tags = null,
        [Description("Author slugs to filter by (comma-separated)")] string? authors = null,
        [Description("Content type filter (comma-separated)")] string? contentType = null,
        [Description("Whether to include article content in results")] bool includeContent = false,
        AppDbContext? db = null,
        FullTextSearchService? ftsService = null)
    {
        if (db == null || ftsService == null)
            return "Service unavailable";

        limit = Math.Clamp(limit, 1, 50);

        var articlesQuery = db.Articles
            .Include(a => a.Owner)
            .Include(a => a.ArticleTags).ThenInclude(at => at.Tag)
            .Where(a => a.Status == "published");

        // Tag filter
        if (!string.IsNullOrWhiteSpace(tags))
        {
            var tagSlugs = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var tagSlug in tagSlugs)
            {
                articlesQuery = articlesQuery.Where(a => a.ArticleTags.Any(at => at.Tag.Slug == tagSlug));
            }
        }

        // Author filter
        if (!string.IsNullOrWhiteSpace(authors))
        {
            var authorSlugs = authors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var authorIds = await db.Users
                .Where(u => authorSlugs.Contains(u.Slug))
                .Select(u => u.Id)
                .ToListAsync();
            if (authorIds.Count > 0)
                articlesQuery = articlesQuery.Where(a => authorIds.Contains(a.OwnerId));
        }

        // Content type filter
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            var types = contentType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            articlesQuery = articlesQuery.Where(a => a.ContentType != null && types.Contains(a.ContentType));
        }

        // Full-text search
        var articleIds = await articlesQuery.Select(a => a.Id).ToListAsync();
        var ftsResults = await ftsService.SearchAsync(query, limit * 2);
        var matchedIds = ftsResults
            .Where(r => articleIds.Contains(r.ArticleId))
            .OrderByDescending(r => r.Rank)
            .Take(limit)
            .Select(r => r.ArticleId)
            .ToList();

        var articles = await articlesQuery
            .Where(a => matchedIds.Contains(a.Id))
            .ToListAsync();

        // Order by FTS rank
        var ordered = matchedIds
            .Select(id => articles.FirstOrDefault(a => a.Id == id))
            .Where(a => a != null)
            .ToList();

        var results = ordered.Select(a => new
        {
            id = a!.Id,
            title = a.Title,
            slug = a.Slug,
            excerpt = a.Excerpt,
            status = a.Status,
            contentType = a.ContentType,
            author = a.Owner?.Name,
            authorSlug = a.Owner?.Slug,
            tags = a.ArticleTags.Select(at => at.Tag.Name).ToArray(),
            readTimeMinutes = a.ReadTimeMinutes,
            createdAt = a.CreatedAt.ToString("o"),
            updatedAt = a.UpdatedAt.ToString("o"),
            content = includeContent ? ExtractPlainText(a.Content) : null
        });

        return System.Text.Json.JsonSerializer.Serialize(new { articles = results, total = results.Count() });
    }

    [McpServerTool, Description("Get a specific article by its ID or slug. Returns full article details including content as plain text.")]
    public static async Task<string> GetArticle(
        [Description("Article ID or slug")] string idOrSlug,
        AppDbContext? db = null)
    {
        if (db == null)
            return "Service unavailable";

        var article = await db.Articles
            .Include(a => a.Owner)
            .Include(a => a.ArticleTags).ThenInclude(at => at.Tag)
            .Include(a => a.Attachments)
            .FirstOrDefaultAsync(a => a.Id == idOrSlug || a.Slug == idOrSlug);

        if (article == null || article.Status != "published")
            return "Article not found";

        var result = new
        {
            id = article.Id,
            title = article.Title,
            slug = article.Slug,
            excerpt = article.Excerpt,
            status = article.Status,
            contentType = article.ContentType,
            author = article.Owner?.Name,
            authorSlug = article.Owner?.Slug,
            tags = article.ArticleTags.Select(at => at.Tag.Name).ToArray(),
            readTimeMinutes = article.ReadTimeMinutes,
            createdAt = article.CreatedAt.ToString("o"),
            updatedAt = article.UpdatedAt.ToString("o"),
            contentText = ExtractPlainText(article.Content),
            attachments = article.Attachments.Select(att => new
            {
                id = att.Id,
                fileName = att.FileName,
                contentType = att.ContentType,
                sizeBytes = att.SizeBytes
            }).ToArray()
        };

        return System.Text.Json.JsonSerializer.Serialize(result);
    }

    [McpServerTool, Description("List articles from the Knowledge Portal. Returns published articles with pagination.")]
    public static async Task<string> ListArticles(
        [Description("Page number (default 1)")] int page = 1,
        [Description("Items per page (1-50, default 20)")] int limit = 20,
        [Description("Filter by content type")] string? contentType = null,
        [Description("Filter by tag slug (comma-separated)")] string? tags = null,
        AppDbContext? db = null)
    {
        if (db == null)
            return "Service unavailable";

        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 50);

        var query = db.Articles
            .Include(a => a.Owner)
            .Include(a => a.ArticleTags).ThenInclude(at => at.Tag)
            .Where(a => a.Status == "published");

        if (!string.IsNullOrWhiteSpace(contentType))
            query = query.Where(a => a.ContentType == contentType);

        if (!string.IsNullOrWhiteSpace(tags))
        {
            var tagSlugs = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var tagSlug in tagSlugs)
            {
                query = query.Where(a => a.ArticleTags.Any(at => at.Tag.Slug == tagSlug));
            }
        }

        var total = await query.CountAsync();
        var articles = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .ToListAsync();

        var results = articles.Select(a => new
        {
            id = a.Id,
            title = a.Title,
            slug = a.Slug,
            excerpt = a.Excerpt,
            contentType = a.ContentType,
            author = a.Owner?.Name,
            authorSlug = a.Owner?.Slug,
            tags = a.ArticleTags.Select(at => at.Tag.Name).ToArray(),
            readTimeMinutes = a.ReadTimeMinutes,
            createdAt = a.CreatedAt.ToString("o")
        });

        return System.Text.Json.JsonSerializer.Serialize(new { articles = results, total, page, limit });
    }

    [McpServerTool, Description("List all available tags in the Knowledge Portal.")]
    public static async Task<string> ListTags(AppDbContext? db = null)
    {
        if (db == null)
            return "Service unavailable";

        var tags = await db.Tags
            .Select(t => new { id = t.Id, name = t.Name, slug = t.Slug })
            .OrderBy(t => t.name)
            .ToListAsync();

        return System.Text.Json.JsonSerializer.Serialize(new { tags, total = tags.Count });
    }

    [McpServerTool, Description("Get Knowledge Portal statistics: total articles, authors, tags, and recent activity.")]
    public static async Task<string> GetPortalStats(AppDbContext? db = null)
    {
        if (db == null)
            return "Service unavailable";

        var totalArticles = await db.Articles.CountAsync(a => a.Status == "published");
        var totalAuthors = await db.Users.CountAsync();
        var totalTags = await db.Tags.CountAsync();
        var recentArticles = await db.Articles
            .Where(a => a.Status == "published")
            .OrderByDescending(a => a.CreatedAt)
            .Take(5)
            .Select(a => new { a.Title, a.Slug, a.CreatedAt })
            .ToListAsync();

        return System.Text.Json.JsonSerializer.Serialize(new
        {
            totalArticles,
            totalAuthors,
            totalTags,
            recentArticles = recentArticles.Select(a => new
            {
                title = a.Title,
                slug = a.Slug,
                createdAt = a.CreatedAt.ToString("o")
            })
        });
    }
}
