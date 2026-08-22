using System.Net.Http.Json;
using System.Text.Json;

namespace KnowledgePortal.Api.Services;

/// <summary>
/// Optional cross-encoder/external reranker adapter. The provider is fail-open to the local
/// deterministic reranker, bounded by candidate/document limits and a short timeout. It is
/// disabled by default because enabling it sends selected candidate text to the configured host.
/// </summary>
public sealed class ExternalRagChunkReranker(HttpClient http, LocalRagChunkReranker local,
    IConfiguration config, ILogger<ExternalRagChunkReranker> logger) : IRagChunkReranker
{
    private readonly bool _enabled = config.GetValue("Reranking:External:Enabled", false);
    private readonly string? _endpoint = config["Reranking:External:Endpoint"];
    private readonly string? _model = config["Reranking:External:Model"];
    private readonly string? _apiKey = config["Reranking:External:ApiKey"];
    private readonly int _timeoutSeconds = Math.Clamp(config.GetValue("Reranking:External:TimeoutSeconds", 8), 1, 30);
    private readonly int _maxCandidates = Math.Clamp(config.GetValue("Reranking:External:MaxCandidates", 50), 1, 100);
    private readonly int _maxCharacters = Math.Clamp(config.GetValue("Reranking:External:MaxDocumentCharacters", 4000), 500, 16000);
    private readonly double _externalWeight = Math.Clamp(config.GetValue("Reranking:External:ScoreWeight", .8), 0, 1);

    public async Task<IReadOnlyList<RagRetrievalChunk>> RerankAsync(string query,
        IReadOnlyList<RagChunkCandidate> candidates, CancellationToken ct = default)
    {
        var localRanked = await local.RerankAsync(query, candidates, ct);
        if (!_enabled || candidates.Count == 0 || !ValidEndpoint(_endpoint)) return localRanked;

        var selected = candidates.Take(_maxCandidates).ToList();
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.Authorization = new("Bearer", _apiKey);
        request.Content = JsonContent.Create(new
        {
            model = _model,
            query,
            documents = selected.Select((x, index) => new
            {
                id = index.ToString(),
                text = x.Chunk.ChunkText[..Math.Min(_maxCharacters, x.Chunk.ChunkText.Length)]
            })
        });

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
            var scores = ParseScores(json.RootElement, selected.Count);
            if (scores.Count == 0) throw new InvalidOperationException("External reranker returned no valid scores");

            var localScores = localRanked.ToDictionary(x => Key(x.Chunk), x => x.Score);
            var reranked = selected.Select((candidate, index) =>
            {
                var localScore = localScores.GetValueOrDefault(Key(candidate.Chunk));
                var externalScore = scores.GetValueOrDefault(index, localScore);
                return new RagRetrievalChunk(candidate.Chunk,
                    _externalWeight * externalScore + (1 - _externalWeight) * localScore,
                    candidate.MatchType);
            }).Concat(localRanked.Where(x => !selected.Any(c => Key(c.Chunk) == Key(x.Chunk))))
                .OrderByDescending(x => x.Score).ThenBy(x => x.Chunk.ArticleId)
                .ThenBy(x => x.Chunk.ChunkIndex).ToList();
            return reranked;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "External RAG reranker failed; using local reranker provider={Provider}",
                SafeProvider(_endpoint));
            return localRanked;
        }
    }

    private static Dictionary<int, double> ParseScores(JsonElement root, int count)
    {
        var output = new Dictionary<int, double>();
        if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return output;
        foreach (var item in results.EnumerateArray())
        {
            int? index = item.TryGetProperty("index", out var ix) && ix.TryGetInt32(out var parsed) ? parsed : null;
            if (index == null && item.TryGetProperty("id", out var id) && int.TryParse(id.GetString(), out parsed)) index = parsed;
            double? score = item.TryGetProperty("relevance_score", out var relevance) && relevance.TryGetDouble(out var rel) ? rel
                : item.TryGetProperty("score", out var value) && value.TryGetDouble(out var val) ? val : null;
            if (index is >= 0 && index < count && score != null && double.IsFinite(score.Value))
                output[index.Value] = Math.Clamp(score.Value, 0, 1);
        }
        return output;
    }

    private static bool ValidEndpoint(string? endpoint) => Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback);
    private static string SafeProvider(string? endpoint) => Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
        ? uri.Host : "invalid";
    private static string Key(VectorChunkResult x) => $"{x.ArticleId}:{x.SourceType}:{x.AttachmentId}:{x.ChunkIndex}";
}
