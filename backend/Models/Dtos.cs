namespace KnowledgePortal.Api.Models;

// Articles
public record CreateArticleRequest(
    string Title,
    object? Content = null,
    string? Excerpt = null,
    string? Status = null,
    string? ContentType = null,
    string? Difficulty = null,
    string? Audience = null,
    string[]? Tags = null);

public record UpdateArticleRequest(
    string? Title = null,
    object? Content = null,
    string? Excerpt = null,
    string? Status = null,
    string? ContentType = null,
    string? Difficulty = null,
    string? Audience = null,
    string? ChangeSummary = null,
    string[]? Tags = null);

// Auth
public record LoginRequest(string Email, string Password);
public record RegisterRequest(string Name, string Email, string Password);
public record UpdateProfileRequest(string? Name, string? Email, string? CurrentPassword, string? NewPassword);

// Admin Users
public record CreateUserRequest(string Name, string Email, string Password, string? Role = null);
public record UpdateUserRequest(string UserId, string? Name = null, string? Email = null, string? Password = null, string? Role = null);

// API Keys
public record CreateKeyRequest(string Name, int? ExpiresInDays = null);

// Article Feedback
public record FeedbackRequest(bool Helpful, string? Comment = null);

// Search
public record RecordClickRequest(string SearchQueryId, string ArticleId);

// Tags
public record CreateTagRequest(string Name);
public record UpdateTagRequest(string Id, string Name);
