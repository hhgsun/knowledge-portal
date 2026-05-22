namespace KnowledgePortal.Api.Models.Entities;

public class ApiKey
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..21];
    public string UserId { get; set; } = null!;
    public string KeyHash { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Permissions { get; set; } // JSON array string
    public DateTime? LastUsedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}
