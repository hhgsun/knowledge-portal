namespace KnowledgePortal.Api.Services;

public sealed class RagBusyException(string message) : Exception(message);
public sealed class RagCircuitOpenException(string message) : Exception(message);
public sealed class RagStageTimeoutException(string stage, int seconds)
    : TimeoutException($"RAG stage '{stage}' exceeded {seconds} seconds.");
public record RagRuntimeSnapshot(int ActiveRequests, bool CircuitOpen, DateTime? CircuitOpenUntil,
    int RequestBudgetSeconds, int RetrievalTimeoutSeconds, int GenerationTimeoutSeconds,
    int ReduceTimeoutSeconds, int MapParallelism, int AiRetryCount);

/// <summary>Process-wide resilience controls for REST, MCP and evaluation RAG traffic.</summary>
public sealed class RagResilienceService(IConfiguration config, PortalMetrics metrics, ILogger<RagResilienceService> logger)
{
    private readonly SemaphoreSlim _bulkhead = new(Math.Max(1, config.GetValue("RagResilience:ConcurrencyLimit", 4)));
    private readonly int _queueTimeoutSeconds = Math.Max(1, config.GetValue("RagResilience:QueueTimeoutSeconds", 5));
    private readonly int _failureThreshold = Math.Max(1, config.GetValue("RagResilience:CircuitBreakerFailureThreshold", 5));
    private readonly TimeSpan _breakDuration = TimeSpan.FromSeconds(Math.Max(1, config.GetValue("RagResilience:CircuitBreakerSeconds", 30)));
    private readonly object _circuitLock = new();
    private int _consecutiveAiFailures;
    private DateTime _openUntil;
    private int _activeRequests;

    public int RequestBudgetSeconds => Math.Max(10, config.GetValue("RagResilience:RequestBudgetSeconds", 120));
    public int RetrievalTimeoutSeconds => Math.Max(1, config.GetValue("RagResilience:RetrievalTimeoutSeconds", 15));
    public int GenerationTimeoutSeconds => Math.Max(1, config.GetValue("RagResilience:GenerationTimeoutSeconds", 45));
    public int ReduceTimeoutSeconds => Math.Max(1, config.GetValue("RagResilience:ReduceTimeoutSeconds", 60));
    public int AiRetryCount => Math.Clamp(config.GetValue("RagResilience:AiRetryCount", 1), 0, 3);
    public int MapParallelism => Math.Clamp(config.GetValue("RagResilience:MapParallelism", 3), 1, 8);

    public async Task<IAsyncDisposable> EnterAsync(CancellationToken ct)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        wait.CancelAfter(TimeSpan.FromSeconds(_queueTimeoutSeconds));
        try { await _bulkhead.WaitAsync(wait.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new RagBusyException("RAG capacity is full. Please retry shortly."); }
        Interlocked.Increment(ref _activeRequests);
        return new Lease(_bulkhead, () => Interlocked.Decrement(ref _activeRequests));
    }

    public RagRuntimeSnapshot Snapshot()
    {
        lock (_circuitLock) return new(Volatile.Read(ref _activeRequests), _openUntil > DateTime.UtcNow,
            _openUntil > DateTime.UtcNow ? _openUntil : null, RequestBudgetSeconds, RetrievalTimeoutSeconds,
            GenerationTimeoutSeconds, ReduceTimeoutSeconds, MapParallelism, AiRetryCount);
    }

    public async Task<T> ExecuteAsync<T>(string stage, int timeoutSeconds, int retries, bool aiBound,
        Func<CancellationToken, Task<T>> action, CancellationToken requestToken)
    {
        if (aiBound && CircuitOpen()) throw new RagCircuitOpenException("RAG generation circuit is temporarily open.");
        for (var attempt = 0; ; attempt++)
        {
            using var activity = PortalMetrics.RagActivities.StartActivity($"rag.{stage}");
            activity?.SetTag("rag.stage", NormalizeStage(stage));
            activity?.SetTag("rag.attempt", attempt + 1);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            if (aiBound) metrics.RagLlmCalls.Add(1, Tags("stage", NormalizeStage(stage)));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                var value = await action(timeout.Token);
                watch.Stop(); RecordStage(stage, "success", watch.Elapsed.TotalMilliseconds, aiBound);
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Ok);
                if (aiBound) RecordSuccess();
                return value;
            }
            catch (OperationCanceledException) when (requestToken.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) when (attempt >= retries)
            {
                watch.Stop(); RecordStage(stage, "timeout", watch.Elapsed.TotalMilliseconds, aiBound);
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, "timeout");
                if (aiBound) RecordFailure(stage);
                throw new RagStageTimeoutException(stage, timeoutSeconds);
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < retries)
            {
                watch.Stop(); RecordStage(stage, "retry", watch.Elapsed.TotalMilliseconds, aiBound);
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, "transient_retry");
                logger.LogWarning(ex, "Transient RAG {Stage} failure; retry {Attempt}", stage, attempt + 1);
                await Task.Delay(TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)), requestToken);
            }
            catch (Exception ex)
            {
                watch.Stop(); RecordStage(stage, "error", watch.Elapsed.TotalMilliseconds, aiBound);
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ErrorKind(ex));
                if (aiBound) RecordFailure(stage);
                throw;
            }
        }
    }

    private bool CircuitOpen() { lock (_circuitLock) return _openUntil > DateTime.UtcNow; }
    private void RecordSuccess() { lock (_circuitLock) { _consecutiveAiFailures = 0; _openUntil = DateTime.MinValue; } }
    private void RecordFailure(string stage)
    {
        lock (_circuitLock)
        {
            if (++_consecutiveAiFailures < _failureThreshold) return;
            _openUntil = DateTime.UtcNow + _breakDuration; _consecutiveAiFailures = 0;
            logger.LogWarning("RAG AI circuit opened for {Seconds}s after failures at {Stage}", _breakDuration.TotalSeconds, stage);
        }
    }
    private static bool IsTransient(Exception ex) => ex is HttpRequestException or TimeoutException or TaskCanceledException;
    private void RecordStage(string stage, string outcome, double elapsedMs, bool aiBound)
    {
        var normalized = NormalizeStage(stage);
        metrics.RagStageDuration.Record(elapsedMs, new("stage", normalized), new("outcome", outcome));
        if (outcome is "error" or "timeout") metrics.RagFailures.Add(1, new("stage", normalized), new("error_type", outcome));
        if (aiBound) { /* RagLlmCalls is recorded at attempt start, including timed-out calls. */ }
    }
    private static string NormalizeStage(string stage) => stage.StartsWith("map-", StringComparison.Ordinal) ? "map" : stage;
    private static string ErrorKind(Exception ex) => ex is HttpRequestException ? "http" : ex is TimeoutException or TaskCanceledException ? "timeout" : "unexpected";
    private static KeyValuePair<string, object?>[] Tags(string key, object? value) => [new(key, value)];
    private sealed class Lease(SemaphoreSlim gate, Action onDispose) : IAsyncDisposable
    {
        private int _disposed;
        public ValueTask DisposeAsync() { if (Interlocked.Exchange(ref _disposed, 1) == 0) { onDispose(); gate.Release(); } return ValueTask.CompletedTask; }
    }
}
