using KnowledgePortal.Api.Data;
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

    public async Task<bool> EmbedArticleAsync(Article article, CancellationToken ct = default)
    {
        var text = ContentExtractor.ExtractSearchableText(article.Title, article.Excerpt, article.Content);
        if (string.IsNullOrWhiteSpace(text))
        {
            logger.LogWarning("Article {ArticleId} has no extractable text, skipping embedding", article.Id);
            return false;
        }

        var textHash = ContentExtractor.ComputeHash(text);

        var existing = await db.ArticleEmbeddings
            .FirstOrDefaultAsync(e => e.ArticleId == article.Id, ct);

        if (existing != null && existing.TextHash == textHash && existing.ModelName == _modelName)
        {
            if (article.IndexedAt == null)
            {
                article.IndexedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            return false;
        }

        var results = await embeddingGenerator.GenerateAsync([text], cancellationToken: ct);
        var vector = results[0].Vector.ToArray();
        var norm = ComputeNorm(vector);
        var embeddingBytes = SerializeEmbedding(vector);

        if (existing != null)
        {
            existing.Embedding = embeddingBytes;
            existing.EmbeddingNorm = norm;
            existing.ModelName = _modelName;
            existing.TextHash = textHash;
            existing.Dimensions = vector.Length;
            existing.CreatedAt = DateTime.UtcNow;
        }
        else
        {
            db.ArticleEmbeddings.Add(new ArticleEmbedding
            {
                ArticleId = article.Id,
                Embedding = embeddingBytes,
                EmbeddingNorm = norm,
                ModelName = _modelName,
                TextHash = textHash,
                Dimensions = vector.Length,
            });
        }

        article.IndexedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Embedded article {ArticleId} ({Dimensions} dims, model={Model})",
            article.Id, vector.Length, _modelName);
        return true;
    }

    public async Task RemoveEmbeddingAsync(string articleId, CancellationToken ct = default)
    {
        var existing = await db.ArticleEmbeddings
            .FirstOrDefaultAsync(e => e.ArticleId == articleId, ct);
        if (existing != null)
        {
            db.ArticleEmbeddings.Remove(existing);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<int> InvalidateStaleModelAsync(CancellationToken ct = default)
    {
        var staleArticleIds = await db.ArticleEmbeddings
            .Where(e => e.ModelName != _modelName)
            .Select(e => e.ArticleId)
            .ToListAsync(ct);

        if (staleArticleIds.Count == 0) return 0;

        await db.ArticleEmbeddings
            .Where(e => staleArticleIds.Contains(e.ArticleId))
            .ExecuteDeleteAsync(ct);

        await db.Articles
            .Where(a => staleArticleIds.Contains(a.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IndexedAt, (DateTime?)null), ct);

        logger.LogWarning("Invalidated {Count} embeddings due to model change (new model: {Model})",
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

    private static double ComputeNorm(float[] vector)
    {
        double sum = 0;
        for (int i = 0; i < vector.Length; i++)
            sum += (double)vector[i] * vector[i];
        return Math.Sqrt(sum);
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
