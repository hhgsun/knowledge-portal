namespace KnowledgePortal.Api.Models.Entities;

public class AssistantEvaluationCandidate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..21];
    public string InteractionId { get; set; } = null!;
    public string Question { get; set; } = null!;
    public string ActualRoute { get; set; } = null!;
    public string? ExpectedRoute { get; set; }
    public string Reason { get; set; } = null!;
    public string Status { get; set; } = "pending";
    public string? ReviewedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    public AssistantInteraction Interaction { get; set; } = null!;
    public User? ReviewedBy { get; set; }
}

public class AssistantRoutingShadowSample
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..21];
    public string QueryFingerprint { get; set; } = null!;
    public string PrimaryRoute { get; set; } = null!;
    public double PrimaryConfidence { get; set; }
    public string ShadowRoute { get; set; } = null!;
    public double ShadowConfidence { get; set; }
    public string PrimaryModel { get; set; } = null!;
    public string ShadowModel { get; set; } = null!;
    public bool Agreed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AssistantAnswerCacheEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..21];
    public string UserId { get; set; } = null!;
    public string UserScope { get; set; } = null!;
    public string QueryFingerprint { get; set; } = null!;
    public string QueryEmbeddingJson { get; set; } = "[]";
    public string CorpusFingerprint { get; set; } = null!;
    public string RuntimeFingerprint { get; set; } = null!;
    public string AnswerJson { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime LastHitAt { get; set; } = DateTime.UtcNow;
    public int HitCount { get; set; }
    public User User { get; set; } = null!;
}
