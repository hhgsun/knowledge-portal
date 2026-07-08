using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Pgvector;

namespace KnowledgePortal.Api.Services;

public sealed class VectorSearchService(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<VectorSearchService> logger)
{
    private readonly double _minScore = config.GetValue("Ollama:MinSimilarityScore", 0.3);

    public record VectorSearchResult(string ArticleId, double Score);

    public async Task<List<VectorSearchResult>> SearchAsync(string queryText, int limit, CancellationToken ct = default)
    {
        var queryResults = await embeddingGenerator.GenerateAsync([queryText], cancellationToken: ct);
        var queryVector = new Vector(queryResults[0].Vector.ToArray());

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // pgvector cosine distance: 1 - cosine_similarity, so score = 1 - distance
        var results = await db.Database
            .SqlQueryRaw<PgvectorResult>(
                """
                SELECT "ArticleId", MIN("Embedding" <=> {0}::vector) AS "Distance"
                FROM article_embeddings
                GROUP BY "ArticleId"
                HAVING MIN("Embedding" <=> {0}::vector) <= {1}
                ORDER BY "Distance" ASC
                LIMIT {2}
                """,
                queryVector.ToString(), 1.0 - _minScore, limit)
            .ToListAsync(ct);

        return results
            .Select(r => new VectorSearchResult(r.ArticleId, 1.0 - r.Distance))
            .ToList();
    }

    private class PgvectorResult
    {
        public string ArticleId { get; set; } = null!;
        public double Distance { get; set; }
    }
}
