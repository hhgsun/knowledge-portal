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
    private readonly ConcurrentDictionary<string, List<CachedChunk>> _cache = new();
    private readonly double _minScore = config.GetValue("Ollama:MinSimilarityScore", 0.3);
    private volatile bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public record VectorSearchResult(string ArticleId, double Score);
    private record CachedChunk(float[] Vector, double Norm);

    public async Task<List<VectorSearchResult>> SearchAsync(string queryText, int limit, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        if (_cache.IsEmpty) return [];

        var queryResults = await embeddingGenerator.GenerateAsync([queryText], cancellationToken: ct);
        var queryVector = queryResults[0].Vector.ToArray();
        var queryNorm = ComputeNorm(queryVector);
        if (queryNorm == 0) return [];

        var results = new List<VectorSearchResult>();
        foreach (var (articleId, chunks) in _cache)
        {
            // Best chunk score per article
            double bestScore = 0;
            foreach (var chunk in chunks)
            {
                var score = CosineSimilarity(queryVector, chunk.Vector, queryNorm, chunk.Norm);
                if (score > bestScore) bestScore = score;
            }
            if (bestScore >= _minScore)
                results.Add(new VectorSearchResult(articleId, bestScore));
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

    public void UpdateArticle(string articleId, List<(float[] Vector, double Norm)> chunks)
    {
        var cached = chunks.Select(c => new CachedChunk(c.Vector, c.Norm)).ToList();
        _cache[articleId] = cached;
    }

    public void RemoveArticle(string articleId)
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
                .Select(e => new { e.ArticleId, e.ChunkIndex, e.Embedding, e.EmbeddingNorm })
                .ToListAsync(ct);

            _cache.Clear();
            foreach (var group in embeddings.GroupBy(e => e.ArticleId))
            {
                var chunks = group
                    .OrderBy(e => e.ChunkIndex)
                    .Select(e => new CachedChunk(EmbeddingService.DeserializeEmbedding(e.Embedding), e.EmbeddingNorm))
                    .ToList();
                _cache[group.Key] = chunks;
            }
            _initialized = true;
            logger.LogInformation("Vector search cache initialized with {Count} articles ({Total} chunks)",
                _cache.Count, embeddings.Count);
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
