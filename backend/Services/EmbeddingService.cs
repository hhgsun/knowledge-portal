using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Pgvector;

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
        // Optimistic concurrency: snapshot the row version (xmin) BEFORE reading content.
        // Any concurrent edit bumps xmin, so the conditional IndexedAt claim below fails
        // and the article stays queued (IndexedAt=null) for the next poll.
        var xmin = await GetArticleXminAsync(article.Id, ct);
        if (xmin == null) return false; // article deleted meanwhile

        await db.Entry(article).ReloadAsync(ct);
        if (article.Status != "published") return false;

        var attachmentText = await AttachmentHelper.GetAttachmentTextAsync(db, config, article.Id, ct);
        var text = ContentExtractor.ExtractSearchableText(article.Title, article.Excerpt, article.Content, attachmentText);
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
                await TryClaimIndexedAsync(article.Id, xmin.Value, ct);
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
            db.ArticleEmbeddings.Add(new ArticleEmbedding
            {
                ArticleId = article.Id,
                ChunkIndex = i,
                Embedding = new Vector(vector),
                ModelName = _modelName,
                TextHash = i == 0 ? textHash : ContentExtractor.ComputeHash(chunks[i]),
                Dimensions = vector.Length,
            });
        }

        await db.SaveChangesAsync(ct);

        if (!await TryClaimIndexedAsync(article.Id, xmin.Value, ct))
            return false; // article changed while embedding — chunks get replaced on the retry

        logger.LogInformation("Embedded article {ArticleId} ({Chunks} chunks, {Dimensions} dims, model={Model})",
            article.Id, chunks.Count, embedResults[0].Vector.Length, _modelName);
        return true;
    }

    /// <summary>Reads the current xmin row version of an article (null if the row is gone).</summary>
    private async Task<long?> GetArticleXminAsync(string articleId, CancellationToken ct)
    {
        var rows = await db.Database
            .SqlQueryRaw<long>("""SELECT xmin::text::bigint AS "Value" FROM articles WHERE "Id" = {0}""", articleId)
            .ToListAsync(ct);
        return rows.Count > 0 ? rows[0] : null;
    }

    /// <summary>
    /// Marks the article as indexed only if it has not been modified since <paramref name="xmin"/>
    /// was captured. Returns false when a concurrent edit won — IndexedAt stays null so the
    /// background service re-embeds the fresh content on its next poll.
    /// </summary>
    private async Task<bool> TryClaimIndexedAsync(string articleId, long xmin, CancellationToken ct)
    {
        var claimed = await db.Database.ExecuteSqlRawAsync(
            """UPDATE articles SET "IndexedAt" = {0} WHERE "Id" = {1} AND xmin::text::bigint = {2}""",
            [DateTime.UtcNow, articleId, xmin], ct);

        if (claimed == 0)
            logger.LogInformation("Article {ArticleId} changed during embedding, deferring to next poll", articleId);
        return claimed > 0;
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
}
