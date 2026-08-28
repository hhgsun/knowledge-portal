using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace KnowledgePortal.Api.Services;

/// <summary>Process-local bulkhead and circuit state for the optional cross-encoder.</summary>
public sealed class ExternalRerankerState(IConfiguration config)
{
    private readonly SemaphoreSlim _gate = new(Math.Clamp(
        config.GetValue("Reranking:External:ConcurrencyLimit", 4), 1, 32));
    private readonly int _queueTimeoutMs = Math.Clamp(
        config.GetValue("Reranking:External:QueueTimeoutMilliseconds", 100), 0, 5000);
    private readonly int _failureThreshold = Math.Clamp(
        config.GetValue("Reranking:External:CircuitBreakerFailureThreshold", 3), 1, 20);
    private readonly TimeSpan _breakDuration = TimeSpan.FromSeconds(Math.Clamp(
        config.GetValue("Reranking:External:CircuitBreakerSeconds", 30), 1, 300));
    private readonly object _sync = new();
    private int _consecutiveFailures;
    private DateTime _openUntil;

    public bool IsCircuitOpen
    {
        get { lock (_sync) return _openUntil > DateTime.UtcNow; }
    }

    public async Task<IDisposable?> TryEnterAsync(CancellationToken ct)
    {
        if (IsCircuitOpen) return null;
        using var queue = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (_queueTimeoutMs == 0)
        {
            if (!_gate.Wait(0)) return null;
        }
        else
        {
            queue.CancelAfter(TimeSpan.FromMilliseconds(_queueTimeoutMs));
            try { await _gate.WaitAsync(queue.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        }
        return new GateLease(_gate);
    }

    public void RecordSuccess()
    {
        lock (_sync) { _consecutiveFailures = 0; _openUntil = DateTime.MinValue; }
    }

    public void RecordFailure()
    {
        lock (_sync)
        {
            if (++_consecutiveFailures < _failureThreshold) return;
            _openUntil = DateTime.UtcNow + _breakDuration;
            _consecutiveFailures = 0;
        }
    }

    private sealed class GateLease(SemaphoreSlim gate) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) gate.Release();
        }
    }
}

