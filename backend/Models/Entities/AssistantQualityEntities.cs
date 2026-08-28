namespace KnowledgePortal.Api.Models.Entities;

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
