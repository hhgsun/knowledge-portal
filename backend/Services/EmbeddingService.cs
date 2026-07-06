using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace KnowledgePortal.Api.Services;

public class EmbeddingService(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    AppDbContext db,
    IConfiguration config,
    ILogger<EmbeddingService> logger)
{
    private readonly string _modelName = config["Ollama:EmbeddingModel"] ?? "nomic-embed-text";
    private const int ChunkWordLimit = 500;
    private const int ChunkOverlap = 50;

    public async Task<bool> EmbedArticleAsync(Article article, CancellationToken ct = default)
    {
        var text = ContentExtractor.ExtractSearchableText(article.Title, article.Excerpt, article.Content);
        if (string.IsNullOrWhiteSpace(text))
        {
            logger.LogWarning("Article {ArticleId} has no extractable text, skipping embedding", article.Id);
            return false;
        }

        var textHash = ContentExtractor.ComputeHash(text);

        // Check if already up-to-date (compare hash of first chunk)
        var existingChunks = await db.ArticleEmbeddings
            .Where(e => e.ArticleId == article.Id)
            .OrderBy(e => e.ChunkIndex)
            .ToListAsync(ct);

        if (existingChunks.Count > 0 && existingChunks[0].TextHash == textHash && existingChunks[0].ModelName == _modelName)
        {
            if (article.IndexedAt == null)
            {
                article.IndexedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            return false;
        }

        // Chunk the text
        var chunks = ChunkText(text);

        // Generate embeddings for all chunks
        var embedResults = await embeddingGenerator.GenerateAsync(chunks, cancellationToken: ct);

        // Remove old embeddings
        if (existingChunks.Count > 0)
            db.ArticleEmbeddings.RemoveRange(existingChunks);

        // Insert new chunk embeddings
        for (int i = 0; i < chunks.Count; i++)
        {
            var vector = embedResults[i].Vector.ToArray();
            var norm = VectorMath.ComputeNorm(vector);
            db.ArticleEmbeddings.Add(new ArticleEmbedding
            {
                ArticleId = article.Id,
                ChunkIndex = i,
                Embedding = SerializeEmbedding(vector),
                EmbeddingNorm = norm,
                ModelName = _modelName,
                TextHash = i == 0 ? textHash : ContentExtractor.ComputeHash(chunks[i]),
                Dimensions = vector.Length,
            });
        }

        article.IndexedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Embedded article {ArticleId} ({Chunks} chunks, {Dimensions} dims, model={Model})",
            article.Id, chunks.Count, embedResults[0].Vector.Length, _modelName);
        return true;
    }

    /// <summary>
    /// Returns all chunk embeddings for an article (for cache update).
    /// </summary>
    public async Task<List<ArticleEmbedding>> GetArticleEmbeddingsAsync(string articleId, CancellationToken ct = default)
    {
        return await db.ArticleEmbeddings
            .Where(e => e.ArticleId == articleId)
            .OrderBy(e => e.ChunkIndex)
            .ToListAsync(ct);
    }

    public async Task RemoveEmbeddingAsync(string articleId, CancellationToken ct = default)
    {
        var existing = await db.ArticleEmbeddings
            .Where(e => e.ArticleId == articleId)
            .ToListAsync(ct);
        if (existing.Count > 0)
        {
            db.ArticleEmbeddings.RemoveRange(existing);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<int> InvalidateStaleModelAsync(CancellationToken ct = default)
    {
        var staleArticleIds = await db.ArticleEmbeddings
            .Where(e => e.ModelName != _modelName)
            .Select(e => e.ArticleId)
            .Distinct()
            .ToListAsync(ct);

        if (staleArticleIds.Count == 0) return 0;

        await db.ArticleEmbeddings
            .Where(e => staleArticleIds.Contains(e.ArticleId))
            .ExecuteDeleteAsync(ct);

        await db.Articles
            .Where(a => staleArticleIds.Contains(a.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IndexedAt, (DateTime?)null), ct);

        logger.LogWarning("Invalidated {Count} articles' embeddings due to model change (new model: {Model})",
            staleArticleIds.Count, _modelName);
        return staleArticleIds.Count;
    }

    public async Task<bool> IsOllamaAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            await embeddingGenerator.GenerateAsync(["test"], cancellationToken: ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Splits text into chunks of approximately ChunkWordLimit words with overlap.
    /// Short texts (≤ ChunkWordLimit) return a single chunk.
    /// </summary>
    internal static List<string> ChunkText(string text)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= ChunkWordLimit)
            return [text];

        var chunks = new List<string>();
        var step = ChunkWordLimit - ChunkOverlap;
        for (int i = 0; i < words.Length; i += step)
        {
            var end = Math.Min(i + ChunkWordLimit, words.Length);
            chunks.Add(string.Join(' ', words[i..end]));
            if (end >= words.Length) break;
        }

        return chunks;
    }

    public static byte[] SerializeEmbedding(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    public static float[] DeserializeEmbedding(byte[] bytes)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }
}
