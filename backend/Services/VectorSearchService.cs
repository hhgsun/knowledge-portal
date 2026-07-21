using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Pgvector;

namespace KnowledgePortal.Api.Services;

public record VectorSearchResult(string ArticleId, double Score, int ChunkIndex);

/// <summary>A single matched chunk with its stored text — used by RAG to build prompt context.</summary>
public record VectorChunkResult(string ArticleId, int ChunkIndex, double Score, string ChunkText);

/// <summary>
/// pgvector-backed semantic retrieval. Abstracted so RAG (and its tests) can depend on
/// the retrieval contract without the pgvector/Docker infrastructure.
/// </summary>
public interface IVectorSearchService
{
    /// <summary>Article-level retrieval: best matching chunk per article (list/semantic view).</summary>
    Task<List<VectorSearchResult>> SearchAsync(string queryText, int limit, CancellationToken ct = default, double? minScore = null);

    /// <summary>
    /// Chunk-level retrieval: up to <paramref name="maxPerArticle"/> best chunks per article,
    /// globally capped at <paramref name="maxChunks"/>, with the chunk text included. Lets RAG
    /// consider several passages of a long document instead of a single window.
    /// </summary>
    Task<List<VectorChunkResult>> SearchChunksAsync(string queryText, int maxChunks, CancellationToken ct = default, double? minScore = null, int maxPerArticle = 3);
}

public sealed class VectorSearchService(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IServiceScopeFactory scopeFactory,
    IConfiguration config) : IVectorSearchService
{
    private readonly double _minScore = config.GetValue("Ollama:MinSimilarityScore", 0.5);
    // HNSW candidate-list size at query time. The default (40) is far below the row limits we
    // ask for at 50k+ scale, so recall collapses without raising it. Kept >= rowLimit per query.
    private readonly int _efSearch = config.GetValue("Ollama:HnswEfSearch", 200);

    /// <param name="minScore">Overrides Ollama:MinSimilarityScore when set — RAG uses a lower
    /// threshold than list-style semantic search (the LLM judges relevance itself).</param>
    public async Task<List<VectorSearchResult>> SearchAsync(string queryText, int limit, CancellationToken ct = default, double? minScore = null)
    {
        var effectiveMinScore = minScore ?? _minScore;
        var queryVector = await EmbedQueryAsync(queryText, ct);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // pgvector cosine distance: 1 - cosine_similarity, so score = 1 - distance.
        // Single ORDER BY distance LIMIT N scan so the HNSW index can drive the query;
        // published-only at the source; best chunk per article picked in memory.
        var rowLimit = Math.Max(limit * 5, 50);
        var rows = await QueryWithEfSearchAsync(db, rowLimit, () => db.Database
            .SqlQueryRaw<PgvectorResult>(
                """
                SELECT e."ArticleId", e."ChunkIndex", e."Embedding" <=> {0}::vector AS "Distance"
                FROM article_embeddings e
                JOIN articles a ON a."Id" = e."ArticleId"
                WHERE a."Status" = 'published'
                ORDER BY e."Embedding" <=> {0}::vector
                LIMIT {1}
                """,
                queryVector.ToString(), rowLimit)
            .ToListAsync(ct), ct);

        return rows
            .GroupBy(r => r.ArticleId)
            .Select(g => g.OrderBy(r => r.Distance).First())
            .Where(r => 1.0 - r.Distance >= effectiveMinScore)
            .OrderBy(r => r.Distance)
            .Take(limit)
            .Select(r => new VectorSearchResult(r.ArticleId, 1.0 - r.Distance, r.ChunkIndex))
            .ToList();
    }

    public async Task<List<VectorChunkResult>> SearchChunksAsync(string queryText, int maxChunks, CancellationToken ct = default, double? minScore = null, int maxPerArticle = 3)
    {
        var effectiveMinScore = minScore ?? _minScore;
        var queryVector = await EmbedQueryAsync(queryText, ct);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Over-fetch: per-article capping + score filtering below can drop many candidates.
        var rowLimit = Math.Max(maxChunks * 5, 50);
        var rows = await QueryWithEfSearchAsync(db, rowLimit, () => db.Database
            .SqlQueryRaw<PgvectorChunkRow>(
                """
                SELECT e."ArticleId", e."ChunkIndex", e."Content", e."Embedding" <=> {0}::vector AS "Distance"
                FROM article_embeddings e
                JOIN articles a ON a."Id" = e."ArticleId"
                WHERE a."Status" = 'published'
                ORDER BY e."Embedding" <=> {0}::vector
                LIMIT {1}
                """,
                queryVector.ToString(), rowLimit)
            .ToListAsync(ct), ct);

        return rows
            .GroupBy(r => r.ArticleId)
            .SelectMany(g => g.OrderBy(r => r.Distance).Take(Math.Max(1, maxPerArticle)))
            .Where(r => 1.0 - r.Distance >= effectiveMinScore)
            .OrderBy(r => r.Distance)
            .Take(maxChunks)
            .Select(r => new VectorChunkResult(r.ArticleId, r.ChunkIndex, 1.0 - r.Distance, r.Content ?? ""))
            .ToList();
    }

    private async Task<Vector> EmbedQueryAsync(string queryText, CancellationToken ct)
    {
        var queryResults = await embeddingGenerator.GenerateAsync([queryText], cancellationToken: ct);
        return new Vector(queryResults[0].Vector.ToArray());
    }

    /// <summary>
    /// Runs a vector query with a per-transaction HNSW ef_search set high enough to actually
    /// surface <paramref name="rowLimit"/> candidates. SET LOCAL requires a transaction and the
    /// GUC value must be a literal (not a bind parameter); ef_search is a validated int, so the
    /// interpolation is injection-safe.
    /// </summary>
    private async Task<List<T>> QueryWithEfSearchAsync<T>(AppDbContext db, int rowLimit, Func<Task<List<T>>> runQuery, CancellationToken ct)
    {
        var efSearch = Math.Max(_efSearch, rowLimit);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // efSearch is a validated int (Math.Max of two ints), so the interpolation is injection-safe.
        // SET LOCAL cannot bind parameters for GUCs, hence ExecuteSqlRaw over ExecuteSql.
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync($"SET LOCAL hnsw.ef_search = {efSearch}", ct);
#pragma warning restore EF1002
        var rows = await runQuery();
        await tx.CommitAsync(ct);
        return rows;
    }

    private class PgvectorResult
    {
        public string ArticleId { get; set; } = null!;
        public int ChunkIndex { get; set; }
        public double Distance { get; set; }
    }

    private class PgvectorChunkRow
    {
        public string ArticleId { get; set; } = null!;
        public int ChunkIndex { get; set; }
        public string? Content { get; set; }
        public double Distance { get; set; }
    }
}
