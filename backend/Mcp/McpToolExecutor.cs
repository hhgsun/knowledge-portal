using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Diagnostics;
using System.Security.Claims;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
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
    private readonly IConfiguration _config;
    private readonly IServiceProvider _services;
    private readonly ISearchReranker _reranker;
    private readonly ContentGovernanceService _governance;
    private readonly ILogger<McpToolExecutor> _logger;

    private static readonly string[] AllowedSorts = ["newest", "oldest", "most_viewed"];

    public McpToolExecutor(AppDbContext db, ArticleService articleService, TagService tagService,
        IConfiguration config, IServiceProvider services, ISearchReranker reranker, ContentGovernanceService governance,
        ILogger<McpToolExecutor> logger)
    {
        _db = db;
        _articleService = articleService;
        _tagService = tagService;
        _config = config;
        _services = services;
        _reranker = reranker;
        _governance = governance;
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
                    Description = "Search published Knowledge Portal articles using full-text, semantic, hybrid, or RAG search. Supports the same filters and response behavior as GET /api/search.",
                    InputSchema = new McpInputSchema
                    {
                        Properties = new Dictionary<string, McpPropertySchema>
                        {
                            ["query"] = new() { Type = "string", Description = "Search query text" },
                            ["type"] = new() { Type = "string", Description = "Search mode", Enum = new List<string> { "fulltext", "semantic", "hybrid", "rag" }, Default = "fulltext" },
                            ["page"] = new() { Type = "integer", Description = "Page number (1-based)", Default = 1 },
                            ["limit"] = new() { Type = "integer", Description = "Maximum number of results per page (1-50)", Default = 20 },
                            ["tags"] = new() { Type = "string", Description = "Filter by tag slugs, comma-separated (AND logic)" },
                            ["authors"] = new() { Type = "string", Description = "Filter by author slugs, comma-separated (OR logic)" },
                            ["content_type"] = new() { Type = "string", Description = "Filter by content type, comma-separated (OR logic)" },
                            ["include_content"] = new() { Type = "boolean", Description = "Include full article content as plain text in results", Default = false },
                            ["include_attachments"] = new() { Type = "boolean", Description = "Include attachment metadata in results", Default = false },
                            ["only_own_content"] = new() { Type = "boolean", Description = "For API-key callers, restrict results to articles created by that key", Default = false }
                        },
                        Required = new List<string> { "query" }
                    },
                    OutputSchema = SearchOutputSchema()
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
                    },
                    OutputSchema = ObjectOutputSchema("id", "title", "slug", "updatedAt")
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
                    },
                    OutputSchema = ObjectOutputSchema("articles", "total", "page", "limit", "totalPages")
                },
                new()
                {
                    Name = "list_tags",
                    Description = "List all available tags in Knowledge Portal with their article counts.",
                    InputSchema = new McpInputSchema
                    {
                        Properties = new Dictionary<string, McpPropertySchema>()
                    },
                    OutputSchema = ObjectOutputSchema("tags", "total")
                },
                new()
                {
                    Name = "get_portal_info",
                    Description = "Get Knowledge Portal statistics and recent activity. Returns total article/author/tag counts and recently published articles.",
                    InputSchema = new McpInputSchema
                    {
                        Properties = new Dictionary<string, McpPropertySchema>()
                    },
                    OutputSchema = ObjectOutputSchema("totalArticles", "totalAuthors", "totalTags", "contentTypes", "recentArticles")
                },
                new()
                {
                    Name = "get_project_context",
                    Description = "Build a project briefing from articles tagged with the supplied project tag. Returns current published context, governance, evidence, and decision-support warnings.",
                    InputSchema = new McpInputSchema
                    {
                        Properties = new Dictionary<string, McpPropertySchema>
                        {
                            ["project_tag"] = new() { Type = "string", Description = "Project tag slug" },
                            ["limit"] = new() { Type = "integer", Description = "Maximum context articles (1-50)", Default = 20 },
                            ["include_content"] = new() { Type = "boolean", Description = "Include canonical article content", Default = true },
                            ["include_attachments"] = new() { Type = "boolean", Description = "Include attachment metadata", Default = true }
                        },
                        Required = ["project_tag"]
                    },
                    OutputSchema = SearchOutputSchema()
                },
                new()
                {
                    Name = "get_integration_guidance",
                    Description = "Find implementation guidance for an integration task, optionally scoped to a project. Uses hybrid retrieval and returns full evidence/governance metadata.",
                    InputSchema = new McpInputSchema
                    {
                        Properties = new Dictionary<string, McpPropertySchema>
                        {
                            ["integration_query"] = new() { Type = "string", Description = "Integration goal or question" },
                            ["project_tag"] = new() { Type = "string", Description = "Optional project tag slug" },
                            ["limit"] = new() { Type = "integer", Description = "Maximum sources (1-50)", Default = 10 },
                            ["include_attachments"] = new() { Type = "boolean", Description = "Include attachment metadata", Default = true }
                        },
                        Required = ["integration_query"]
                    },
                    OutputSchema = SearchOutputSchema()
                },
                new()
                {
                    Name = "find_authoritative_content",
                    Description = "Find sources for a decision and rank their IDs by dynamic authority, approval, and review freshness without replacing relevance ranking.",
                    InputSchema = new McpInputSchema
                    {
                        Properties = new Dictionary<string, McpPropertySchema>
                        {
                            ["query"] = new() { Type = "string", Description = "Decision topic" },
                            ["project_tag"] = new() { Type = "string", Description = "Optional project tag slug" },
                            ["limit"] = new() { Type = "integer", Description = "Maximum sources (1-50)", Default = 10 }
                        },
                        Required = ["query"]
                    },
                    OutputSchema = SearchOutputSchema()
                },
                new()
                {
                    Name = "compare_sources",
                    Description = "Compare 2-10 published articles side by side with canonical content, provenance and governance. Does not claim semantic contradiction detection.",
                    InputSchema = new McpInputSchema
                    {
                        Properties = new Dictionary<string, McpPropertySchema>
                        {
                            ["article_ids"] = new() { Type = "string", Description = "Comma-separated article IDs or slugs (2-10)" }
                        },
                        Required = ["article_ids"]
                    },
                    OutputSchema = ObjectOutputSchema("sources", "comparison")
                },
                new()
                {
                    Name = "get_recent_changes",
                    Description = "Get recently updated published knowledge, optionally scoped to a project tag, with governance and evidence metadata.",
                    InputSchema = new McpInputSchema
                    {
                        Properties = new Dictionary<string, McpPropertySchema>
                        {
                            ["project_tag"] = new() { Type = "string", Description = "Optional project tag slug" },
                            ["days"] = new() { Type = "integer", Description = "Lookback window (1-3650 days)", Default = 30 },
                            ["limit"] = new() { Type = "integer", Description = "Maximum articles (1-50)", Default = 20 }
                        }
                    },
                    OutputSchema = ObjectOutputSchema("results", "since", "total")
                }
            }
        };
    }

    // ─── Tool Dispatcher ───────────────────────────────────────────────

    public async Task<McpToolCallResult> ExecuteToolAsync(string toolName, JsonElement? arguments,
        ClaimsPrincipal? principal = null, CancellationToken ct = default)
    {
        try
        {
            return toolName switch
            {
                "search_articles" => await SearchArticlesAsync(arguments, principal, ct),
                "get_article" => await GetArticleAsync(arguments),
                "list_articles" => await ListArticlesAsync(arguments),
                "list_tags" => await ListTagsAsync(),
                "get_portal_info" => await GetPortalInfoAsync(),
                "get_project_context" => await GetProjectContextAsync(arguments, principal, ct),
                "get_integration_guidance" => await GetIntegrationGuidanceAsync(arguments, principal, ct),
                "find_authoritative_content" => await FindAuthoritativeContentAsync(arguments, principal, ct),
                "compare_sources" => await CompareSourcesAsync(arguments, ct),
                "get_recent_changes" => await GetRecentChangesAsync(arguments, principal, ct),
                _ => ErrorResult($"Unknown tool: {toolName}")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Full detail server-side only — exception messages can leak internals
            _logger.LogError(ex, "MCP tool {ToolName} execution failed", toolName);
            return ErrorResult("Tool execution failed");
        }
    }

    // ─── Tool Implementations ──────────────────────────────────────────

    private async Task<McpToolCallResult> SearchArticlesAsync(JsonElement? args, ClaimsPrincipal? principal, CancellationToken ct)
    {
        var query = GetString(args, "query");
        if (string.IsNullOrWhiteSpace(query))
            return ErrorResult("Parameter 'query' is required");

        var type = (GetString(args, "type") ?? "fulltext").ToLowerInvariant();
        if (type is not ("fulltext" or "semantic" or "hybrid" or "rag"))
            return ErrorResult("Parameter 'type' must be one of: fulltext, semantic, hybrid, rag");

        var page = Math.Max(1, GetInt(args, "page", 1));
        var limit = Math.Clamp(GetInt(args, "limit", 20), 1, 50);
        var includeContent = GetBool(args, "include_content");
        var includeAttachments = GetBool(args, "include_attachments");
        var onlyOwnContent = GetBool(args, "only_own_content");
        var sw = Stopwatch.StartNew();

        // Same inline syntax as REST: ##content-type, #tag, @author.
        var tagSlugs = SplitCsv(GetString(args, "tags"));
        var authorSlugs = SplitCsv(GetString(args, "authors"));
        var contentTypes = SplitCsv(GetString(args, "content_type"));
        var remainingWords = new List<string>();
        foreach (var word in query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.StartsWith("##") && word.Length > 2) contentTypes.Add(word[2..]);
            else if (word.StartsWith('#') && word.Length > 1) tagSlugs.Add(word[1..]);
            else if (word.StartsWith('@') && word.Length > 1) authorSlugs.Add(word[1..]);
            else remainingWords.Add(word);
        }
        tagSlugs = tagSlugs.Distinct().ToList();
        authorSlugs = authorSlugs.Distinct().ToList();
        contentTypes = contentTypes.Distinct().ToList();
        var searchQuery = string.Join(' ', remainingWords).Trim();

        var authorIds = authorSlugs.Count > 0
            ? await _db.ResolveAuthorIdsAsync(authorSlugs)
            : null;

        // Match REST semantics: an entirely unknown tag set is a definite miss. An unknown
        // author must also remain a restrictive filter instead of widening to every author.
        List<string>? resolvedTags = null;
        if (tagSlugs.Count > 0)
        {
            resolvedTags = await _db.Tags.Where(t => tagSlugs.Contains(t.Slug)).Select(t => t.Slug).ToListAsync(ct);
            if (resolvedTags.Count == 0)
                return await SearchResultAsync(new { results = Array.Empty<object>(), query, type = "tag", tags = tagSlugs, total = 0, page = 1, totalPages = 0 }, query, 0, "tag", sw, principal, ct);
        }

        var apiKeyId = onlyOwnContent ? principal?.FindFirst("apiKeyId")?.Value : null;

        var filter = new ArticleFilter(
            OwnerIds: authorSlugs.Count > 0 ? authorIds : null,
            ContentTypes: contentTypes.Count > 0 ? contentTypes : null,
            ApiKeyId: apiKeyId,
            TagSlugs: resolvedTags);
        var snippetTokens = searchQuery.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (tagSlugs.Count > 0 && string.IsNullOrWhiteSpace(searchQuery))
        {
            var tagQuery = ArticleService.ApplyFilter(_db.Articles.WherePublished(), filter);
            var total = await tagQuery.CountAsync(ct);
            var articles = await tagQuery.OrderByDescending(a => a.UpdatedAt).Skip((page - 1) * limit).Take(limit).ToListAsync(ct);
            var results = await BuildSearchResultsAsync(articles, includeContent, includeAttachments, snippetTokens);
            return await SearchResultAsync(new { results, query, type = "tag", tags = tagSlugs, total, page, totalPages = (int)Math.Ceiling(total / (double)limit) }, query, total, "tag", sw, principal, ct);
        }

        var indexingPending = await _db.Articles.AnyAsync(a => a.Status == "published" && a.IndexedAt == null, ct);
        var ollamaEnabled = _config.GetValue("Ollama:Enabled", false);
        var vectorSearch = ollamaEnabled ? _services.GetService<IVectorSearchService>() : null;

        if (type == "rag")
        {
            if (!ollamaEnabled || vectorSearch == null)
                return TextResult(JsonSerializer.Serialize(new { answer = "AI arama şu anda kullanılamıyor. Ollama servisi aktif değil.", sources = Array.Empty<object>(), query, type, indexingPending }, _jsonOptions));

            var ragService = _services.GetService<RagService>();
            if (ragService == null)
                return TextResult(JsonSerializer.Serialize(new { answer = "RAG servisi kullanılamıyor.", sources = Array.Empty<object>(), query, type, indexingPending }, _jsonOptions));

            try
            {
                var rag = await ragService.AskAsync(searchQuery, filter, ct);
                return await SearchResultAsync(new
                {
                    answer = rag.Answer,
                    sources = rag.Sources.Select(s => new
                    {
                        s.ArticleId, s.Title, s.Slug, s.Score,
                        canonicalUrl = $"/api/articles/{s.Slug}",
                        sourceType = "article"
                    }),
                    query, type, indexingPending
                }, query, rag.Sources.Count, type, sw, principal, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MCP RAG search failed");
                return TextResult(JsonSerializer.Serialize(new { answer = "AI yanıtı oluşturulurken bir hata oluştu.", sources = Array.Empty<object>(), query, type, indexingPending, warning = "RAG search failed" }, _jsonOptions));
            }
        }

        if (type == "semantic")
        {
            if (!ollamaEnabled || vectorSearch == null)
                return TextResult(JsonSerializer.Serialize(new { results = Array.Empty<object>(), query, type, total = 0, page = 1, totalPages = 1, indexingPending, warning = "Semantic search unavailable — Ollama disabled" }, _jsonOptions));
            try
            {
                var hits = await vectorSearch.SearchAsync(searchQuery, limit, ct, filter: filter);
                var ids = hits.Select(h => h.ArticleId).ToList();
                var articles = await ArticleService.ApplyFilter(_db.Articles.WherePublished().Where(a => ids.Contains(a.Id)), filter).ToListAsync(ct);
                var byId = articles.ToDictionary(a => a.Id);
                var results = await BuildSearchResultsAsync(hits.Where(h => byId.ContainsKey(h.ArticleId)).Select(h => byId[h.ArticleId]).ToList(), includeContent, includeAttachments, snippetTokens, hits.ToDictionary(h => h.ArticleId, h => Math.Round(h.Score, 4)));
                return await SearchResultAsync(new { results, query, type, total = results.Count, page = 1, totalPages = 1, indexingPending }, query, results.Count, type, sw, principal, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MCP semantic search failed");
                return TextResult(JsonSerializer.Serialize(new { results = Array.Empty<object>(), query, type, total = 0, page = 1, totalPages = 1, indexingPending, warning = "Semantic search failed" }, _jsonOptions));
            }
        }

        if (type == "hybrid")
        {
            var candidateLimit = Math.Clamp(_config.GetValue("Search:HybridCandidateLimit", 200), limit, 500);
            var fulltextIds = (await _articleService.SearchPublishedAsync(searchQuery, candidateLimit, filter)).Select(a => a.Id).ToList();
            List<VectorSearchResult>? semanticHits = null;
            if (ollamaEnabled && vectorSearch != null)
            {
                try { semanticHits = await vectorSearch.SearchAsync(searchQuery, candidateLimit, ct, filter: filter); }
                catch (Exception ex) { _logger.LogWarning(ex, "MCP hybrid semantic leg failed"); }
            }
            var scores = RrfHelper.Merge(fulltextIds, semanticHits?.Select(h => h.ArticleId).ToList());
            var ids = scores.Keys.ToList();
            var articles = await ArticleService.ApplyFilter(_db.Articles.WherePublished().Where(a => ids.Contains(a.Id)), filter).ToListAsync(ct);
            var reranked = _reranker.Rerank(searchQuery, articles.Select(a => new RerankCandidate(a.Id, a.Title, a.Excerpt, a.Content, scores[a.Id].Score)).ToList()).Take(limit).ToList();
            var byId = articles.ToDictionary(a => a.Id);
            var ordered = reranked.Where(h => byId.ContainsKey(h.ArticleId)).Select(h => byId[h.ArticleId]).ToList();
            var results = await BuildSearchResultsAsync(ordered, includeContent, includeAttachments, snippetTokens,
                reranked.ToDictionary(h => h.ArticleId, h => Math.Round(h.Score, 4)), scores.ToDictionary(s => s.Key, s => s.Value.MatchType));
            var warning = semanticHits == null && ollamaEnabled ? "Semantic search unavailable — using fulltext only" : null;
            return await SearchResultAsync(new { results, query, type, total = results.Count, page = 1, totalPages = 1, indexingPending, warning }, query, results.Count, type, sw, principal, ct);
        }

        var pageResult = await _articleService.SearchPublishedPagedAsync(searchQuery, page, limit, filter);
        var fulltextResults = await BuildSearchResultsAsync(pageResult.Articles, includeContent, includeAttachments, snippetTokens);
        return await SearchResultAsync(new { results = fulltextResults, query, type, total = pageResult.Total, page, totalPages = (int)Math.Ceiling(pageResult.Total / (double)limit), indexingPending }, query, pageResult.Total, type, sw, principal, ct);
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
        var node = JsonSerializer.SerializeToNode(detail, _jsonOptions)!.AsObject();
        AddSecurityAssessment(node);
        var governance = await _governance.BuildAsync([article]);
        node["governance"] = JsonSerializer.SerializeToNode(governance[article.Id], _jsonOptions);
        return StructuredResult(node);
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

    private async Task<McpToolCallResult> GetProjectContextAsync(JsonElement? args, ClaimsPrincipal? principal, CancellationToken ct)
    {
        var projectTag = GetString(args, "project_tag")?.Trim();
        if (string.IsNullOrWhiteSpace(projectTag)) return ErrorResult("Parameter 'project_tag' is required");

        var searchArgs = JsonSerializer.SerializeToElement(new
        {
            query = $"#{projectTag}",
            type = "fulltext",
            limit = Math.Clamp(GetInt(args, "limit", 20), 1, 50),
            include_content = GetBool(args, "include_content", true),
            include_attachments = GetBool(args, "include_attachments", true)
        });
        var result = await SearchArticlesAsync(searchArgs, principal, ct);
        return DecorateTaskResult(result, "project_context", new JsonObject { ["projectTag"] = projectTag });
    }

    private async Task<McpToolCallResult> GetIntegrationGuidanceAsync(JsonElement? args, ClaimsPrincipal? principal, CancellationToken ct)
    {
        var integrationQuery = GetString(args, "integration_query")?.Trim();
        if (string.IsNullOrWhiteSpace(integrationQuery)) return ErrorResult("Parameter 'integration_query' is required");
        var projectTag = GetString(args, "project_tag")?.Trim();

        var searchArgs = JsonSerializer.SerializeToElement(new
        {
            query = string.IsNullOrWhiteSpace(projectTag) ? integrationQuery : $"{integrationQuery} #{projectTag}",
            type = "hybrid",
            limit = Math.Clamp(GetInt(args, "limit", 10), 1, 50),
            include_content = true,
            include_attachments = GetBool(args, "include_attachments", true)
        });
        var result = await SearchArticlesAsync(searchArgs, principal, ct);
        return DecorateTaskResult(result, "integration_guidance", new JsonObject
        {
            ["integrationQuery"] = integrationQuery,
            ["projectTag"] = projectTag
        });
    }

    private async Task<McpToolCallResult> FindAuthoritativeContentAsync(JsonElement? args, ClaimsPrincipal? principal, CancellationToken ct)
    {
        var query = GetString(args, "query")?.Trim();
        if (string.IsNullOrWhiteSpace(query)) return ErrorResult("Parameter 'query' is required");
        var projectTag = GetString(args, "project_tag")?.Trim();

        var searchArgs = JsonSerializer.SerializeToElement(new
        {
            query = string.IsNullOrWhiteSpace(projectTag) ? query : $"{query} #{projectTag}",
            type = "hybrid",
            limit = Math.Clamp(GetInt(args, "limit", 10), 1, 50),
            include_content = true,
            include_attachments = false
        });
        var result = await SearchArticlesAsync(searchArgs, principal, ct);
        return DecorateTaskResult(result, "authoritative_content", new JsonObject
        {
            ["decisionTopic"] = query,
            ["projectTag"] = projectTag,
            ["rankingNote"] = "Use decisionSupport.recommendedArticleIds for governance order; results retain retrieval relevance order."
        });
    }

    private async Task<McpToolCallResult> CompareSourcesAsync(JsonElement? args, CancellationToken ct)
    {
        var references = SplitCsv(GetString(args, "article_ids"));
        if (references.Count is < 2 or > 10)
            return ErrorResult("Parameter 'article_ids' must contain 2-10 comma-separated IDs or slugs");

        var articles = await _db.Articles.WherePublished()
            .Where(a => references.Contains(a.Id) || references.Contains(a.Slug))
            .ToListAsync(ct);
        if (articles.Count != references.Distinct().Count())
            return ErrorResult("One or more articles were not found or are not published");

        articles = references.Select(reference => articles.First(a => a.Id == reference || a.Slug == reference)).ToList();
        var governance = await _governance.BuildAsync(articles, ct);
        var sources = new JsonArray();
        foreach (var article in articles)
        {
            var detail = await _articleService.BuildDetailAsync(article);
            var node = JsonSerializer.SerializeToNode(detail, _jsonOptions)!.AsObject();
            AddSecurityAssessment(node);
            node["governance"] = JsonSerializer.SerializeToNode(governance[article.Id], _jsonOptions);
            node["canonicalUrl"] = $"/api/articles/{article.Slug}";
            sources.Add(node);
        }

        var ordered = governance.OrderByDescending(item => item.Value.ReliabilityScore).ToList();
        return StructuredResult(new JsonObject
        {
            ["sources"] = sources,
            ["comparison"] = new JsonObject
            {
                ["recommendedArticleIds"] = new JsonArray(ordered.Select(item => (JsonNode?)JsonValue.Create(item.Key)).ToArray()),
                ["highestReliabilityScore"] = ordered[0].Value.ReliabilityScore,
                ["requiresCaution"] = ordered.Any(item => item.Value.Warnings.Length > 0),
                ["conflictAssessment"] = "not_evaluated",
                ["note"] = "Sources are compared by recorded governance metadata; their claims were not semantically adjudicated."
            }
        });
    }

    private async Task<McpToolCallResult> GetRecentChangesAsync(JsonElement? args, ClaimsPrincipal? principal, CancellationToken ct)
    {
        var days = Math.Clamp(GetInt(args, "days", 30), 1, 3650);
        var limit = Math.Clamp(GetInt(args, "limit", 20), 1, 50);
        var projectTag = GetString(args, "project_tag")?.Trim();
        var since = DateTime.UtcNow.AddDays(-days);
        var filter = new ArticleFilter(TagSlugs: string.IsNullOrWhiteSpace(projectTag) ? null : [projectTag]);
        var query = ArticleService.ApplyFilter(_db.Articles.WherePublished(), filter)
            .Where(a => a.UpdatedAt >= since).OrderByDescending(a => a.UpdatedAt);
        var total = await query.CountAsync(ct);
        var articles = await query.Take(limit).ToListAsync(ct);
        var results = await BuildSearchResultsAsync(articles, false, false, []);
        var sw = Stopwatch.StartNew();
        var result = await SearchResultAsync(new
        {
            results,
            query = string.IsNullOrWhiteSpace(projectTag) ? $"recent:{days}d" : $"recent:{days}d #{projectTag}",
            type = "recent_changes",
            since = since.ToString("o"),
            total,
            page = 1,
            totalPages = total == 0 ? 0 : 1
        }, $"recent changes {days} days {projectTag}".Trim(), total, "recent_changes", sw, principal, ct);
        return DecorateTaskResult(result, "recent_changes", new JsonObject { ["projectTag"] = projectTag, ["days"] = days });
    }

    // ─── Helpers ───────────────────────────────────────────────────────

    private async Task<List<ArticleSummaryDto>> BuildSearchResultsAsync(
        IReadOnlyList<Article> articles,
        bool includeContent,
        bool includeAttachments,
        string[] snippetTokens,
        IReadOnlyDictionary<string, double>? scores = null,
        IReadOnlyDictionary<string, string>? matchTypes = null)
    {
        var ids = articles.Select(a => a.Id).ToList();
        var enrichment = await _articleService.GetEnrichmentAsync(ids);
        var attachments = includeAttachments
            ? await AttachmentHelper.GetAttachmentMapAsync(_db, ids)
            : null;

        return articles.Select(article =>
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

    private async Task<McpToolCallResult> SearchResultAsync(
        object payload,
        string query,
        int resultCount,
        string searchType,
        Stopwatch stopwatch,
        ClaimsPrincipal? principal,
        CancellationToken ct)
    {
        stopwatch.Stop();
        var record = new SearchQuery
        {
            Query = query.Trim(),
            UserId = principal?.Identity?.IsAuthenticated == true ? principal.GetUserId() : null,
            ResultsCount = resultCount,
            SearchType = searchType,
            ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
        };
        _db.SearchQueries.Add(record);
        await _db.SaveChangesAsync(ct);

        var json = JsonSerializer.SerializeToNode(payload, _jsonOptions)!.AsObject();
        json["responseTimeMs"] = stopwatch.ElapsedMilliseconds;
        json["searchQueryId"] = record.Id;
        await AddGovernanceAsync(json, ct);
        AddEvidence(json);
        return StructuredResult(json);
    }

    private static List<string> SplitCsv(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static McpToolCallResult TextResult(string text)
    {
        JsonNode? structured = null;
        try { structured = JsonNode.Parse(text); }
        catch (JsonException) { /* Plain text is retained for non-JSON results. */ }
        if (structured != null) return StructuredResult(structured);
        return new McpToolCallResult
        {
            Content = new List<McpContent>
            {
                new() { Type = "text", Text = text }
            }
        };
    }

    private static McpToolCallResult StructuredResult(JsonNode value)
    {
        RedactNodeSecrets(value);
        var text = value.ToJsonString(_jsonOptions);
        return new McpToolCallResult
        {
            StructuredContent = value,
            Content = [new McpContent { Type = "text", Text = text }]
        };
    }

    private static McpToolCallResult DecorateTaskResult(McpToolCallResult result, string task, JsonObject metadata)
    {
        if (result.IsError || result.StructuredContent is not JsonObject payload) return result;
        metadata["task"] = task;
        payload["taskContext"] = metadata;
        return StructuredResult(payload);
    }

    private static void AddEvidence(JsonObject payload)
    {
        if (payload["results"] is not JsonArray results) return;

        foreach (var node in results)
        {
            if (node is not JsonObject result) continue;
            AddSecurityAssessment(result);
            var snippet = result["snippet"]?.GetValue<string>();
            var excerpt = result["excerpt"]?.GetValue<string>();
            var passage = !string.IsNullOrWhiteSpace(snippet) ? snippet : excerpt;
            var slug = result["slug"]?.GetValue<string>();
            result["evidenceAvailable"] = !string.IsNullOrWhiteSpace(passage);
            result["evidence"] = new JsonArray
            {
                new JsonObject
                {
                    ["articleId"] = result["id"]?.DeepClone(),
                    ["articleSlug"] = slug,
                    ["canonicalUrl"] = string.IsNullOrWhiteSpace(slug) ? null : $"/api/articles/{slug}",
                    ["sourceType"] = "article",
                    ["passage"] = passage,
                    ["updatedAt"] = result["updatedAt"]?.DeepClone(),
                    ["matchType"] = result["matchType"]?.DeepClone(),
                    ["score"] = result["score"]?.DeepClone()
                }
            };
        }
    }

    private static void AddSecurityAssessment(JsonObject article)
    {
        var text = string.Join('\n', new[] { "title", "excerpt", "snippet", "contentMarkdown", "contentText" }
            .Select(name => article[name]?.GetValue<string>()).Where(value => !string.IsNullOrWhiteSpace(value)));
        article["securityAssessment"] = JsonSerializer.SerializeToNode(ContentSecurityService.Assess(text), _jsonOptions);
    }

    private static void RedactNodeSecrets(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToList())
            {
                if (property.Value is JsonValue value && value.TryGetValue<string>(out var text))
                    obj[property.Key] = ContentSecurityService.RedactSecrets(text);
                else if (property.Value != null)
                    RedactNodeSecrets(property.Value);
            }
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is JsonValue value && value.TryGetValue<string>(out var text))
                    array[i] = ContentSecurityService.RedactSecrets(text);
                else if (array[i] != null)
                    RedactNodeSecrets(array[i]!);
            }
        }
    }

    private async Task AddGovernanceAsync(JsonObject payload, CancellationToken ct)
    {
        if (payload["results"] is not JsonArray results) return;
        var ids = results.OfType<JsonObject>()
            .Select(r => r["id"]?.GetValue<string>()).Where(id => id != null).Cast<string>().ToList();
        var articles = await _db.Articles.Where(a => ids.Contains(a.Id)).ToListAsync(ct);
        var governance = await _governance.BuildAsync(articles, ct);

        foreach (var result in results.OfType<JsonObject>())
        {
            var id = result["id"]?.GetValue<string>();
            if (id != null && governance.TryGetValue(id, out var item))
                result["governance"] = JsonSerializer.SerializeToNode(item, _jsonOptions);
        }

        var values = governance.Values.ToList();
        payload["decisionSupport"] = new JsonObject
        {
            ["highAuthorityCount"] = values.Count(v => v.AuthorityLevel == "high"),
            ["approvalNotRecordedCount"] = values.Count(v => v.ApprovalState == "not_recorded"),
            ["overdueReviewCount"] = values.Count(v => v.ReviewState == "overdue"),
            ["averageReliabilityScore"] = values.Count == 0 ? null : Math.Round(values.Average(v => v.ReliabilityScore), 1),
            ["requiresCaution"] = values.Any(v => v.ApprovalState == "not_recorded" || v.ReviewState is "overdue" or "not_recorded"),
            ["conflictAssessment"] = "not_evaluated",
            ["recommendedArticleIds"] = new JsonArray(governance
                .OrderByDescending(item => item.Value.ReliabilityScore)
                .Select(item => (JsonNode?)JsonValue.Create(item.Key)).ToArray()),
            ["warnings"] = new JsonArray(values.SelectMany(v => v.Warnings).Distinct()
                .Select(warning => (JsonNode?)JsonValue.Create(warning)).ToArray())
        };
    }

    private static JsonObject ObjectOutputSchema(params string[] required) => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = true,
        ["required"] = new JsonArray(required.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray()),
        ["properties"] = new JsonObject(required.ToDictionary(
            name => name,
            name => (JsonNode?)new JsonObject { ["description"] = $"Tool result field '{name}'" }))
    };

    private static JsonObject SearchOutputSchema() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = true,
        ["properties"] = new JsonObject
        {
            ["results"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = true,
                    ["required"] = new JsonArray("id", "title", "slug", "updatedAt", "evidenceAvailable", "evidence"),
                    ["properties"] = new JsonObject
                    {
                        ["id"] = new JsonObject { ["type"] = "string" },
                        ["title"] = new JsonObject { ["type"] = "string" },
                        ["slug"] = new JsonObject { ["type"] = "string" },
                        ["updatedAt"] = new JsonObject { ["type"] = "string" },
                        ["evidenceAvailable"] = new JsonObject { ["type"] = "boolean" },
                        ["evidence"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["required"] = new JsonArray("articleId", "articleSlug", "canonicalUrl", "sourceType"),
                                ["properties"] = new JsonObject
                                {
                                    ["articleId"] = new JsonObject { ["type"] = "string" },
                                    ["articleSlug"] = new JsonObject { ["type"] = "string" },
                                    ["canonicalUrl"] = new JsonObject { ["type"] = "string" },
                                    ["sourceType"] = new JsonObject { ["type"] = "string" },
                                    ["passage"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                                    ["updatedAt"] = new JsonObject { ["type"] = "string" },
                                    ["matchType"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                                    ["score"] = new JsonObject { ["type"] = new JsonArray("number", "null") }
                                }
                            }
                        }
                    }
                }
            },
            ["answer"] = new JsonObject { ["type"] = "string" },
            ["sources"] = new JsonObject { ["type"] = "array" },
            ["query"] = new JsonObject { ["type"] = "string" },
            ["type"] = new JsonObject { ["type"] = "string" }
        },
        ["required"] = new JsonArray("query", "type")
    };

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
