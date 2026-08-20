namespace KnowledgePortal.Api.Services;

/// <summary>
/// Bounded, cached Ollama readiness probe. Health polling must not start an unbounded embedding
/// request on every scrape or keep a readiness connection open for the model client's full timeout.
/// </summary>
public sealed class OllamaHealthProbe(
    IServiceScopeFactory scopes,
    IConfiguration config,
    ILogger<OllamaHealthProbe> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(
        Math.Max(1, config.GetValue("Health:OllamaProbeTimeoutSeconds", 3)));
    private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(
        Math.Max(1, config.GetValue("Health:OllamaProbeCacheSeconds", 15)));
    private bool _lastResult;
    private DateTime _validUntilUtc;

    public async Task<bool> CheckAsync(CancellationToken ct)
    {
        if (_validUntilUtc > DateTime.UtcNow) return _lastResult;

        await _gate.WaitAsync(ct);
        try
        {
            if (_validUntilUtc > DateTime.UtcNow) return _lastResult;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_timeout);
            try
            {
                using var scope = scopes.CreateScope();
                var embeddings = scope.ServiceProvider.GetService<EmbeddingService>();
                _lastResult = embeddings != null && await embeddings.IsOllamaAvailableAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Ollama readiness probe exceeded {TimeoutMs} ms", _timeout.TotalMilliseconds);
                _lastResult = false;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Ollama readiness probe failed");
                _lastResult = false;
            }
            _validUntilUtc = DateTime.UtcNow + _cacheDuration;
            return _lastResult;
        }
        finally
        {
            _gate.Release();
        }
    }
}
