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
    private readonly ArticleService _articleService;
    private readonly TagService _tagService;
    private readonly ILogger<McpToolExecutor> _logger;

    private static readonly string[] AllowedSorts = ["newest", "oldest", "most_viewed"];

    public McpToolExecutor(AppDbContext db, ArticleService articleService, TagService tagService,
        ILogger<McpToolExecutor> logger)
    {
        _db = db;
        _articleService = articleService;
        _tagService = tagService;
        _logger = logger;
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
                    Description = "Search published articles in Knowledge Portal using full-text search. Returns matching article summaries (title, excerpt, ownerName, tags, viewCount, …) and optionally full content as plain text.",
                    InputSchema = new McpInputSchema
                    {
                        Properties = new Dictionary<string, McpPropertySchema>
                        {
                            ["query"] = new() { Type = "string", Description = "Search query text" },
                            ["page"] = new() { Type = "integer", Description = "Page number (1-based)", Default = 1 },
                            ["limit"] = new() { Type = "integer", Description = "Maximum number of results per page (1-50)", Default = 20 },
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
                    Description = "Get full details of a specific published article by its ID or URL slug. Returns title, canonical contentMarkdown, normalized contentText, owner, tags, attachments, and metadata — the same shape as the REST article detail endpoint.",
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
            // Full detail server-side only — exception messages can leak internals
            _logger.LogError(ex, "MCP tool {ToolName} execution failed", toolName);
            return ErrorResult("Tool execution failed");
        }
    }

    // ─── Tool Implementations ──────────────────────────────────────────

    private async Task<McpToolCallResult> SearchArticlesAsync(JsonElement? args)
    {
        var query = GetString(args, "query");
        if (string.IsNullOrWhiteSpace(query))
            return ErrorResult("Parameter 'query' is required");

        var page = Math.Max(1, GetInt(args, "page", 1));
        var limit = Math.Clamp(GetInt(args, "limit", 20), 1, 50);
        var tags = GetString(args, "tags");
        var authors = GetString(args, "authors");
        var contentType = GetString(args, "content_type");
        var includeContent = GetBool(args, "include_content");

        // Author filter (OR logic) — tag (AND) and content type filters go through ArticleFilter
        List<string>? authorIds = null;
        if (!string.IsNullOrWhiteSpace(authors))
        {
            var resolved = await _db.ResolveAuthorIdsAsync(authors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            if (resolved.Count > 0)
                authorIds = resolved;
        }

        var filter = new ArticleFilter(
            OwnerIds: authorIds,
            ContentTypes: string.IsNullOrWhiteSpace(contentType) ? null : contentType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            TagSlugs: string.IsNullOrWhiteSpace(tags) ? null : tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        // Same paged pipeline as GET /api/search — true post-filter total, real pagination
        var pageResult = await _articleService.SearchPublishedPagedAsync(query, page, limit, filter);
        var summaries = await _articleService.BuildSummariesAsync(pageResult.Articles, includeContent);

        var result = new
        {
            articles = summaries,
            total = pageResult.Total,
            page,
            limit,
            totalPages = (int)Math.Ceiling(pageResult.Total / (double)limit),
            query
        };
        return TextResult(JsonSerializer.Serialize(result, _jsonOptions));
    }

    private async Task<McpToolCallResult> GetArticleAsync(JsonElement? args)
    {
        var idOrSlug = GetString(args, "id_or_slug");
        if (string.IsNullOrWhiteSpace(idOrSlug))
            return ErrorResult("Parameter 'id_or_slug' is required");

        // Same loader + detail builder as GET /api/articles/{idOrSlug}
        var article = await _articleService.GetByIdOrSlugAsync(idOrSlug);
        if (article == null || article.Status != "published")
            return ErrorResult("Article not found or not published");

        var detail = await _articleService.BuildDetailAsync(article);
        return TextResult(JsonSerializer.Serialize(detail, _jsonOptions));
    }

    private async Task<McpToolCallResult> ListArticlesAsync(JsonElement? args)
    {
        var page = Math.Max(1, GetInt(args, "page", 1));
        var limit = Math.Clamp(GetInt(args, "limit", 20), 1, 50);
        var contentType = GetString(args, "content_type");
        var tags = GetString(args, "tags");
        var sort = GetString(args, "sort") ?? "newest";
        if (!AllowedSorts.Contains(sort))
            return ErrorResult($"Invalid sort '{sort}'. Allowed: {string.Join(", ", AllowedSorts)}");

        // Same filter + paging + summary pipeline as GET /api/articles
        var filter = new ArticleFilter(
            ContentTypes: string.IsNullOrWhiteSpace(contentType) ? null : [contentType],
            TagSlugs: string.IsNullOrWhiteSpace(tags) ? null : tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var query = ArticleService.ApplyFilter(_db.Articles.WherePublished(), filter);

        var (articles, total) = await _articleService.ListAsync(query, page, limit, sort);

        var result = new { articles, total, page, limit, totalPages = (int)Math.Ceiling((double)total / limit) };
        return TextResult(JsonSerializer.Serialize(result, _jsonOptions));
    }

    private async Task<McpToolCallResult> ListTagsAsync()
    {
        // Same listing as GET /api/tags, restricted to published article counts
        var tags = await _tagService.ListWithCountsAsync(publishedOnly: true);

        var result = new { tags, total = tags.Count };
        return TextResult(JsonSerializer.Serialize(result, _jsonOptions));
    }

    private async Task<McpToolCallResult> GetPortalInfoAsync()
    {
        var totalArticles = await _db.Articles.CountAsync(a => a.Status == "published");
        // Authors = distinct owners of published articles; tags = tags actually used
        // by published articles — consistent with the published-only scope of all tools
        var totalAuthors = await _db.Articles.WherePublished()
            .Select(a => a.OwnerId).Distinct().CountAsync();
        var totalTags = await _db.ArticleTags
            .Where(at => at.Article.Status == "published")
            .Select(at => at.TagId).Distinct().CountAsync();

        // Same summary pipeline as the dashboard's recent-articles list
        var (recentArticles, _) = await _articleService.ListAsync(_db.Articles.WherePublished(), 1, 5, "newest");

        var contentTypes = await _db.Articles
            .WherePublished()
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

}
