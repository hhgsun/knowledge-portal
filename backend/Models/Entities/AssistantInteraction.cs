namespace KnowledgePortal.Api.Models.Entities;

/// <summary>
/// Privacy-safe assistant decision/feedback audit. Raw user text and generated answers are never
/// persisted; the linked SearchQuery keeps the existing search/RAG quality record when applicable.
/// </summary>
public class AssistantInteraction
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..21];
    public string? UserId { get; set; }
    public string? ApiKeyId { get; set; }
    public string QueryFingerprint { get; set; } = null!;
    public string Route { get; set; } = null!;
    public string RouteSource { get; set; } = null!;
    public string ReasonCode { get; set; } = null!;
    public double Confidence { get; set; }
    public string? SearchQueryId { get; set; }
    public string ToolCallsJson { get; set; } = "[]";
    public long DurationMs { get; set; }
    public bool? Helpful { get; set; }
    public string? FeedbackReason { get; set; }
    public string? CorrectedRoute { get; set; }
    public DateTime? FeedbackAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
    public ApiKey? ApiKey { get; set; }
}
