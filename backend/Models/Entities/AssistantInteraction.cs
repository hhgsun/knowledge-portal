namespace KnowledgePortal.Api.Models.Entities;

/// <summary>
/// Privacy-safe grounded-answer audit. Raw user text and generated answers are never persisted;
/// reproducibility and feedback metadata live here, separate from document-search analytics.
/// </summary>
public class AssistantInteraction
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..21];
    public string? UserId { get; set; }
    public string? ApiKeyId { get; set; }
    public string QueryFingerprint { get; set; } = null!;
    public string ApplicationVersion { get; set; } = null!;
    public string? ConversationId { get; set; }
    public string? RagTraceId { get; set; }
    public string? RagPromptVersion { get; set; }
    public string? RagRetrievalVersion { get; set; }
    public string? RagReranker { get; set; }
    public string? RagIndexProfile { get; set; }
    public string? RagGroundingStatus { get; set; }
    public string? RagAnswerHash { get; set; }
    public string? ClickedArticleId { get; set; }
    public string ToolCallsJson { get; set; } = "[]";
    public long DurationMs { get; set; }
    public bool? Helpful { get; set; }
    public string? FeedbackReason { get; set; }
    public DateTime? FeedbackAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
    public ApiKey? ApiKey { get; set; }
    public AssistantConversation? Conversation { get; set; }
}
