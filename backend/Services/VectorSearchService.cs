using System.Collections.Concurrent;
using System.Numerics.Tensors;
using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace KnowledgePortal.Api.Services;

public sealed class VectorSearchService(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<VectorSearchService> logger)
{
    private readonly ConcurrentDictionary<string, CachedEmbedding> _cache = new();
    private readonly double _minScore = config.GetValue("Ollama:MinSimilarityScore", 0.3);
    private volatile bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public record VectorSearchResult(string ArticleId, double Score);
    private record CachedEmbedding(float[] Vector, double Norm);

    public async Task<List<VectorSearchResult>> SearchAsync(string queryText, int limit, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        if (_cache.IsEmpty) return [];

        var queryResults = await embeddingGenerator.GenerateAsync([queryText], cancellationToken: ct);
        var queryVector = queryResults[0].Vector.ToArray();
        var queryNorm = ComputeNorm(queryVector);
        if (queryNorm == 0) return [];

        var results = new List<VectorSearchResult>(_cache.Count);
        foreach (var (articleId, cached) in _cache)
        {
            var score = CosineSimilarity(queryVector, cached.Vector, queryNorm, cached.Norm);
            if (score >= _minScore)
                results.Add(new VectorSearchResult(articleId, score));
        }

        results.Sort((a, b) => b.Score.CompareTo(a.Score));
        return results.Count > limit ? results[..limit] : results;
    }

    public void InvalidateCache()
    {
        _initialized = false;
        _cache.Clear();
        logger.LogInformation("Vector search cache invalidated");
    }

    public void UpdateSingle(string articleId, float[] vector, double norm)
    {
        _cache[articleId] = new CachedEmbedding(vector, norm);
    }

    public void RemoveSingle(string articleId)
    {
        _cache.TryRemove(articleId, out _);
    }

    public int CacheSize => _cache.Count;

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;
        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var embeddings = await db.ArticleEmbeddings
                .Select(e => new { e.ArticleId, e.Embedding, e.EmbeddingNorm })
                .ToListAsync(ct);

            _cache.Clear();
            foreach (var e in embeddings)
            {
                var vector = EmbeddingService.DeserializeEmbedding(e.Embedding);
                _cache[e.ArticleId] = new CachedEmbedding(vector, e.EmbeddingNorm);
            }
            _initialized = true;
            logger.LogInformation("Vector search cache initialized with {Count} embeddings", _cache.Count);
        }
        finally { _initLock.Release(); }
    }

    private static double CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b, double normA, double normB)
    {
        if (normA == 0 || normB == 0) return 0;
        var dot = TensorPrimitives.Dot(a, b);
        return dot / (normA * normB);
    }

    private static double ComputeNorm(float[] vector)
    {
        double sum = 0;
        for (int i = 0; i < vector.Length; i++)
            sum += (double)vector[i] * vector[i];
        return Math.Sqrt(sum);
    }
}
