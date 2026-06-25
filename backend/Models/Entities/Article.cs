namespace KnowledgePortal.Api.Models.Entities;

public class Article
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..21];
    public string Title { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Content { get; set; } // JSON string (TipTap)
    public string? Excerpt { get; set; }
    public string Status { get; set; } = "draft";
    public string OwnerId { get; set; } = null!;
    public string ContentType { get; set; } = "reference";
    public string? Audience { get; set; }
    public string? CreatedViaApiKeyId { get; set; }
    public int? ReadTimeMinutes { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? LastReviewedAt { get; set; }
    public int ReviewIntervalDays { get; set; } = 90;
    public DateTime? IndexedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User Owner { get; set; } = null!;
    public ApiKey? CreatedViaApiKey { get; set; }
    public ICollection<ArticleVersion> Versions { get; set; } = [];
    public ICollection<ArticleTag> ArticleTags { get; set; } = [];
    public ICollection<ArticleVote> Votes { get; set; } = [];
    public ICollection<ArticleComment> Comments { get; set; } = [];
    public ICollection<ArticleView> Views { get; set; } = [];
    public ICollection<ArticleAttachment> Attachments { get; set; } = [];
    public ArticleEmbedding? ArticleEmbedding { get; set; }
}
