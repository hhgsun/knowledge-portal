namespace KnowledgePortal.Api.Models.Entities;

public class ArticleView
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..21];
    public string ArticleId { get; set; } = null!;
    public string? UserId { get; set; }
    public string? SessionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Article Article { get; set; } = null!;
    public User? User { get; set; }
}
