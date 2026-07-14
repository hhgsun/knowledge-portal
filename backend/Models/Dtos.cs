using System.Text.Json.Serialization;

namespace KnowledgePortal.Api.Models;

// Articles
public record CreateArticleRequest(
    string Title,
    object? Content = null,
    string? Excerpt = null,
    string? Status = null,
    string? ContentType = null,
    string[]? Tags = null);

public record UpdateArticleRequest(
    string? Title = null,
    object? Content = null,
    string? Excerpt = null,
    string? Status = null,
    string? ContentType = null,
    string? ChangeSummary = null,
    string[]? Tags = null);

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
    string? Content,
    List<object>? Attachments);

// Full article detail shared by GET /api/articles/{idOrSlug} and the MCP get_article tool.
// Content is the raw TipTap document; ContentText is the extracted plain text.
public record ArticleDetailDto(
    string Id,
    string Title,
    string Slug,
    string? Excerpt,
    object? Content,
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
    List<object>? Tags,
    int ViewCount,
    List<object>? Attachments);

// Tags
public record CreateTagRequest(string Name);
public record UpdateTagRequest(string Id, string Name);
public record TagWithCountDto(string Id, string Name, string Slug, int ArticleCount);

// Lookups
public record CreateLookupRequest(string Category, string Value, string Label, string? Color = null, string? Icon = null, int? SortOrder = null);
public record UpdateLookupRequest(string Id, string? Label = null, string? Color = null, string? Icon = null, int? SortOrder = null, bool? IsActive = null);

// Featured links (sidebar)
public record CreateFeaturedLinkRequest(string Label, string LinkType, string Target, string? Icon = null, int? SortOrder = null);
public record UpdateFeaturedLinkRequest(string Id, string? Label = null, string? Target = null, string? Icon = null, int? SortOrder = null, bool? IsActive = null);
public record FeaturedLinkDto(string Id, string Label, string LinkType, string Target, string? Icon, int SortOrder, bool IsActive);

// Attachments
public record AttachmentResponse(string Id, string FileName, string ContentType, long SizeBytes, string DownloadUrl, string CreatedAt);
public record AttachmentListResponse(AttachmentResponse[] Attachments, int Total);
