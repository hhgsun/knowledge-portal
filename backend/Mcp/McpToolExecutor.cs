using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Mcp;

/// <summary>
/// MCP tool definitions and execution logic for Knowledge Portal.
/// All tools return McpToolCallResult with content array per MCP spec.
/// </summary>
public class McpToolExecutor
{
    private readonly AppDbContext _db;
    private readonly FullTextSearchService _ftsService;

    public McpToolExecutor(AppDbContext db, FullTextSearchService ftsService)
    {
        _db = db;
        _ftsService = ftsService;
    }

    // ─── Tool Registry ─────────────────────────────────────────────────

    public static McpToolsListResult GetToolDefinitions()
    {
        return new McpToolsListResult
        {
            Tools = new List<McpToolDefinition>
            {
                new()
                {
                    Name = "search_articles",
                    Description = "Search published articles in Knowledge Portal using full-text search. Returns matching articles with title, excerpt, author, tags, and optionally full content.",
                    InputSchema = new McpInputSchema
                    {
                        Properties = new Dictionary<string, McpPropertySchema>
                        {
                            ["query"] = new() { Type = "string", Description = "Search query text" },
                            ["limit"] = new() { Type = "integer", Description = "Maximum number of results to return (1-50)", Default = 20 },
                            ["tags"] = new() { Type = "string", Description = "Filter by tag slugs, comma-separated (AND logic)" },
                            ["authors"] = new() { Type = "string", Description = "Filter by author slugs, comma-separated (OR logic)" },
                            ["content_type"] = new() { Type = "string", Description = "Filter by content type, comma-separated (OR logic)" },
                            ["include_content"] = new() { Type = "boolean", Description = "Include full article content as plain text in results", Default = false }
                        },
                        Required = new List<string> { "query" }
                    }
                },
                new()
                {
                    Name = "get_article",
                    Description = "Get full details of a specific published article by its ID or URL slug. Returns title, content, author, tags, attachments, and metadata.",
                    InputSchema = new McpInputSchema
                    {
                        Properties = new Dictionary<string, McpPropertySchema>
                        {
                            ["id_or_slug"] = new() { Type = "string", Description = "Article ID or URL slug" }
                        },
                        Required = new List<string> { "id_or_slug" }
                    }
                },
                new()
                {
                    Name = "list_articles",
                    Description = "List published articles with pagination and optional filtering by content type or tags.",
                    InputSchema = new McpInputSchema
                    {
                        Properties = new Dictionary<string, McpPropertySchema>
                        {
                            ["page"] = new() { Type = "integer", Description = "Page number (1-based)", Default = 1 },
                            ["limit"] = new() { Type = "integer", Description = "Items per page (1-50)", Default = 20 },
                            ["content_type"] = new() { Type = "string", Description = "Filter by content type" },
                            ["tags"] = new() { Type = "string", Description = "Filter by tag slugs, comma-separated" },
                            ["sort"] = new() { Type = "string", Description = "Sort order", Enum = new List<string> { "newest", "oldest", "most_viewed" }, Default = "newest" }
                        }
                    }
                },
                new()
                {
                    Name = "list_tags",
                    Description = "List all available tags in Knowledge Portal with their article counts.",
                    InputSchema = new McpInputSchema
                    {
                        Properties = new Dictionary<string, McpPropertySchema>()
                    }
                },
                new()
                {
                    Name = "get_portal_info",
                    Description = "Get Knowledge Portal statistics and recent activity. Returns total article/author/tag counts and recently published articles.",
                    InputSchema = new McpInputSchema
                    {
                        Properties = new Dictionary<string, McpPropertySchema>()
                    }
                }
            }
        };
    }

    // ─── Tool Dispatcher ───────────────────────────────────────────────

    public async Task<McpToolCallResult> ExecuteToolAsync(string toolName, JsonElement? arguments)
    {
        try
        {
            return toolName switch
            {
                "search_articles" => await SearchArticlesAsync(arguments),
                "get_article" => await GetArticleAsync(arguments),
                "list_articles" => await ListArticlesAsync(arguments),
                "list_tags" => await ListTagsAsync(),
                "get_portal_info" => await GetPortalInfoAsync(),
                _ => ErrorResult($"Unknown tool: {toolName}")
            };
        }
        catch (Exception ex)
        {
            return ErrorResult($"Tool execution failed: {ex.Message}");
        }
    }

