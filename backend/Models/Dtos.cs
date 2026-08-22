using System.Text.Json.Serialization;

namespace KnowledgePortal.Api.Models;

// Articles
public record CreateArticleRequest(
    string Title,
    string? ContentMarkdown = null,
    string? Excerpt = null,
    string? Status = null,
    string? ContentType = null,
    string[]? Tags = null,
    int? ReviewIntervalDays = null);

public record UpdateArticleRequest(
    string? Title = null,
    string? ContentMarkdown = null,
    string? Excerpt = null,
    string? Status = null,
    string? ContentType = null,
    string? ChangeSummary = null,
    string[]? Tags = null,
    int? ReviewIntervalDays = null);

// Auth
public record LoginRequest(string Email, string Password);
public record RegisterRequest(string Name, string Email, string Password);
public record AzureLoginRequest(string AccessToken);
public record UpdateProfileRequest(string? Name, string? Email, string? CurrentPassword, string? NewPassword);

// Admin Users
public record CreateUserRequest(string Name, string Email, string Password, string? Role = null);
public record UpdateUserRequest(string UserId, string? Name = null, string? Email = null, string? Password = null, string? Role = null);

// API Keys
public record CreateKeyRequest(string Name, int? ExpiresInDays = null);
public record UpdateKeyRequest(string Id, string? Name = null, int? ExpiresInDays = null);

// Admin API Keys
public record AdminCreateKeyRequest(string UserId, string Name, int? ExpiresInDays = null);
public record AdminUpdateKeyRequest(string Id, string? Name = null, int? ExpiresInDays = null);

// Article Feedback
public record VoteRequest(bool IsHelpful, string? Reason = null);
public record CommentRequest(string Comment);

// Search
public record RecordClickRequest(string SearchQueryId, string ArticleId);
public record RagFeedbackRequest(string SearchQueryId, bool Helpful, string? Reason = null);

// Articles — single summary shape shared by article lists, search results (REST) and MCP tools.
// Score/MatchType only appear on scored (semantic/hybrid) results — hidden when null to keep the wire format per flow.
public record ArticleSummaryDto(
    string Id,
    string Title,
    string Slug,
    string? Excerpt,
    string? Status,
    string ContentType,
    string? CreatedAt,
    string UpdatedAt,
    string? OwnerName,
    string? OwnerSlug,
    string? ApiKeyName,
    int? ReadTimeMinutes,
    List<object>? Tags,
    int ViewCount,
    double WilsonScore,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double? Score,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? MatchType,
    string? ContentMarkdown,
    List<object>? Attachments,
    // Match-context window from the article body (search results only; null → clients show Excerpt)
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Snippet = null,
    // Operational metadata: populated only for editor/admin article-list requests.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ArticleIndexingStatusDto? IndexingStatus = null);

public record ArticleIndexingStatusDto(string State, string? IndexedAt);

// Full article detail shared by GET /api/articles/{idOrSlug} and the MCP get_article tool.
// Markdown is the canonical source; ContentText is its normalized searchable text.
public record ArticleDetailDto(
    string Id,
    string Title,
    string Slug,
    string? Excerpt,
    string? ContentMarkdown,
    string? ContentText,
    string Status,
    string ContentType,
    string OwnerId,
    string? OwnerName,
    string? OwnerSlug,
    string? ApiKeyName,
    int? ReadTimeMinutes,
    string CreatedAt,
    string UpdatedAt,
    string? PublishedAt,
    string? LastReviewedAt,
    string? ApprovedAt,
    string? ApprovedBy,
    List<object>? Tags,
    int ViewCount,
    List<object>? Attachments,
    // Operational metadata: populated only for editor/admin article-detail requests.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ArticleIndexingStatusDto? IndexingStatus = null);

// Tags
public record CreateTagRequest(string Name);
public record UpdateTagRequest(string Id, string Name);
public record TagWithCountDto(string Id, string Name, string Slug, int ArticleCount);
public record TagListResponse(List<TagWithCountDto> Tags, int Total, int Page, int TotalPages);

// Lookups
public record CreateLookupRequest(string Category, string Value, string Label, string? Color = null, string? Icon = null, int? SortOrder = null, int? AuthorityWeight = null);
public record UpdateLookupRequest(string Id, string? Label = null, string? Color = null, string? Icon = null, int? SortOrder = null, bool? IsActive = null, int? AuthorityWeight = null);

// Featured links (sidebar)
public record CreateFeaturedLinkRequest(string Label, string LinkType, string Target, string? Icon = null, string? Color = null, int? SortOrder = null);
public record UpdateFeaturedLinkRequest(string Id, string? Label = null, string? Target = null, string? Icon = null, string? Color = null, int? SortOrder = null, bool? IsActive = null);
public record FeaturedLinkDto(string Id, string Label, string LinkType, string Target, string? Icon, string? Color, int SortOrder, bool IsActive);

// Attachments
public record AttachmentResponse(string Id, string FileName, string ContentType, long SizeBytes, string DownloadUrl,
    string ExtractionStatus, bool ExtractionTruncated, int ExtractedCharacters,
    int ExtractionCharacterLimit, string CreatedAt);
public record AttachmentListResponse(AttachmentResponse[] Attachments, int Total);

// Search diagnostics (admin ops screen). Read-only health report for the FTS and vector indexes.
public record SearchDiagnosticsDto(
    IndexingHealthDto Embedding,
    FullTextHealthDto FullText,
    VectorHealthDto Vector,
    IndexHealthDto[] Indexes,
    PlanProbeDto[] Plans,
    SearchTrafficDto[] Traffic,
    SearchSettingDto[] Settings,
    string[] Warnings);

/// <summary>Queue depth plus the articles that keep failing — "backlog" and "stuck" need
/// opposite responses, so they are never merged into one number.</summary>
public record IndexingHealthDto(int Published, int Indexed, int Pending, int Failing, FailingArticleDto[] TopFailures);
public record FailingArticleDto(string ArticleId, string? Title, int FailureCount, string NextRetryAt);

public record FullTextHealthDto(int Published, int Indexed, int Missing);
public record VectorHealthDto(
    long Chunks, long Articles, long OrphanChunks, string? TableSize, bool DenormalizedFilterColumns);

/// <summary>Existence and validity matter separately: an interrupted build leaves an index
/// Postgres silently refuses to use.</summary>
public record IndexHealthDto(string Name, string Table, bool Exists, bool Valid, string? Size, string? Definition);

/// <summary>One EXPLAIN probe: whether the expected index drives that query shape, plus the plan.</summary>
public record PlanProbeDto(string Name, bool UsesVectorIndex, string Plan, string? Error);

/// <summary>What users actually experienced, per search type, over the reporting window.</summary>
public record SearchTrafficDto(string SearchType, int Total, int ZeroResults, int P50Ms, int P95Ms);

public record SearchSettingDto(string Name, string Value, string? Note);
