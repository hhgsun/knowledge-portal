namespace KnowledgePortal.Api.Models.Entities;

public class ArticleEmbedding
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..21];
    public string ArticleId { get; set; } = null!;
    public byte[] Embedding { get; set; } = null!;
    public double EmbeddingNorm { get; set; }
    public string ModelName { get; set; } = null!;
    public string TextHash { get; set; } = null!;
    public int Dimensions { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Article Article { get; set; } = null!;
}