    // ─── Tool Implementations ──────────────────────────────────────────

    private async Task<McpToolCallResult> SearchArticlesAsync(JsonElement? args)
    {
        var query = GetString(args, "query");
        if (string.IsNullOrWhiteSpace(query))
            return ErrorResult("Parameter 'query' is required");

        var limit = Math.Clamp(GetInt(args, "limit", 20), 1, 50);
        var tags = GetString(args, "tags");
        var authors = GetString(args, "authors");
        var contentType = GetString(args, "content_type");
        var includeContent = GetBool(args, "include_content");

        var articlesQuery = _db.Articles
            .Include(a => a.Owner)
            .Include(a => a.ArticleTags).ThenInclude(at => at.Tag)
            .Where(a => a.Status == "published");

        // Tag filter (AND logic)
        if (!string.IsNullOrWhiteSpace(tags))
        {
            var tagSlugs = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var tagSlug in tagSlugs)
                articlesQuery = articlesQuery.Where(a => a.ArticleTags.Any(at => at.Tag.Slug == tagSlug));
        }

        // Author filter (OR logic)
        if (!string.IsNullOrWhiteSpace(authors))
        {
            var authorSlugs = authors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var authorIds = await _db.Users
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
        var ftsResults = await _ftsService.SearchAsync(query, limit * 2);
        var matchedIds = ftsResults
            .Where(r => articleIds.Contains(r.ArticleId))
            .OrderByDescending(r => r.Rank)
            .Take(limit)
            .Select(r => r.ArticleId)
            .ToList();

        var articles = await articlesQuery
            .Where(a => matchedIds.Contains(a.Id))
            .ToListAsync();

        var ordered = matchedIds
            .Select(id => articles.FirstOrDefault(a => a.Id == id))
            .Where(a => a != null)
            .Select(a => new
            {
                id = a!.Id,
                title = a.Title,
                slug = a.Slug,
                excerpt = a.Excerpt,
                contentType = a.ContentType,
                author = a.Owner?.Name,
                authorSlug = a.Owner?.Slug,
                tags = a.ArticleTags.Select(at => new { name = at.Tag.Name, slug = at.Tag.Slug }),
                readTimeMinutes = a.ReadTimeMinutes,
                createdAt = a.CreatedAt.ToString("o"),
                updatedAt = a.UpdatedAt.ToString("o"),
                content = includeContent ? ExtractPlainText(a.Content) : null
            })
            .ToList();

        var result = new { articles = ordered, total = ordered.Count, query };
        return TextResult(JsonSerializer.Serialize(result, _jsonOptions));
    }

    private async Task<McpToolCallResult> GetArticleAsync(JsonElement? args)
    {
        var idOrSlug = GetString(args, "id_or_slug");
        if (string.IsNullOrWhiteSpace(idOrSlug))
            return ErrorResult("Parameter 'id_or_slug' is required");

        var article = await _db.Articles
            .Include(a => a.Owner)
            .Include(a => a.ArticleTags).ThenInclude(at => at.Tag)
            .Include(a => a.Attachments)
            .FirstOrDefaultAsync(a => a.Id == idOrSlug || a.Slug == idOrSlug);

        if (article == null || article.Status != "published")
            return ErrorResult("Article not found or not published");

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
            tags = article.ArticleTags.Select(at => new { name = at.Tag.Name, slug = at.Tag.Slug }),
            readTimeMinutes = article.ReadTimeMinutes,
            createdAt = article.CreatedAt.ToString("o"),
            updatedAt = article.UpdatedAt.ToString("o"),
            publishedAt = article.PublishedAt?.ToString("o"),
            content = ExtractPlainText(article.Content),
            attachments = article.Attachments.Select(att => new
            {
                id = att.Id,
                fileName = att.FileName,
                contentType = att.ContentType,
                sizeBytes = att.SizeBytes,
                downloadUrl = $"/api/attachments/{att.Id}/download"
            })
        };

