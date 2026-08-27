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
    private readonly int _parentChunkTargetWords = Math.Clamp(
        config.GetValue("Ollama:ParentChunkTargetWords", KnowledgeChunker.DefaultParentTargetWords), 300, 3000);
    private readonly int _childChunkTargetWords = Math.Clamp(
        config.GetValue("Ollama:ChildChunkTargetWords", KnowledgeChunker.DefaultChildTargetWords), 80, 800);
    private readonly int _childChunkOverlapWords = Math.Clamp(
        config.GetValue("Ollama:ChildChunkOverlapWords", KnowledgeChunker.DefaultChildOverlapWords), 0,
        Math.Clamp(config.GetValue("Ollama:ChildChunkTargetWords", KnowledgeChunker.DefaultChildTargetWords), 80, 800) - 1);
    private readonly string _chunkingVersion = config["Ollama:ChunkingVersion"] ?? "hierarchical-parent-child-v2";
    private readonly string _indexProfile = ComputeIndexProfile(config);

    internal static string ComputeIndexProfile(IConfiguration source)
    {
        var parentTarget = Math.Clamp(source.GetValue("Ollama:ParentChunkTargetWords",
            KnowledgeChunker.DefaultParentTargetWords), 300, 3000);
        var childTarget = Math.Clamp(source.GetValue("Ollama:ChildChunkTargetWords",
            KnowledgeChunker.DefaultChildTargetWords), 80, 800);
        var childOverlap = Math.Clamp(source.GetValue("Ollama:ChildChunkOverlapWords",
            KnowledgeChunker.DefaultChildOverlapWords), 0, childTarget - 1);
        return ContentExtractor.ComputeHash(string.Join('|',
            source["Ollama:EmbeddingModel"] ?? "bge-m3",
            source.GetValue("Ollama:EmbeddingDimensions", 1024),
            source["Ollama:ChunkingVersion"] ?? "hierarchical-parent-child-v2",
            parentTarget,
            childTarget,
            childOverlap))[..16];
    }

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
        var contentHash = ContentExtractor.ComputeHash(string.Join('|', chunks.Select(c =>
            $"{c.SourceType}:{c.AttachmentId}:{c.SourceName}:{c.SourceLocation}:" +
            $"{ContentExtractor.ComputeHash(c.Content)}:{ContentExtractor.ComputeHash(c.Parent.Content)}")));
        var textHash = $"{_indexProfile}:{contentHash}";

        // Check if already up-to-date (compare hash of first chunk)
        var existingChunks = await db.ArticleEmbeddings
            .Where(e => e.ArticleId == article.Id)
            .OrderBy(e => e.ChunkIndex)
            .ToListAsync(ct);

        if (existingChunks.Count > 0 && existingChunks.All(x => x.ParentChunkId != null) &&
            existingChunks[0].TextHash == textHash && existingChunks[0].ModelName == _modelName)
        {
            if (article.IndexedAt == null)
                await TryClaimIndexedAsync(article.Id, xmin.Value, ct);
            return false;
        }

        var embedResults = await GenerateInBatchesAsync(chunks.Select(c => c.Content).ToList(), ct);

        if (embedResults.Count != chunks.Count)
            throw new InvalidOperationException(
                $"Embedding model '{_modelName}' returned {embedResults.Count} vectors for {chunks.Count} child chunks.");

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

        var existingParents = await db.ArticleChunkParents
            .Where(p => p.ArticleId == article.Id).ToListAsync(ct);
        if (existingParents.Count > 0)
            db.ArticleChunkParents.RemoveRange(existingParents);

        var selectedParents = chunks.Select(c => c.Parent).DistinctBy(p => p.Id).ToList();
        for (var parentIndex = 0; parentIndex < selectedParents.Count; parentIndex++)
        {
            var parent = selectedParents[parentIndex];
            db.ArticleChunkParents.Add(new ArticleChunkParent
            {
                Id = parent.Id,
                ArticleId = article.Id,
                ParentIndex = parentIndex,
                SourceType = parent.SourceType,
                AttachmentId = parent.AttachmentId,
                SourceName = parent.SourceName,
                SourceLocation = parent.SourceLocation,
                Content = parent.Content,
                TextHash = ContentExtractor.ComputeHash(parent.Content),
                WordCount = parent.Content.Split((char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries).Length
            });
        }

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
                ParentChunkId = chunks[i].Parent.Id,
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

        logger.LogInformation(
            "Embedded article {ArticleId} ({Parents} parents, {Children} searchable children, {Dimensions} dims, model={Model})",
            article.Id, selectedParents.Count, chunks.Count, embedResults[0].Vector.Length, _modelName);
        return true;
    }

    private sealed record ParentSeed(string Id, string Content, string SourceType,
        string? AttachmentId, string? SourceName, string? SourceLocation);
    private sealed record ChunkSeed(string Content, string SourceType, string? AttachmentId,
        string? SourceName, string? SourceLocation, ParentSeed Parent);

    private async Task<List<List<ChunkSeed>>> BuildChunkSourcesAsync(Article article, CancellationToken ct)
    {
        var result = new List<List<ChunkSeed>>();
        AddSource(result, FlattenHierarchy(KnowledgeChunker.BuildMarkdownHierarchy(article.Title,
                article.Excerpt, article.Content, _parentChunkTargetWords, _childChunkTargetWords,
                _childChunkOverlapWords), "article", null, article.Title),
            article.Id, "article");

        var attachments = await db.ArticleAttachments
            .Where(a => a.ArticleId == article.Id).OrderBy(a => a.CreatedAt).ToListAsync(ct);
        foreach (var attachment in attachments)
        {
            var extraction = AttachmentHelper.GetOrExtract(config, attachment);
            if (extraction.Status == "failed")
            {
                logger.LogWarning("Attachment extraction failed: {AttachmentId}: {Error}",
                    attachment.Id, extraction.Error);
                continue;
            }

            var attachmentChunks = extraction.Segments.SelectMany(segment => FlattenHierarchy(
                KnowledgeChunker.BuildTextHierarchy(segment.Text, segment.Location,
                    _parentChunkTargetWords, _childChunkTargetWords, _childChunkOverlapWords),
                "attachment", attachment.Id, attachment.FileName));
            if (!extraction.Segments.Any() && !string.IsNullOrWhiteSpace(extraction.Text))
            {
                attachmentChunks = FlattenHierarchy(KnowledgeChunker.BuildTextHierarchy(extraction.Text,
                    "file", _parentChunkTargetWords, _childChunkTargetWords, _childChunkOverlapWords),
                    "attachment", attachment.Id, attachment.FileName);
            }
            AddSource(result, attachmentChunks, article.Id, attachment.FileName);
        }
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
        return result;
    }

    private static IEnumerable<ChunkSeed> FlattenHierarchy(
        IEnumerable<KnowledgeParentChunk> hierarchy, string sourceType, string? attachmentId,
        string? sourceName)
    {
        foreach (var parent in hierarchy)
        {
            var seed = new ParentSeed(Guid.NewGuid().ToString("N")[..21], parent.Content,
                sourceType, attachmentId, sourceName, parent.Location);
            foreach (var child in parent.Children)
                yield return new(child.Content, sourceType, attachmentId, sourceName,
                    child.Location, seed);
        }
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
            .SqlQueryRaw<long>("""SELECT xmin::text::bigint AS "Value" FROM articles WHERE id = {0}""", articleId)
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
            """UPDATE articles SET indexed_at = {0} WHERE id = {1} AND xmin::text::bigint = {2}""",
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
        var parents = await db.ArticleChunkParents
            .Where(p => p.ArticleId == articleId)
            .ToListAsync(ct);
        if (existing.Count > 0)
            db.ArticleEmbeddings.RemoveRange(existing);
        if (parents.Count > 0)
            db.ArticleChunkParents.RemoveRange(parents);
        if (existing.Count > 0 || parents.Count > 0)
            await db.SaveChangesAsync(ct);
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
        var removed = await db.ArticleEmbeddings
            .Where(e => !db.Articles.Any(a => a.Id == e.ArticleId && a.Status == "published"))
            .ExecuteDeleteAsync(ct);
        await db.ArticleChunkParents
            .Where(p => !db.Articles.Any(a => a.Id == p.ArticleId && a.Status == "published"))
            .ExecuteDeleteAsync(ct);
        return removed;
    }

    public async Task<int> InvalidateStaleModelAsync(CancellationToken ct = default)
    {
        var staleArticleIds = await db.ArticleEmbeddings
            .Where(e => e.ChunkIndex == 0 &&
                (e.ModelName != _modelName || !e.TextHash.StartsWith(_indexProfile + ":")))
            .Select(e => e.ArticleId)
            .Distinct()
            .ToListAsync(ct);

        if (staleArticleIds.Count == 0) return 0;

        // Keep the previous rows until each article is replaced atomically. Vector retrieval is
        // constrained to the active model, so incompatible vectors are never scored; a chunking-
        // only transition can continue serving the previous structure during the rolling rebuild.
        if (db.Database.IsRelational())
        {
            await db.Articles
                .Where(a => staleArticleIds.Contains(a.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.IndexedAt, (DateTime?)null), ct);
        }
        else
        {
            var articles = await db.Articles.Where(a => staleArticleIds.Contains(a.Id)).ToListAsync(ct);
            foreach (var article in articles) article.IndexedAt = null;
            await db.SaveChangesAsync(ct);
        }

        logger.LogWarning(
            "Queued rolling semantic reindex for {Count} articles due to index profile change (model={Model}, chunkingVersion={ChunkingVersion}, profile={IndexProfile})",
            staleArticleIds.Count, _modelName, _chunkingVersion, _indexProfile);
        return staleArticleIds.Count;
    }

    public async Task<bool> IsOllamaAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var results = await embeddingGenerator.GenerateAsync(["test"], cancellationToken: ct);
            var result = results.FirstOrDefault();
            if (result?.Vector.Length == _expectedDimensions) return true;

            logger.LogWarning(
                "Ollama health probe embedding dimension mismatch: model {Model} returned {ActualDimensions} dims, expected {ExpectedDimensions}",
                _modelName, result?.Vector.Length, _expectedDimensions);
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Backwards-compatible default text chunking entry point used by lexical fallback and tests.
    /// Production embedding uses the configured limits and Markdown-aware path above.
    /// </summary>
    internal static List<string> ChunkText(string text)
        => KnowledgeChunker.ChunkText(text, KnowledgeChunker.DefaultTargetWords,
            KnowledgeChunker.DefaultOverlapWords).Select(x => x.Content).ToList();
}