/// <summary>
/// Optional production cross-encoder adapter. Candidate text is bounded and sent only after ACL
/// filtering. Provider failures, overload, incomplete responses, and an open circuit all fail open
/// to the deterministic local reranker.
/// </summary>
public sealed class ExternalRagChunkReranker(
    HttpClient http,
    LocalRagChunkReranker local,
    ExternalRerankerState state,
    IConfiguration config,
    PortalMetrics metrics,
    ILogger<ExternalRagChunkReranker> logger) : IRagChunkReranker
{
    private readonly bool _enabled = config.GetValue("Reranking:External:Enabled", false);
    private readonly string? _endpoint = config["Reranking:External:Endpoint"];
    private readonly string? _model = config["Reranking:External:Model"];
    private readonly string? _apiKey = config["Reranking:External:ApiKey"];
    private readonly string _apiKeyHeader = config["Reranking:External:ApiKeyHeader"] ?? "Authorization";
    private readonly string _requestFormat = (config["Reranking:External:RequestFormat"] ?? "objects").ToLowerInvariant();
    private readonly int _timeoutSeconds = Math.Clamp(config.GetValue("Reranking:External:TimeoutSeconds", 8), 1, 30);
    private readonly int _maxRetries = Math.Clamp(config.GetValue("Reranking:External:MaxRetries", 1), 0, 2);
    private readonly int _maxCandidates = Math.Clamp(config.GetValue("Reranking:External:MaxCandidates", 50), 1, 100);
    private readonly int _maxCharacters = Math.Clamp(config.GetValue("Reranking:External:MaxDocumentCharacters", 4000), 500, 16000);
    private readonly int _maxQueryCharacters = Math.Clamp(config.GetValue("Reranking:External:MaxQueryCharacters", 1200), 100, 4000);
    private readonly int _maxResponseBytes = Math.Clamp(config.GetValue("Reranking:External:MaxResponseBytes", 262144), 4096, 2_097_152);
    private readonly double _externalWeight = Math.Clamp(config.GetValue("Reranking:External:ScoreWeight", .8), 0, 1);
    private readonly double _minimumCoverage = Math.Clamp(config.GetValue("Reranking:External:MinimumScoreCoverage", .8), .5, 1);

    public async Task<IReadOnlyList<RagRetrievalChunk>> RerankAsync(string query,
        IReadOnlyList<RagChunkCandidate> candidates, CancellationToken ct = default)
    {
        var localRanked = await local.RerankAsync(query, candidates, ct);
        if (!_enabled || candidates.Count == 0 || !ValidEndpoint(_endpoint)) return localRanked;
        if (state.IsCircuitOpen) { Record("circuit_open", 0); return localRanked; }

        using var lease = await state.TryEnterAsync(ct);
        if (lease == null)
        {
            Record(state.IsCircuitOpen ? "circuit_open" : "bulkhead_rejected", 0);
            return localRanked;
        }

        var candidatesByKey = candidates.GroupBy(candidate => Key(candidate.Chunk), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var selected = localRanked.GroupBy(item => Key(item.Chunk), StringComparer.Ordinal)
            .Select(group => group.First()).Take(_maxCandidates)
            .Select(item => candidatesByKey[Key(item.Chunk)]).ToList();
        var watch = Stopwatch.StartNew();
        using var activity = PortalMetrics.RagActivities.StartActivity("rag.reranker");
        activity?.SetTag("rag.reranker_provider", SafeProvider(_endpoint));
        activity?.SetTag("rag.reranker_candidates", selected.Count);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));
            Dictionary<int, double>? rawScores = null;
            for (var attempt = 0; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    using var request = BuildRequest(query, selected);
                    using var response = await http.SendAsync(request,
                        HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    if (IsTransient(response.StatusCode) && attempt < _maxRetries)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(150 * (attempt + 1)), timeout.Token);
                        continue;
                    }
                    response.EnsureSuccessStatusCode();
                    rawScores = await ParseScoresAsync(response.Content, selected.Count, timeout.Token);
                    break;
                }
                catch (HttpRequestException) when (attempt < _maxRetries)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(150 * (attempt + 1)), timeout.Token);
                }
            }

            if (rawScores == null || rawScores.Count / (double)selected.Count < _minimumCoverage)
                throw new InvalidOperationException("Cross-encoder score coverage is below the configured minimum.");
            var scores = NormalizeScores(rawScores);
            if (scores.Count == 0)
                throw new InvalidOperationException("Cross-encoder returned no usable score distribution.");

            var localScores = localRanked.ToDictionary(item => Key(item.Chunk), item => item.Score, StringComparer.Ordinal);
            var selectedKeys = selected.Select(candidate => Key(candidate.Chunk)).ToHashSet(StringComparer.Ordinal);
            var reranked = selected.Select((candidate, index) =>
            {
                var localScore = localScores[Key(candidate.Chunk)];
                var score = scores.TryGetValue(index, out var externalScore)
                    ? _externalWeight * externalScore + (1 - _externalWeight) * localScore
                    : localScore;
                return new RagRetrievalChunk(candidate.Chunk, score, candidate.MatchType);
            }).Concat(localRanked.Where(item => !selectedKeys.Contains(Key(item.Chunk))))
                .OrderByDescending(item => item.Score).ThenBy(item => item.Chunk.ArticleId)
                .ThenBy(item => item.Chunk.ChunkIndex).ToList();

            state.RecordSuccess();
            watch.Stop(); Record("success", watch.Elapsed.TotalMilliseconds);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return reranked;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            state.RecordFailure();
            watch.Stop();
            var outcome = exception is OperationCanceledException ? "timeout" : "failure";
            Record(outcome, watch.Elapsed.TotalMilliseconds);
            logger.LogWarning(exception,
                "External RAG reranker failed; using local reranker provider={Provider}", SafeProvider(_endpoint));
            activity?.SetStatus(ActivityStatusCode.Error, outcome);
            return localRanked;
        }
    }

    private HttpRequestMessage BuildRequest(string query, IReadOnlyList<RagChunkCandidate> selected)
    {
        var boundedQuery = query[..Math.Min(query.Length, _maxQueryCharacters)];
        object payload = _requestFormat switch
        {
            "strings" => new
            {
                model = EmptyToNull(_model), query = boundedQuery,
                documents = selected.Select(DocumentText).ToArray(), top_n = selected.Count,
                return_documents = false
            },
            "texts" => new
            {
                model = EmptyToNull(_model), query = boundedQuery,
                texts = selected.Select(DocumentText).ToArray(), raw_scores = true
            },
            _ => new
            {
                model = EmptyToNull(_model), query = boundedQuery,
                documents = selected.Select((candidate, index) => new
                    { id = index.ToString(), text = DocumentText(candidate) }).ToArray()
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = JsonContent.Create(payload) };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.TryAddWithoutValidation("X-Request-ID", Guid.NewGuid().ToString("N"));
        if (!string.IsNullOrWhiteSpace(_apiKey)) AddApiKey(request);
        return request;
    }

    private string DocumentText(RagChunkCandidate candidate)
    {
        var metadata = $"Title: {candidate.Title}\nSource: {candidate.Chunk.SourceName ?? "article"}\n";
        var excerpt = string.IsNullOrWhiteSpace(candidate.Excerpt) ? "" : $"Excerpt: {candidate.Excerpt}\n";
        var value = metadata + excerpt + "Passage: " + candidate.Chunk.ChunkText;
        return value[..Math.Min(value.Length, _maxCharacters)];
    }

    private void AddApiKey(HttpRequestMessage request)
    {
        if (_apiKeyHeader.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Authorization = new("Bearer", _apiKey); return;
        }
        if (_apiKeyHeader.Equals("X-API-Key", StringComparison.OrdinalIgnoreCase)
            || _apiKeyHeader.Equals("Api-Key", StringComparison.OrdinalIgnoreCase))
            request.Headers.TryAddWithoutValidation(_apiKeyHeader, _apiKey);
        else throw new InvalidOperationException("Unsupported external reranker API key header.");
    }

    private async Task<Dictionary<int, double>> ParseScoresAsync(HttpContent content, int count, CancellationToken ct)
    {
        if (content.Headers.ContentLength > _maxResponseBytes)
            throw new InvalidOperationException("Cross-encoder response exceeded the configured byte limit.");
        await using var source = await content.ReadAsStreamAsync(ct);
        await using var bounded = new MemoryStream();
        var buffer = new byte[8192]; var total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, ct);
            if (read == 0) break;
            total += read;
            if (total > _maxResponseBytes)
                throw new InvalidOperationException("Cross-encoder response exceeded the configured byte limit.");
            await bounded.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        bounded.Position = 0;
        using var json = await JsonDocument.ParseAsync(bounded, cancellationToken: ct);
        return ParseScores(json.RootElement, count);
    }

    internal static Dictionary<int, double> ParseScores(JsonElement root, int count)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("scores", out var numericScores)
            && numericScores.ValueKind == JsonValueKind.Array
            && numericScores.EnumerateArray().All(value => value.TryGetDouble(out _)))
            return numericScores.EnumerateArray().Select((value, index) => (index, Score: value.GetDouble()))
                .Where(item => item.index < count && double.IsFinite(item.Score))
                .ToDictionary(item => item.index, item => item.Score);

        JsonElement results;
        if (root.ValueKind == JsonValueKind.Array) results = root;
        else if (root.TryGetProperty("results", out var nested)) results = nested;
        else if (root.TryGetProperty("data", out nested)) results = nested;
        else return [];
        if (results.ValueKind != JsonValueKind.Array) return [];

        var output = new Dictionary<int, double>(); var ordinal = 0;
        foreach (var item in results.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var direct))
            {
                if (ordinal < count && double.IsFinite(direct)) output[ordinal] = direct;
                ordinal++; continue;
            }
            if (item.ValueKind != JsonValueKind.Object) continue;
            int? index = item.TryGetProperty("index", out var indexValue)
                         && indexValue.TryGetInt32(out var parsedIndex) ? parsedIndex : null;
            if (index == null && item.TryGetProperty("id", out var idValue))
            {
                if (idValue.ValueKind == JsonValueKind.Number && idValue.TryGetInt32(out parsedIndex))
                    index = parsedIndex;
                else if (idValue.ValueKind == JsonValueKind.String
                         && int.TryParse(idValue.GetString(), out parsedIndex))
                    index = parsedIndex;
            }
            index ??= ordinal;
            double? score = Score(item, "relevance_score") ?? Score(item, "score") ?? Score(item, "logit");
            if (index is >= 0 && index < count && score != null && double.IsFinite(score.Value)
                && !output.ContainsKey(index.Value)) output[index.Value] = score.Value;
            ordinal++;
        }
        return output;
    }

    internal static Dictionary<int, double> NormalizeScores(IReadOnlyDictionary<int, double> scores)
    {
        if (scores.Count == 0) return [];
        if (scores.Values.All(value => value is >= 0 and <= 1)) return scores.ToDictionary();
        var min = scores.Values.Min(); var max = scores.Values.Max();
        if (Math.Abs(max - min) < 1e-12) return [];
        return scores.ToDictionary(item => item.Key, item => (item.Value - min) / (max - min));
    }

    private void Record(string outcome, double elapsedMs)
    {
        metrics.RagRerankerRequests.Add(1,
            new KeyValuePair<string, object?>("outcome", outcome));
        if (elapsedMs > 0) metrics.RagRerankerDuration.Record(elapsedMs,
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    private static double? Score(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.TryGetDouble(out var parsed) ? parsed : null;
    private static bool IsTransient(HttpStatusCode status) => status == HttpStatusCode.RequestTimeout
        || status == HttpStatusCode.TooManyRequests || (int)status >= 500;
    private static bool ValidEndpoint(string? endpoint) => Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
        && string.IsNullOrEmpty(uri.UserInfo) && (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback);
    private static string SafeProvider(string? endpoint) => Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
        ? uri.Host : "invalid";
    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static string Key(VectorChunkResult value) =>
        $"{value.ArticleId}:{value.SourceType}:{value.AttachmentId}:{value.ChunkIndex}";
}
