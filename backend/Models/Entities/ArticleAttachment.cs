namespace KnowledgePortal.Api.Models.Entities;

public class ArticleAttachment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..21];
    public string ArticleId { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public string StoredFileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = null!;
    public string ExtractionStatus { get; set; } = "pending";
    public string? ExtractionError { get; set; }
    public DateTime? ExtractedAt { get; set; }
    public string UploadedById { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Article Article { get; set; } = null!;
    public User UploadedBy { get; set; } = null!;
}
