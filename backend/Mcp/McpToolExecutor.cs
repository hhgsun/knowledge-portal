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
    private readonly SearchExecutionService _searchExecution;
    private readonly KnowledgeAnswerService _knowledgeAnswers;
    private readonly ContentGovernanceService _governance;
    private readonly KnowledgeInputValidationService _inputValidation;
    private readonly ILogger<McpToolExecutor> _logger;
    private readonly McpToolsListResult _definitions;

    private static readonly string[] AllowedSorts = ["newest", "oldest", "most_viewed"];
    private static readonly HashSet<string> KnownToolNames =
    [
        "search_articles", "ask_knowledge", "get_article", "list_articles", "list_tags",
        "get_portal_info", "get_project_context", "get_integration_guidance",
        "find_authoritative_content", "compare_sources", "get_recent_changes"
    ];

    private sealed record McpScope(List<string> Tags, List<string> ContentTypes,
        Dictionary<string, string[]> Facets)
    {
        public bool IsEmpty => Tags.Count == 0 && ContentTypes.Count == 0 && Facets.Count == 0;

        public ArticleFilter ToArticleFilter() => new(
            ContentTypes: ContentTypes.Count == 0 ? null : ContentTypes,
            TagSlugs: Tags.Count == 0 ? null : Tags,
            Facets: Facets.Count == 0 ? null : Facets);
    }

    public McpToolExecutor(AppDbContext db, ArticleService articleService, TagService tagService,
        SearchExecutionService searchExecution, KnowledgeAnswerService knowledgeAnswers,
        ContentGovernanceService governance,
        KnowledgeInputValidationService inputValidation,
        ILogger<McpToolExecutor> logger)
    {
        _db = db;
        _articleService = articleService;
        _tagService = tagService;
        _searchExecution = searchExecution;
        _knowledgeAnswers = knowledgeAnswers;
        _governance = governance;
        _inputValidation = inputValidation;
        _logger = logger;
        _definitions = GetToolDefinitions(inputValidation.MaxQuestionCharacters,
            inputValidation.MaxScopeItems, inputValidation.MaxScopeValueCharacters);
    }

    // ─── Tool Registry ─────────────────────────────────────────────────

    public static McpToolsListResult GetToolDefinitions(
        int maxQuestionCharacters = 4000,
        int maxScopeItems = 50,
        int maxScopeValueCharacters = 200)
    {
        return new McpToolsListResult
        {
            Tools = new List<McpToolDefinition>
            {
                new()
                {
                    Name = "search_articles",
                    Description = "Retrieve published Knowledge Portal articles using full-text, semantic, or hybrid search. Returns ranked documents and never generates an AI answer.",
                    InputSchema = new McpInputSchema
                    {
                        Properties = new Dictionary<string, McpPropertySchema>
                        {
                            ["query"] = new() { Type = "string", Description = "Search query text", MinLength = 1, MaxLength = maxQuestionCharacters },
                            ["type"] = new() { Type = "string", Description = "Search mode", Enum = new List<string> { "fulltext", "semantic", "hybrid" }, Default = "fulltext" },
                            ["page"] = new() { Type = "integer", Description = "Page number (1-based)", Default = 1, Minimum = 1 },
                            ["limit"] = new() { Type = "integer", Description = "Maximum number of results per page (1-50)", Default = 20, Minimum = 1, Maximum = 50 },
                            ["scope"] = ScopePropertySchema(maxScopeItems, maxScopeValueCharacters),
                            ["tags"] = CsvScopeProperty("Legacy scope field: tag slugs, comma-separated (AND logic)", maxScopeItems, maxScopeValueCharacters),
                            ["authors"] = CsvScopeProperty("Filter by author slugs, comma-separated (OR logic)", maxScopeItems, maxScopeValueCharacters),
                            ["content_type"] = CsvScopeProperty("Legacy scope field: content types, comma-separated (OR logic)", maxScopeItems, maxScopeValueCharacters),
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
                    Name = "ask_knowledge",
                    Description = "Generate a grounded AI answer from authorized Knowledge Portal evidence. Returns citations, claims, sources, and evidence; use search_articles when document retrieval is wanted instead.",
                    InputSchema = new McpInputSchema
                    {
                        Properties = new Dictionary<string, McpPropertySchema>
                        {
                            ["question"] = new() { Type = "string", Description = "Question to answer from portal knowledge", MinLength = 1, MaxLength = maxQuestionCharacters },
                            ["scope"] = ScopePropertySchema(maxScopeItems, maxScopeValueCharacters),
                            ["authors"] = CsvScopeProperty("Filter evidence by author slugs, comma-separated (OR logic)", maxScopeItems, maxScopeValueCharacters),
                            ["only_own_content"] = new() { Type = "boolean", Description = "For API-key callers, restrict evidence to articles created by that key", Default = false }
                        },
                        Required = ["question"]
                    },
                    OutputSchema = KnowledgeAnswerOutputSchema()
                },
                new()
                {
                    Name = "get_article",
                    Description = "Get full details of a specific published article by its ID or URL slug. Returns title, canonical contentMarkdown, normalized contentText, owner, tags, attachments, and metadata — the same shape as the REST article detail endpoint.",
                    InputSchema = new McpInputSchema
                    {
                        Properties = new Dictionary<string, McpPropertySchema>
                        {
                            ["id_or_slug"] = new() { Type = "string", Description = "Article ID or URL slug", MinLength = 1, MaxLength = 300 }
                        },
                        Required = new List<string> { "id_or_slug" }
                    },
                    OutputSchema = ObjectOutputSchema("id", "title", "slug", "updatedAt")
                },
                new()
                {
                    Name = "list_articles",
                    Description = "List published articles with pagination and an optional shared scope. Scope supports dynamic category:value facets with OR-within and AND-across semantics.",
                    InputSchema = new McpInputSchema
                    {
                        Properties = new Dictionary<string, McpPropertySchema>
                        {
                            ["page"] = new() { Type = "integer", Description = "Page number (1-based)", Default = 1, Minimum = 1 },
                            ["limit"] = new() { Type = "integer", Description = "Items per page (1-50)", Default = 20, Minimum = 1, Maximum = 50 },
                            ["scope"] = ScopePropertySchema(maxScopeItems, maxScopeValueCharacters),
                            ["content_type"] = CsvScopeProperty("Legacy scope field: content types, comma-separated (OR logic)", maxScopeItems, maxScopeValueCharacters),
                            ["tags"] = CsvScopeProperty("Legacy scope field: tag slugs, comma-separated (AND logic)", maxScopeItems, maxScopeValueCharacters),
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
                    Description = "Build a governed briefing from a required tag/content-type scope. Scope tags use AND logic; content types use OR logic.",
                    InputSchema = new McpInputSchema
                    {
                        Properties = new Dictionary<string, McpPropertySchema>
                        {
                            ["scope"] = ScopePropertySchema(maxScopeItems, maxScopeValueCharacters),
                            ["project_tag"] = ScopeStringProperty("Legacy scope field: one project tag slug", maxScopeValueCharacters),
                            ["limit"] = new() { Type = "integer", Description = "Maximum context articles (1-50)", Default = 20, Minimum = 1, Maximum = 50 },
                            ["include_content"] = new() { Type = "boolean", Description = "Include canonical article content", Default = true },
                            ["include_attachments"] = new() { Type = "boolean", Description = "Include attachment metadata", Default = true }
                        },
                    },
                    OutputSchema = SearchOutputSchema()
                },
                new()
                {
                    Name = "get_integration_guidance",
                    Description = "Find implementation guidance for an integration task, optionally constrained by the shared tag/content-type scope. Uses hybrid retrieval and returns full evidence/governance metadata.",
                    InputSchema = new McpInputSchema
                    {
                        Properties = new Dictionary<string, McpPropertySchema>
                        {
                            ["integration_query"] = new() { Type = "string", Description = "Integration goal or question", MinLength = 1, MaxLength = maxQuestionCharacters },
                            ["scope"] = ScopePropertySchema(maxScopeItems, maxScopeValueCharacters),
                            ["project_tag"] = ScopeStringProperty("Legacy scope field: one optional project tag slug", maxScopeValueCharacters),
                            ["limit"] = new() { Type = "integer", Description = "Maximum sources (1-50)", Default = 10, Minimum = 1, Maximum = 50 },
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
                            ["query"] = new() { Type = "string", Description = "Decision topic", MinLength = 1, MaxLength = maxQuestionCharacters },
                            ["scope"] = ScopePropertySchema(maxScopeItems, maxScopeValueCharacters),
                            ["project_tag"] = ScopeStringProperty("Legacy scope field: one optional project tag slug", maxScopeValueCharacters),
                            ["limit"] = new() { Type = "integer", Description = "Maximum sources (1-50)", Default = 10, Minimum = 1, Maximum = 50 }
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
                            ["article_ids"] = new() { Type = "string", Description = "Comma-separated article IDs or slugs (2-10)", MinLength = 3, MaxLength = 3010 },
                            ["scope"] = ScopePropertySchema(maxScopeItems, maxScopeValueCharacters)
                        },
                        Required = ["article_ids"]
                    },
                    OutputSchema = ObjectOutputSchema("sources", "comparison")
                },
                new()
                {
                    Name = "get_recent_changes",
                    Description = "Get recently updated published knowledge, optionally constrained by the shared tag/content-type scope, with governance and evidence metadata.",
                    InputSchema = new McpInputSchema
                    {
                        Properties = new Dictionary<string, McpPropertySchema>
                        {
                            ["scope"] = ScopePropertySchema(maxScopeItems, maxScopeValueCharacters),
                            ["project_tag"] = ScopeStringProperty("Legacy scope field: one optional project tag slug", maxScopeValueCharacters),
                            ["days"] = new() { Type = "integer", Description = "Lookback window (1-3650 days)", Default = 30, Minimum = 1, Maximum = 3650 },
                            ["limit"] = new() { Type = "integer", Description = "Maximum articles (1-50)", Default = 20, Minimum = 1, Maximum = 50 }
                        }
                    },
                    OutputSchema = ObjectOutputSchema("results", "since", "total")
                }
            }
        };
    }

    public static bool IsKnownTool(string toolName) => KnownToolNames.Contains(toolName);

    public McpToolsListResult GetDefinitions() => _definitions;

    private string? ValidateArguments(string toolName, JsonElement? arguments)
    {
        var tool = _definitions.Tools.FirstOrDefault(item => item.Name == toolName);
        if (tool == null) return $"Unknown tool: {toolName}";

        if (arguments is { ValueKind: not JsonValueKind.Object and not JsonValueKind.Null })
            return "Tool arguments must be an object";

        var supplied = arguments is { ValueKind: JsonValueKind.Object }
            ? arguments.Value.EnumerateObject().ToDictionary(property => property.Name, property => property.Value)
            : new Dictionary<string, JsonElement>();

        foreach (var required in tool.InputSchema.Required ?? [])
        {
            if (!supplied.TryGetValue(required, out var value)
                || value.ValueKind == JsonValueKind.Null
                || value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))
                return $"Parameter '{required}' is required";
        }

        foreach (var argument in supplied)
        {
            if (!tool.InputSchema.Properties.TryGetValue(argument.Key, out var schema))
                return $"Unknown parameter '{argument.Key}'";

            var error = ValidateValue($"Parameter '{argument.Key}'", argument.Value, schema);
            if (error != null) return error;
        }

        return null;
    }

    private static string? ValidateValue(string path, JsonElement value, McpPropertySchema schema)
    {
        var validType = schema.Type switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            _ => true
        };
        if (!validType) return $"{path} must be of type {schema.Type}";

        if (schema.Enum is { Count: > 0 }
            && !schema.Enum.Contains(value.GetString()!, StringComparer.Ordinal))
            return $"{path} must be one of: {string.Join(", ", schema.Enum)}";

        if (schema.Type == "integer")
        {
            var number = value.GetInt32();
            if (schema.Minimum is { } minimum && number < minimum)
                return $"{path} must be at least {minimum}";
            if (schema.Maximum is { } maximum && number > maximum)
                return $"{path} must be at most {maximum}";
        }

        if (schema.Type == "string")
        {
            var length = value.GetString()?.Length ?? 0;
            if (schema.MinLength is { } minLength && length < minLength)
                return $"{path} must contain at least {minLength} characters";
            if (schema.MaxLength is { } maxLength && length > maxLength)
                return $"{path} must contain at most {maxLength} characters";
        }

        if (schema.Type == "object" && schema.Properties != null)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (!schema.Properties.TryGetValue(property.Name, out var childSchema))
                {
                    if (schema.AdditionalProperties == false)
                        return $"Unknown property '{property.Name}' in {path.ToLowerInvariant()}";
                    continue;
                }

                var error = ValidateValue($"{path}.{property.Name}", property.Value, childSchema);
                if (error != null) return error;
            }
        }

        if (schema.Type == "array" && schema.Items != null)
        {
            var itemCount = value.GetArrayLength();
            if (schema.MinItems is { } minItems && itemCount < minItems)
                return $"{path} must contain at least {minItems} items";
            if (schema.MaxItems is { } maxItems && itemCount > maxItems)
                return $"{path} must contain at most {maxItems} items";
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                var error = ValidateValue($"{path}[{index}]", item, schema.Items);
                if (error != null) return error;
                index++;
            }
        }

        return null;
    }

    // ─── Tool Dispatcher ───────────────────────────────────────────────

    public async Task<McpToolCallResult> ExecuteToolAsync(string toolName, JsonElement? arguments,
        ClaimsPrincipal? principal = null, CancellationToken ct = default)
    {
        var validationError = ValidateArguments(toolName, arguments);
        if (validationError != null)
            return ErrorResult(validationError);

        try
        {
            return toolName switch
            {
                "search_articles" => await SearchArticlesAsync(arguments, principal, ct),
                "ask_knowledge" => await AskKnowledgeAsync(arguments, principal, ct),
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
            return ErrorResult("Tool execution failed", "tool_execution_failed", retryable: true);
        }
    }

    // ─── Tool Implementations ──────────────────────────────────────────

    private async Task<McpToolCallResult> SearchArticlesAsync(JsonElement? args, ClaimsPrincipal? principal, CancellationToken ct)
    {
        var query = GetString(args, "query");
        if (string.IsNullOrWhiteSpace(query))
            return ErrorResult("Parameter 'query' is required");

        var (scope, scopeError) = ParseScope(args, "tags", "content_type");
        if (scopeError != null) return ErrorResult(scopeError);

        var principalValue = principal ?? new ClaimsPrincipal();
        var execution = await _searchExecution.ExecuteAsync(new PortalSearchRequest(
            query,
            GetString(args, "type") ?? "fulltext",
            GetInt(args, "limit", 20),
            GetInt(args, "page", 1),
            GetBool(args, "only_own_content"),
            GetBool(args, "include_content"),
            GetBool(args, "include_attachments"),
            scope.Tags,
            SplitCsv(GetString(args, "authors")),
            scope.ContentTypes,
            scope.Facets), principalValue, ct);
        if (execution.Error != null) return ServiceErrorResult(execution.Error);

        var result = execution.Result!;
        if (result.Failure != SearchFailureKind.None)
        {
            return result.Failure switch
            {
                SearchFailureKind.AiUnavailable => McpResilienceService.ResilienceError(
                    "ai_unavailable", result.Warning ?? "Semantic search is unavailable.", true, 30),
                _ => McpResilienceService.ResilienceError(
                    "ai_search_failed", result.Warning ?? "AI search failed.", true, 10)
            };
        }

        var payload = new
        {
            results = result.Results, scope = ScopeNode(scope), result.Query, result.Type, result.Tags,
            result.Total, result.Page, result.TotalPages, result.ResponseTimeMs,
            result.IndexingPending, result.IndexCoverage, result.SearchQueryId, result.Warning
        };

        var json = JsonSerializer.SerializeToNode(payload, _jsonOptions)!.AsObject();
        await AddGovernanceAsync(json, ct);
        AddEvidence(json);
        return StructuredResult(json);
    }

    private async Task<McpToolCallResult> AskKnowledgeAsync(
        JsonElement? args, ClaimsPrincipal? principal, CancellationToken ct)
    {
        var question = GetString(args, "question");
        if (string.IsNullOrWhiteSpace(question))
            return ErrorResult("Parameter 'question' is required");
        var (scope, scopeError) = ParseScope(args);
        if (scopeError != null) return ErrorResult(scopeError);

        var execution = await _knowledgeAnswers.ExecuteAsync(new KnowledgeAnswerRequest(
            question,
            GetBool(args, "only_own_content"),
            scope.Tags,
            SplitCsv(GetString(args, "authors")),
            scope.ContentTypes,
            scope.Facets), principal ?? new ClaimsPrincipal(), ct);
        if (execution.Error != null) return ServiceErrorResult(execution.Error);

        var result = execution.Result!;
        if (result.Failure != KnowledgeAnswerFailureKind.None || result.Rag == null)
        {
            return result.Failure switch
            {
                KnowledgeAnswerFailureKind.Unavailable => McpResilienceService.ResilienceError(
                    "ai_unavailable", result.Warning ?? "Knowledge Assistant is unavailable.", true, 30),
                KnowledgeAnswerFailureKind.Busy => McpResilienceService.ResilienceError(
                    "capacity_full", "Knowledge Assistant capacity is full.", true, 5),
                KnowledgeAnswerFailureKind.CircuitOpen => McpResilienceService.ResilienceError(
                    "circuit_open", "Knowledge Assistant is temporarily unavailable.", true, 30),
                KnowledgeAnswerFailureKind.Timeout => McpResilienceService.ResilienceError(
                    "deadline_exceeded", "Grounded answer generation timed out.", true, 10),
                _ => McpResilienceService.ResilienceError(
                    "answer_failed", result.Warning ?? "Grounded answer generation failed.", true, 10)
            };
        }

        var rag = result.Rag;
        var payload = new
        {
            answer = rag.Answer,
            sources = rag.Sources.Select(source => new
            {
                source.ArticleId, source.Title, source.Slug, source.Score,
                source.AuthorityWeight, source.Approved, source.ReviewState,
                source.ReliabilityScore, source.UpdatedAt,
                canonicalUrl = $"/api/articles/{source.Slug}", sourceType = "article"
            }),
            consultedSources = rag.ConsultedSources.Select(source => new
            {
                source.ArticleId, source.Title, source.Slug, source.Score,
                source.AuthorityWeight, source.Approved, source.ReviewState,
                source.ReliabilityScore, source.UpdatedAt,
                canonicalUrl = $"/api/articles/{source.Slug}", sourceType = "article"
            }),
            claims = rag.Claims,
            evidence = rag.Evidence.Select(evidence => new
            {
                evidence.SourceId, evidence.ArticleId, evidence.Title, evidence.Slug,
                canonicalUrl = $"/api/articles/{evidence.Slug}", evidence.SourceType,
                evidence.AttachmentId, evidence.SourceName, evidence.SourceLocation,
                evidence.Passage, evidence.Score, evidence.ChunkId, evidence.PageNumber
            }),
            rag.CitationCoverage, rag.GroundingStatus, rag.ClaimSupportCoverage,
            rag.InsufficientContext, rag.PartialResult, rag.ConflictAssessment, rag.Warnings,
            scope = ScopeNode(scope), question = result.Question, result.ResponseTimeMs,
            result.IndexingPending, result.IndexCoverage, result.TraceId
        };
        return StructuredResult(JsonSerializer.SerializeToNode(payload, _jsonOptions)!.AsObject());
    }

    private async Task<McpToolCallResult> GetArticleAsync(JsonElement? args)
    {
        var idOrSlug = GetString(args, "id_or_slug");
        if (string.IsNullOrWhiteSpace(idOrSlug))
            return ErrorResult("Parameter 'id_or_slug' is required");

        // Same loader + detail builder as GET /api/articles/{idOrSlug}
        var article = await _articleService.GetByIdOrSlugAsync(idOrSlug);
        if (article == null || article.Status != "published")
            return ErrorResult("Article not found or not published", "not_found");

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
        var (scope, scopeError) = ParseScope(args, "tags", "content_type");
        if (scopeError != null) return ErrorResult(scopeError);
        var sort = GetString(args, "sort") ?? "newest";
        if (!AllowedSorts.Contains(sort))
            return ErrorResult($"Invalid sort '{sort}'. Allowed: {string.Join(", ", AllowedSorts)}");

        // Same filter + paging + summary pipeline as GET /api/articles
        var query = ArticleService.ApplyFilter(_db.Articles.WherePublished(), scope.ToArticleFilter());

        var (articles, total) = await _articleService.ListAsync(query, page, limit, sort);

        var result = new { articles, scope = ScopeNode(scope), total, page, limit, totalPages = (int)Math.Ceiling((double)total / limit) };
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
        var (scope, scopeError) = ParseScope(args, legacyProjectTagProperty: "project_tag");
        if (scopeError != null) return ErrorResult(scopeError);
        if (scope.IsEmpty)
            return ErrorResult("Parameter 'scope' must contain at least one tag or content type");

        var searchArgs = JsonSerializer.SerializeToElement(new
        {
            query = ScopeQuery(scope),
            type = "fulltext",
            limit = Math.Clamp(GetInt(args, "limit", 20), 1, 50),
            include_content = GetBool(args, "include_content", true),
            include_attachments = GetBool(args, "include_attachments", true),
            scope = ScopeNode(scope)
        });
        var result = await SearchArticlesAsync(searchArgs, principal, ct);
        return DecorateTaskResult(result, "project_context", ScopeMetadata(scope));
    }

    private async Task<McpToolCallResult> GetIntegrationGuidanceAsync(JsonElement? args, ClaimsPrincipal? principal, CancellationToken ct)
    {
        var integrationQuery = GetString(args, "integration_query")?.Trim();
        if (string.IsNullOrWhiteSpace(integrationQuery)) return ErrorResult("Parameter 'integration_query' is required");
        var (scope, scopeError) = ParseScope(args, legacyProjectTagProperty: "project_tag");
        if (scopeError != null) return ErrorResult(scopeError);

        var searchArgs = JsonSerializer.SerializeToElement(new
        {
            query = integrationQuery,
            type = "hybrid",
            limit = Math.Clamp(GetInt(args, "limit", 10), 1, 50),
            include_content = true,
            include_attachments = GetBool(args, "include_attachments", true),
            scope = ScopeNode(scope)
        });
        var result = await SearchArticlesAsync(searchArgs, principal, ct);
        var metadata = ScopeMetadata(scope);
        metadata["integrationQuery"] = integrationQuery;
        return DecorateTaskResult(result, "integration_guidance", metadata);
    }

    private async Task<McpToolCallResult> FindAuthoritativeContentAsync(JsonElement? args, ClaimsPrincipal? principal, CancellationToken ct)
    {
        var query = GetString(args, "query")?.Trim();
        if (string.IsNullOrWhiteSpace(query)) return ErrorResult("Parameter 'query' is required");
        var (scope, scopeError) = ParseScope(args, legacyProjectTagProperty: "project_tag");
        if (scopeError != null) return ErrorResult(scopeError);

        var searchArgs = JsonSerializer.SerializeToElement(new
        {
            query,
            type = "hybrid",
            limit = Math.Clamp(GetInt(args, "limit", 10), 1, 50),
            include_content = true,
            include_attachments = false,
            scope = ScopeNode(scope)
        });
        var result = await SearchArticlesAsync(searchArgs, principal, ct);
        var metadata = ScopeMetadata(scope);
        metadata["decisionTopic"] = query;
        metadata["rankingNote"] = "Use decisionSupport.recommendedArticleIds for governance order; results retain retrieval relevance order.";
        return DecorateTaskResult(result, "authoritative_content", metadata);
    }

    private async Task<McpToolCallResult> CompareSourcesAsync(JsonElement? args, CancellationToken ct)
    {
        var references = SplitCsv(GetString(args, "article_ids"));
        if (references.Count is < 2 or > 10)
            return ErrorResult("Parameter 'article_ids' must contain 2-10 comma-separated IDs or slugs");

        var (scope, scopeError) = ParseScope(args);
        if (scopeError != null) return ErrorResult(scopeError);

        var articles = await ArticleService.ApplyFilter(_db.Articles.WherePublished(), scope.ToArticleFilter())
            .Where(a => references.Contains(a.Id) || references.Contains(a.Slug))
            .ToListAsync(ct);
        if (articles.Count != references.Distinct().Count())
            return ErrorResult("One or more articles were not found, are not published, or are outside the requested scope");

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
            ["scope"] = ScopeNode(scope),
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
        var (scope, scopeError) = ParseScope(args, legacyProjectTagProperty: "project_tag");
        if (scopeError != null) return ErrorResult(scopeError);
        var since = DateTime.UtcNow.AddDays(-days);
        var query = ArticleService.ApplyFilter(_db.Articles.WherePublished(), scope.ToArticleFilter())
            .Where(a => a.UpdatedAt >= since).OrderByDescending(a => a.UpdatedAt);
        var total = await query.CountAsync(ct);
        var articles = await query.Take(limit).ToListAsync(ct);
        var results = await BuildSearchResultsAsync(articles, false, false, []);
        var sw = Stopwatch.StartNew();
        var result = await SearchResultAsync(new
        {
            results,
            scope = ScopeNode(scope),
            query = scope.IsEmpty ? $"recent:{days}d" : $"recent:{days}d {ScopeQuery(scope)}",
            type = "recent_changes",
            since = since.ToString("o"),
            total,
            page = 1,
            totalPages = total == 0 ? 0 : 1
        }, $"recent changes {days} days {ScopeQuery(scope)}".Trim(), total, "recent_changes", sw, principal, ct);
        var metadata = ScopeMetadata(scope);
        metadata["days"] = days;
        return DecorateTaskResult(result, "recent_changes", metadata);
    }

    // ─── Helpers ───────────────────────────────────────────────────────

    private static McpPropertySchema ScopePropertySchema(int maxItems, int maxValueCharacters) => new()
    {
        Type = "object",
        Description = "Optional knowledge scope. All tags must match (AND); any listed content type may match (OR). Values are semantic tag slugs and active content-type values.",
        AdditionalProperties = false,
        Properties = new Dictionary<string, McpPropertySchema>
        {
            ["tags"] = new()
            {
                Type = "array",
                Description = "Tag slugs; every tag must be present on the article (AND logic)",
                MaxItems = maxItems,
                Items = new McpPropertySchema { Type = "string", MinLength = 1, MaxLength = maxValueCharacters }
            },
            ["contentTypes"] = new()
            {
                Type = "array",
                Description = "Content-type values; an article may match any supplied value (OR logic)",
                MaxItems = maxItems,
                Items = new McpPropertySchema { Type = "string", MinLength = 1, MaxLength = maxValueCharacters }
            },
            ["facets"] = new()
            {
                Type = "array",
                Description = "Generic classifications as category:value pairs; categories combine with AND and values within a category with OR",
                MaxItems = maxItems,
                Items = new McpPropertySchema { Type = "string", MinLength = 3, MaxLength = maxValueCharacters * 2 + 1 }
            }
        }
    };

    private static McpPropertySchema ScopeStringProperty(string description, int maxValueCharacters) =>
        new() { Type = "string", Description = description, MinLength = 1, MaxLength = maxValueCharacters };

    private static McpPropertySchema CsvScopeProperty(
        string description, int maxItems, int maxValueCharacters) =>
        new()
        {
            Type = "string", Description = description, MinLength = 1,
            MaxLength = maxItems * (maxValueCharacters + 1)
        };

    private (McpScope Scope, string? Error) ParseScope(
        JsonElement? args,
        string? legacyTagsProperty = null,
        string? legacyContentTypeProperty = null,
        string? legacyProjectTagProperty = null)
    {
        var tags = new List<string>();
        var contentTypes = new List<string>();
        var facets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (args is { ValueKind: JsonValueKind.Object }
            && args.Value.TryGetProperty("scope", out var scopeElement))
        {
            if (scopeElement.TryGetProperty("tags", out var scopeTags))
            {
                foreach (var value in scopeTags.EnumerateArray())
                {
                    var item = value.GetString()?.Trim();
                    if (string.IsNullOrWhiteSpace(item))
                        return (new McpScope([], [], []), "Parameter 'scope.tags' cannot contain blank values");
                    tags.Add(item);
                }
            }

            if (scopeElement.TryGetProperty("contentTypes", out var scopeContentTypes))
            {
                foreach (var value in scopeContentTypes.EnumerateArray())
                {
                    var item = value.GetString()?.Trim();
                    if (string.IsNullOrWhiteSpace(item))
                        return (new McpScope([], [], []), "Parameter 'scope.contentTypes' cannot contain blank values");
                    contentTypes.Add(item);
                }
            }

            if (scopeElement.TryGetProperty("facets", out var scopeFacets))
            {
                foreach (var value in scopeFacets.EnumerateArray())
                {
                    var item = value.GetString()?.Trim();
                    var parts = item?.Split(':', 2, StringSplitOptions.TrimEntries);
                    if (parts is not { Length: 2 } || parts.Any(part => part.Length == 0))
                        return (new McpScope([], [], []),
                            "Parameter 'scope.facets' values must use category:value format");
                    if (!facets.TryGetValue(parts[0], out var values)) facets[parts[0]] = values = [];
                    values.Add(parts[1]);
                }
            }
        }

        if (legacyTagsProperty != null)
            tags.AddRange(SplitCsv(GetString(args, legacyTagsProperty)));
        if (legacyContentTypeProperty != null)
            contentTypes.AddRange(SplitCsv(GetString(args, legacyContentTypeProperty)));
        if (legacyProjectTagProperty != null
            && GetString(args, legacyProjectTagProperty)?.Trim() is { Length: > 0 } projectTag)
            tags.Add(projectTag);

        var scope = new McpScope(
            tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            contentTypes.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            facets.ToDictionary(entry => entry.Key,
                entry => entry.Value.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase));
        var validation = _inputValidation.ValidateScope(scope.Tags, null, scope.ContentTypes, scope.Facets);
        return (scope, validation?.Message);
    }

    private static JsonObject ScopeNode(McpScope scope) => new()
    {
        ["tags"] = new JsonArray(scope.Tags.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
        ["contentTypes"] = new JsonArray(scope.ContentTypes.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
        ["facets"] = new JsonArray(scope.Facets.SelectMany(entry => entry.Value
            .Select(value => (JsonNode?)JsonValue.Create($"{entry.Key}:{value}"))).ToArray())
    };

    private static JsonObject ScopeMetadata(McpScope scope) => new()
    {
        ["scope"] = ScopeNode(scope)
    };

    private static string ScopeQuery(McpScope scope) => string.Join(' ',
        scope.Tags.Select(tag => $"#{tag}")
            .Concat(scope.ContentTypes.Select(contentType => $"+content_type:{contentType}"))
            .Concat(scope.Facets.SelectMany(entry => entry.Value
                .Select(value => $"+{entry.Key}:{value}"))));

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
        var classifications = await _articleService.GetClassificationsAsync(ids);
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
                SearchSnippetHelper.Build(plainText, snippetTokens),
                classifications.GetValueOrDefault(article.Id));
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
        ["oneOf"] = SuccessOrError(required),
        ["properties"] = new JsonObject(required.ToDictionary(
            name => name,
            name => (JsonNode?)OutputPropertySchema(name)))
        {
            ["error"] = ErrorPropertySchema()
        }
    };

    private static JsonArray SuccessOrError(params string[] required) => new(
        new JsonObject
        {
            ["required"] = new JsonArray(required.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray())
        },
        new JsonObject
        {
            ["required"] = new JsonArray("error")
        });

    private static JsonObject ErrorPropertySchema() => new()
    {
        ["type"] = "object",
        ["required"] = new JsonArray("code", "message", "retryable"),
        ["properties"] = new JsonObject
        {
            ["code"] = new JsonObject { ["type"] = "string" },
            ["message"] = new JsonObject { ["type"] = "string" },
            ["retryable"] = new JsonObject { ["type"] = "boolean" },
            ["retryAfterSeconds"] = new JsonObject { ["type"] = new JsonArray("integer", "null") },
            ["details"] = new JsonObject { ["type"] = new JsonArray("object", "null") }
        }
    };

    private static JsonObject OutputPropertySchema(string name)
    {
        var type = name switch
        {
            "articles" or "tags" or "contentTypes" or "recentArticles" or "results" or "sources" => "array",
            "total" or "page" or "limit" or "totalPages" or "totalArticles" or "totalAuthors" or "totalTags" => "integer",
            "comparison" => "object",
            _ => "string"
        };
        var schema = new JsonObject
        {
            ["type"] = type,
            ["description"] = $"Tool result field '{name}'"
        };
        if (type == "array") schema["items"] = new JsonObject();
        return schema;
    }

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
            ["indexingPending"] = new JsonObject { ["type"] = "boolean" },
            ["indexCoverage"] = new JsonObject
            {
                ["type"] = "object",
                ["required"] = new JsonArray("mode", "fullTextPending", "semanticPending", "relevantPending"),
                ["properties"] = new JsonObject
                {
                    ["mode"] = new JsonObject { ["type"] = "string" },
                    ["fullTextPending"] = new JsonObject { ["type"] = "integer" },
                    ["semanticPending"] = new JsonObject { ["type"] = "integer" },
                    ["relevantPending"] = new JsonObject { ["type"] = "integer" }
                }
            },
            ["query"] = new JsonObject { ["type"] = "string" },
            ["type"] = new JsonObject { ["type"] = "string" },
            ["error"] = ErrorPropertySchema()
        },
        ["oneOf"] = SuccessOrError("query", "type")
    };

    private static JsonObject KnowledgeAnswerOutputSchema() => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = true,
        ["properties"] = new JsonObject
        {
            ["answer"] = new JsonObject { ["type"] = "string" },
            ["sources"] = new JsonObject { ["type"] = "array" },
            ["consultedSources"] = new JsonObject { ["type"] = "array" },
            ["claims"] = new JsonObject { ["type"] = "array" },
            ["evidence"] = new JsonObject { ["type"] = "array" },
            ["citationCoverage"] = new JsonObject { ["type"] = "number" },
            ["claimSupportCoverage"] = new JsonObject { ["type"] = "number" },
            ["groundingStatus"] = new JsonObject { ["type"] = "string" },
            ["insufficientContext"] = new JsonObject { ["type"] = "boolean" },
            ["partialResult"] = new JsonObject { ["type"] = "boolean" },
            ["warnings"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
            ["question"] = new JsonObject { ["type"] = "string" },
            ["traceId"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
            ["error"] = ErrorPropertySchema()
        },
        ["oneOf"] = SuccessOrError("question", "answer", "sources", "evidence")
    };

    private static McpToolCallResult ServiceErrorResult(ServiceError error) => error.StatusCode switch
    {
        404 => ErrorResult(error.Message, "not_found"),
        429 => ErrorResult(error.Message, "capacity_full", retryable: true, retryAfterSeconds: 5),
        >= 500 => ErrorResult(error.Message, "service_unavailable", retryable: true, retryAfterSeconds: 10),
        _ => ErrorResult(error.Message)
    };

    private static McpToolCallResult ErrorResult(
        string message,
        string code = "invalid_arguments",
        bool retryable = false,
        int? retryAfterSeconds = null) =>
        McpResilienceService.ResilienceError(code, message, retryable, retryAfterSeconds);

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
