namespace KnowledgePortal.Api.Models.Entities;

/// <summary>
/// A structure-bounded context unit for parent-document retrieval. Child embeddings point here;
/// the parent itself is deliberately not embedded, avoiding duplicate vectors and retrieval noise.
/// </summary>
public class ArticleChunkParent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..21];
    public string ArticleId { get; set; } = null!;
    public int ParentIndex { get; set; }
    public string SourceType { get; set; } = "article";
    public string? AttachmentId { get; set; }
    public string? SourceName { get; set; }
    public string? SourceLocation { get; set; }
    public string Content { get; set; } = null!;
    public string TextHash { get; set; } = null!;
    public int WordCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Article Article { get; set; } = null!;
    public ArticleAttachment? Attachment { get; set; }
    public ICollection<ArticleEmbedding> Children { get; set; } = [];
}
