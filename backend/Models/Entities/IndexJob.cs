namespace KnowledgePortal.Api.Models.Entities;

/// <summary>
/// Durable, coalescing search-index job. There is one row per article; Generation is incremented
/// whenever the article changes so a worker cannot acknowledge work that became stale mid-flight.
/// </summary>
public class IndexJob
{
    public string ArticleId { get; set; } = null!;
    public string Status { get; set; } = "pending";
    public int Generation { get; set; } = 1;
    public int Priority { get; set; } = 100;
    public int AttemptCount { get; set; }
    public DateTime AvailableAt { get; set; } = DateTime.UtcNow;
    public DateTime? LockedAt { get; set; }
    public string? LockedBy { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public Article Article { get; set; } = null!;
}
