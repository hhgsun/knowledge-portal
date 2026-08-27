using Pgvector;

namespace KnowledgePortal.Api.Models.Entities;

public class ArticleEmbedding
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..21];
    public string ArticleId { get; set; } = null!;
    public int ChunkIndex { get; set; }
    public string SourceType { get; set; } = "article";
    public string? AttachmentId { get; set; }
    public string? SourceName { get; set; }
    public string? SourceLocation { get; set; }
    public string? ParentChunkId { get; set; }
    public Vector Embedding { get; set; } = null!;
    public string ModelName { get; set; } = null!;
    public string TextHash { get; set; } = null!;
    /// <summary>
    /// The exact chunk text that was embedded. Persisted so RAG can build its prompt context
    /// directly from the DB instead of re-extracting attachments and re-chunking on every query.
    /// </summary>
    public string? Content { get; set; }
    public int Dimensions { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Article Article { get; set; } = null!;
    public ArticleAttachment? Attachment { get; set; }
    public ArticleChunkParent? ParentChunk { get; set; }
}