        return TextResult(JsonSerializer.Serialize(result, _jsonOptions));
    }

    private async Task<McpToolCallResult> ListArticlesAsync(JsonElement? args)
    {
        var page = Math.Max(1, GetInt(args, "page", 1));
        var limit = Math.Clamp(GetInt(args, "limit", 20), 1, 50);
        var contentType = GetString(args, "content_type");
        var tags = GetString(args, "tags");
        var sort = GetString(args, "sort") ?? "newest";

        var query = _db.Articles
            .Include(a => a.Owner)
            .Include(a => a.ArticleTags).ThenInclude(at => at.Tag)
            .Where(a => a.Status == "published");

        if (!string.IsNullOrWhiteSpace(contentType))
            query = query.Where(a => a.ContentType == contentType);

        if (!string.IsNullOrWhiteSpace(tags))
        {
            var tagSlugs = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var tagSlug in tagSlugs)
                query = query.Where(a => a.ArticleTags.Any(at => at.Tag.Slug == tagSlug));
        }

        query = sort switch
        {
            "oldest" => query.OrderBy(a => a.CreatedAt),
            "most_viewed" => query.OrderByDescending(a => a.Views.Count),
            _ => query.OrderByDescending(a => a.CreatedAt)
        };

        var total = await query.CountAsync();
        var articles = await query
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
            tags = a.ArticleTags.Select(at => new { name = at.Tag.Name, slug = at.Tag.Slug }),
            readTimeMinutes = a.ReadTimeMinutes,
            createdAt = a.CreatedAt.ToString("o")
        });

        var result = new { articles = results, total, page, limit, totalPages = (int)Math.Ceiling((double)total / limit) };
        return TextResult(JsonSerializer.Serialize(result, _jsonOptions));
    }

    private async Task<McpToolCallResult> ListTagsAsync()
    {
        var tags = await _db.Tags
            .Include(t => t.ArticleTags)
            .OrderBy(t => t.Name)
            .Select(t => new
            {
                id = t.Id,
                name = t.Name,
                slug = t.Slug,
                articleCount = t.ArticleTags.Count(at => at.Article.Status == "published")
            })
            .ToListAsync();

        var result = new { tags, total = tags.Count };
        return TextResult(JsonSerializer.Serialize(result, _jsonOptions));
    }

    private async Task<McpToolCallResult> GetPortalInfoAsync()
    {
        var totalArticles = await _db.Articles.CountAsync(a => a.Status == "published");
        var totalAuthors = await _db.Users.CountAsync();
        var totalTags = await _db.Tags.CountAsync();

        var recentArticles = await _db.Articles
            .Include(a => a.Owner)
            .Where(a => a.Status == "published")
            .OrderByDescending(a => a.CreatedAt)
            .Take(5)
            .Select(a => new
            {
                title = a.Title,
                slug = a.Slug,
                author = a.Owner!.Name,
                createdAt = a.CreatedAt.ToString("o")
            })
            .ToListAsync();

        var contentTypes = await _db.Articles
            .Where(a => a.Status == "published" && a.ContentType != null)
            .GroupBy(a => a.ContentType)
            .Select(g => new { type = g.Key, count = g.Count() })
            .ToListAsync();

        var result = new
        {
            totalArticles,
            totalAuthors,
            totalTags,
            contentTypes,
            recentArticles
        };
        return TextResult(JsonSerializer.Serialize(result, _jsonOptions));
    }

    // ─── Helpers ───────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static McpToolCallResult TextResult(string text)
    {
        return new McpToolCallResult
        {
            Content = new List<McpContent>
            {
                new() { Type = "text", Text = text }
            }
        };
    }

    private static McpToolCallResult ErrorResult(string message)
    {
        return new McpToolCallResult
        {
            IsError = true,
            Content = new List<McpContent>
            {
                new() { Type = "text", Text = message }
            }
        };
    }

    private static string? GetString(JsonElement? args, string property)
    {
        if (args == null) return null;
        return args.Value.TryGetProperty(property, out var val) && val.ValueKind == JsonValueKind.String
            ? val.GetString()
            : null;
    }

    private static int GetInt(JsonElement? args, string property, int defaultValue = 0)
    {
        if (args == null) return defaultValue;
        if (!args.Value.TryGetProperty(property, out var val)) return defaultValue;
        return val.ValueKind == JsonValueKind.Number && val.TryGetInt32(out var i) ? i : defaultValue;
    }

    private static bool GetBool(JsonElement? args, string property, bool defaultValue = false)
    {
        if (args == null) return defaultValue;
        if (!args.Value.TryGetProperty(property, out var val)) return defaultValue;
        return val.ValueKind is JsonValueKind.True or JsonValueKind.False ? val.GetBoolean() : defaultValue;
    }

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
}
