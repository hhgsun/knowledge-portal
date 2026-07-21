using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgePortal.Api.Tests.Integration;

/// <summary>
/// Deterministic, Docker-free replacement for the pgvector-backed VectorSearchService.
/// Ranks published articles by query-token overlap over title + plain-text content, so
/// semantic / hybrid / RAG endpoints return sensible results against the InMemory database
/// without any vector index. Mirrors the real minScore thresholding.
/// </summary>
public sealed class FakeVectorSearchService(IServiceScopeFactory scopeFactory) : IVectorSearchService
{
    public async Task<List<VectorSearchResult>> SearchAsync(string queryText, int limit,
        CancellationToken ct = default, double? minScore = null)
    {
        var threshold = minScore ?? 0.5;
        var tokens = queryText.ToLowerInvariant()
            .Split([' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?'], StringSplitOptions.RemoveEmptyEntries)
            .Distinct()
            .ToList();
        if (tokens.Count == 0) return [];

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var articles = await db.Articles
            .Where(a => a.Status == "published")
            .Select(a => new { a.Id, a.Title, a.Content })
            .ToListAsync(ct);

        var results = new List<VectorSearchResult>();
        foreach (var a in articles)
        {
            var hay = ((a.Title ?? "") + " " + ContentExtractor.ExtractPlainText(a.Content)).ToLowerInvariant();
            var overlap = tokens.Count(t => hay.Contains(t));
            var score = (double)overlap / tokens.Count;
            if (score >= threshold)
                results.Add(new VectorSearchResult(a.Id, score, 0));
        }

        return results
            .OrderByDescending(r => r.Score).ThenBy(r => r.ArticleId)
            .Take(limit)
            .ToList();
    }

    public async Task<List<VectorChunkResult>> SearchChunksAsync(string queryText, int maxChunks,
        CancellationToken ct = default, double? minScore = null, int maxPerArticle = 3)
    {
        // Reuse the article-level ranking, then attach each article's searchable text as the
        // (single) chunk text — mirrors the real service returning stored chunk Content.
        var ranked = await SearchAsync(queryText, maxChunks, ct, minScore);
        if (ranked.Count == 0) return [];

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ids = ranked.Select(r => r.ArticleId).ToList();
        var byId = (await db.Articles.Where(a => ids.Contains(a.Id))
                .Select(a => new { a.Id, a.Title, a.Excerpt, a.Content })
                .ToListAsync(ct))
            .ToDictionary(a => a.Id);

        return ranked
            .Select(r => new VectorChunkResult(r.ArticleId, r.ChunkIndex, r.Score,
                byId.TryGetValue(r.ArticleId, out var a)
                    ? ContentExtractor.ExtractSearchableText(a.Title, a.Excerpt, a.Content, "")
                    : ""))
            .ToList();
    }
}

/// <summary>
/// Deterministic in-process replacement for the Ollama embedding client.
/// Produces 1024-dim bag-of-words vectors (matching the vector(1024) DB column):
/// each token is FNV-1a-hashed to a bucket, counts are accumulated and L2-normalized,
/// so texts sharing words get high cosine similarity and unrelated texts near zero.
/// </summary>
public sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public const int Dimensions = 1024;

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var embeddings = values
            .Select(v => new Embedding<float>(Embed(v)))
            .ToList();
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
    }

    private static ReadOnlyMemory<float> Embed(string text)
    {
        var vector = new float[Dimensions];
        var tokens = text.ToLowerInvariant()
            .Split([' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '"', '\''],
                StringSplitOptions.RemoveEmptyEntries);

        foreach (var token in tokens)
            vector[(int)(Fnv1aHash(token) % Dimensions)] += 1f;

        // L2 normalize so cosine similarity behaves like a real embedding model
        var norm = MathF.Sqrt(vector.Sum(x => x * x));
        if (norm > 0)
            for (var i = 0; i < vector.Length; i++)
                vector[i] /= norm;

        return vector;
    }

    /// <summary>Stable FNV-1a — string.GetHashCode() is randomized per process and must not be used.</summary>
    private static uint Fnv1aHash(string value)
    {
        var hash = 2166136261u;
        foreach (var c in value)
        {
            hash ^= c;
            hash *= 16777619u;
        }
        return hash;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }
}

/// <summary>
/// Deterministic in-process replacement for the Ollama chat client.
/// Returns a fixed answer and records the last request so tests can assert on the
/// exact prompt content (RAG context, source delimiters, injection sanitization).
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];
    /// <summary>Number of completion calls — lets tests distinguish single-pass from map-reduce.</summary>
    public int CallCount { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        LastMessages = messages.ToList();
        CallCount++;
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "FAKE-ANSWER")));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        LastMessages = messages.ToList();
        return Stream();

        static async IAsyncEnumerable<ChatResponseUpdate> Stream()
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "FAKE-ANSWER");
            await Task.CompletedTask;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }
}
