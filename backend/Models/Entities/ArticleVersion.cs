namespace KnowledgePortal.Api.Models.Entities;

public class ArticleVersion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..21];
    public string ArticleId { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Content { get; set; } // JSON string
    public string ChangedBy { get; set; } = null!;
    public string? ChangeSummary { get; set; }
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Article Article { get; set; } = null!;
    public User ChangedByUser { get; set; } = null!;
}
