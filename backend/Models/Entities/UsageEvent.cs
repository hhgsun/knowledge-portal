namespace KnowledgePortal.Api.Models.Entities;

public class UsageEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..21];
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public string? UserId { get; set; }
    public string? ApiKeyId { get; set; }
    public string AuthSource { get; set; } = "anonymous";
    public string Channel { get; set; } = "rest";
    public string Operation { get; set; } = null!;
    public string HttpMethod { get; set; } = null!;
    public string Outcome { get; set; } = "success";
    public int StatusCode { get; set; }
    public long DurationMs { get; set; }
    public User? User { get; set; }
    public ApiKey? ApiKey { get; set; }
}
