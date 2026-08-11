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
    private readonly string _modelName = config["Ollama:EmbeddingModel"] ?? "bge-m3";
    private readonly int _expectedDimensions = config.GetValue("Ollama:EmbeddingDimensions", 1024);

    // Chunks sent to the embedding model per request. An article's chunks used to go in a single
    // call, so a document with enough attachment text (20 attachments x 50k chars is ~330 chunks)
    // exceeded Ollama:TimeoutSeconds and could never finish — the article stayed queued and
    // retried forever. Batching bounds each request instead of letting it scale with the document.
    private readonly int _chunkBatchSize = config.GetValue("Ollama:ChunkBatchSize", 16);

    // Hard ceiling on chunks stored per article; 0 disables it. This is a retrieval-quality knob,
    // not a cost one: the vector scan fetches a fixed window of CHUNKS before collapsing them to
    // articles, so one document with hundreds of near-identical chunks can fill that window on its
    // own and push every other article out of the results (measured in
    // scripts/hnsw_recall_benchmark.sql section 3). The cost is real, though — text past the cap
    // is not semantically searchable, only full-text searchable — so every truncation is logged.
    // Distinct from Ollama:RagMaxChunksPerArticle, which caps chunks per article at query time.
    private readonly int _maxChunksPerSource = config.GetValue("Ollama:MaxIndexChunksPerSource", 100);
    private readonly int _maxTotalChunksPerArticle = config.GetValue("Ollama:MaxTotalChunksPerArticle", 500);

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

        var sources = await BuildChunkSourcesAsync(article, ct);
        var chunks = RoundRobin(sources, _maxTotalChunksPerArticle);
        if (chunks.Count == 0)
        {
            logger.LogWarning("Article {ArticleId} has no extractable text, skipping embedding", article.Id);
            return false;
        }

        // Includes provenance so replacing/renaming an attachment invalidates the embedding set.
        var textHash = ContentExtractor.ComputeHash(string.Join('|', chunks.Select(c =>
            $"{c.SourceType}:{c.AttachmentId}:{c.SourceName}:{ContentExtractor.ComputeHash(c.Content)}")));

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

        var embedResults = await GenerateInBatchesAsync(chunks.Select(c => c.Content).ToList(), ct);

        // Guard: a model/column dimension mismatch would otherwise only surface as an opaque
        // pgvector INSERT error. Fail with an actionable message instead.
        if (embedResults.Any(r => r.Vector.Length != _expectedDimensions))
            throw new InvalidOperationException(
                $"Embedding dimension mismatch: model '{_modelName}' returned {embedResults[0].Vector.Length} dims, " +
                $"expected {_expectedDimensions} (Ollama:EmbeddingDimensions / vector({_expectedDimensions}) column). " +
                "Fix the model/config or migrate the article_embeddings column.");

        // Persist the chunks and claim IndexedAt ATOMICALLY. Embedding generation above is a slow
        // network call; the article can be unpublished meanwhile (ArticlesController deletes its
        // embeddings and bumps xmin). Committing the chunks first and only then checking xmin —
        // as this used to — leaves orphan embeddings for a now-draft article that the background
        // service will never revisit (its query filters Status='published'). Instead, stage the
        // chunk changes inside a transaction and let the xmin-guarded claim gate the commit: if the
        // article changed (xmin bumped), the claim matches 0 rows and we roll the whole thing back,
        // so no chunk row is ever committed for a no-longer-published article.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (existingChunks.Count > 0)
            db.ArticleEmbeddings.RemoveRange(existingChunks);

        for (int i = 0; i < chunks.Count; i++)
        {
            var vector = embedResults[i].Vector.ToArray();
            db.ArticleEmbeddings.Add(new ArticleEmbedding
            {
                ArticleId = article.Id,
                ChunkIndex = i,
                SourceType = chunks[i].SourceType,
                AttachmentId = chunks[i].AttachmentId,
                SourceName = chunks[i].SourceName,
                SourceLocation = chunks[i].SourceLocation,
                Embedding = new Vector(vector),
                ModelName = _modelName,
                TextHash = i == 0 ? textHash : ContentExtractor.ComputeHash(chunks[i].Content),
                Content = chunks[i].Content,
                Dimensions = vector.Length,
            });
        }

        await db.SaveChangesAsync(ct); // staged in the transaction — not visible until commit

        if (!await TryClaimIndexedAsync(article.Id, xmin.Value, ct))
        {
            // Article changed mid-embed (e.g. unpublished). Undo the staged chunks so none leak;
            // if it is still published, it stays queued (IndexedAt=null) and re-embeds next poll.
            await tx.RollbackAsync(ct);
            return false;
        }

        await tx.CommitAsync(ct);

        logger.LogInformation("Embedded article {ArticleId} ({Chunks} chunks, {Dimensions} dims, model={Model})",
            article.Id, chunks.Count, embedResults[0].Vector.Length, _modelName);
        return true;
    }

    private sealed record ChunkSeed(string Content, string SourceType, string? AttachmentId,
        string? SourceName, string? SourceLocation);

    private async Task<List<List<ChunkSeed>>> BuildChunkSourcesAsync(Article article, CancellationToken ct)
    {
        var result = new List<List<ChunkSeed>>();
        var articleText = ContentExtractor.ExtractSearchableText(article.Title, article.Excerpt, article.Content, "");
        AddSource(result, ChunkText(articleText).Select((text, i) =>
            new ChunkSeed(text, "article", null, article.Title, $"chunk:{i}")), article.Id, "article");

        var attachments = await db.ArticleAttachments
            .Where(a => a.ArticleId == article.Id).OrderBy(a => a.CreatedAt).ToListAsync(ct);
        foreach (var attachment in attachments)
        {
            try
            {
                var path = AttachmentHelper.GetFilePath(config, article.Id, attachment.StoredFileName);
                var text = AttachmentTextExtractor.ExtractText(path, Path.GetExtension(attachment.FileName));
                attachment.ExtractionStatus = string.IsNullOrWhiteSpace(text) ? "no_text" : "completed";
                attachment.ExtractionError = null;
                attachment.ExtractedAt = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(text))
                    AddSource(result, ChunkText(text).Select((chunk, i) =>
                        new ChunkSeed(chunk, "attachment", attachment.Id, attachment.FileName, $"chunk:{i}")),
                        article.Id, attachment.FileName);
            }
            catch (Exception ex)
            {
                attachment.ExtractionStatus = "failed";
                attachment.ExtractionError = ex.Message[..Math.Min(2000, ex.Message.Length)];
                attachment.ExtractedAt = DateTime.UtcNow;
                logger.LogWarning(ex, "Attachment extraction failed: {AttachmentId}", attachment.Id);
            }
        }
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
        return result;
    }

    private void AddSource(List<List<ChunkSeed>> target, IEnumerable<ChunkSeed> chunks,
        string articleId, string sourceName)
    {
        var all = chunks.ToList();
        var selected = _maxChunksPerSource > 0 ? all.Take(_maxChunksPerSource).ToList() : all;
        if (selected.Count > 0) target.Add(selected);
        if (selected.Count < all.Count)
            logger.LogWarning("Article {ArticleId} source {Source} truncated from {Total} to {Cap} semantic chunks",
                articleId, sourceName, all.Count, selected.Count);
    }

    /// <summary>Fairly interleaves sources so one huge attachment cannot consume the whole cap.</summary>
    private static List<ChunkSeed> RoundRobin(List<List<ChunkSeed>> sources, int cap)
    {
        var output = new List<ChunkSeed>();
        var effectiveCap = cap <= 0 ? int.MaxValue : cap;
        for (var i = 0; output.Count < effectiveCap; i++)
        {
            var added = false;
            foreach (var source in sources)
            {
                if (i >= source.Count) continue;
                output.Add(source[i]);
                added = true;
                if (output.Count >= effectiveCap) break;
            }
            if (!added) break;
        }
        return output;
    }

    /// <summary>
    /// Embeds the chunks in fixed-size requests, preserving order. One request per article made
    /// its duration scale with the document, so the longest documents — the ones that most need
    /// indexing — were the ones that timed out.
    /// </summary>
    internal async Task<List<Embedding<float>>> GenerateInBatchesAsync(List<string> chunks, CancellationToken ct)
    {
        var batchSize = Math.Max(1, _chunkBatchSize);
        var results = new List<Embedding<float>>(chunks.Count);

        for (var offset = 0; offset < chunks.Count; offset += batchSize)
        {
            var batch = chunks.GetRange(offset, Math.Min(batchSize, chunks.Count - offset));
            results.AddRange(await embeddingGenerator.GenerateAsync(batch, cancellationToken: ct));
        }

        return results;
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

    /// <summary>
    /// Deletes embeddings whose article is missing or no longer published, enforcing the
    /// "published-only" invariant. The transactional xmin guard in <see cref="EmbedArticleAsync"/>
    /// prevents the common unpublish-during-embedding race, but a narrow residual window (and rows
    /// from older builds) can still leave orphans; this is the periodic safety net. Returns the
    /// number of chunk rows removed. See scripts/cleanup_orphan_embeddings.sql for the manual form.
    /// </summary>
    public async Task<int> CleanupOrphanEmbeddingsAsync(CancellationToken ct = default)
    {
        return await db.ArticleEmbeddings
            .Where(e => !db.Articles.Any(a => a.Id == e.ArticleId && a.Status == "published"))
            .ExecuteDeleteAsync(ct);
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
