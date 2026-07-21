using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Pgvector;

namespace KnowledgePortal.Api.Services;

public record VectorSearchResult(string ArticleId, double Score, int ChunkIndex);

/// <summary>
/// pgvector-backed semantic retrieval. Abstracted so RAG (and its tests) can depend on
/// the retrieval contract without the pgvector/Docker infrastructure.
/// </summary>
public interface IVectorSearchService
{
    Task<List<VectorSearchResult>> SearchAsync(string queryText, int limit, CancellationToken ct = default, double? minScore = null);
}

public sealed class VectorSearchService(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<VectorSearchService> logger) : IVectorSearchService
{
    private readonly double _minScore = config.GetValue("Ollama:MinSimilarityScore", 0.5);

    /// <param name="minScore">Overrides Ollama:MinSimilarityScore when set — RAG uses a lower
    /// threshold than list-style semantic search (the LLM judges relevance itself).</param>
    public async Task<List<VectorSearchResult>> SearchAsync(string queryText, int limit, CancellationToken ct = default, double? minScore = null)
    {
        var effectiveMinScore = minScore ?? _minScore;
        var queryResults = await embeddingGenerator.GenerateAsync([queryText], cancellationToken: ct);
        var queryVector = new Vector(queryResults[0].Vector.ToArray());

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // pgvector cosine distance: 1 - cosine_similarity, so score = 1 - distance.
        // Single ORDER BY distance LIMIT N scan so the HNSW index can drive the query;
        // published-only at the source; best chunk per article picked in memory.
        var rowLimit = Math.Max(limit * 5, 50);
        var rows = await db.Database
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
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.ArticleId)
            .Select(g => g.OrderBy(r => r.Distance).First())
            .Where(r => 1.0 - r.Distance >= effectiveMinScore)
            .OrderBy(r => r.Distance)
            .Take(limit)
            .Select(r => new VectorSearchResult(r.ArticleId, 1.0 - r.Distance, r.ChunkIndex))
            .ToList();
    }

    private class PgvectorResult
    {
        public string ArticleId { get; set; } = null!;
        public int ChunkIndex { get; set; }
        public double Distance { get; set; }
    }
}
