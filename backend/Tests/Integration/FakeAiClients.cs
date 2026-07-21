using Microsoft.Extensions.AI;

namespace KnowledgePortal.Api.Tests.Integration;

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

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        LastMessages = messages.ToList();
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
