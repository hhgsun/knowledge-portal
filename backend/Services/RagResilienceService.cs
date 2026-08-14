namespace KnowledgePortal.Api.Services;

public sealed class RagBusyException(string message) : Exception(message);
public sealed class RagCircuitOpenException(string message) : Exception(message);
public sealed class RagStageTimeoutException(string stage, int seconds)
    : TimeoutException($"RAG stage '{stage}' exceeded {seconds} seconds.");

/// <summary>Process-wide resilience controls for REST, MCP and evaluation RAG traffic.</summary>
public sealed class RagResilienceService(IConfiguration config, ILogger<RagResilienceService> logger)
{
    private readonly SemaphoreSlim _bulkhead = new(Math.Max(1, config.GetValue("RagResilience:ConcurrencyLimit", 4)));
    private readonly int _queueTimeoutSeconds = Math.Max(1, config.GetValue("RagResilience:QueueTimeoutSeconds", 5));
    private readonly int _failureThreshold = Math.Max(1, config.GetValue("RagResilience:CircuitBreakerFailureThreshold", 5));
    private readonly TimeSpan _breakDuration = TimeSpan.FromSeconds(Math.Max(1, config.GetValue("RagResilience:CircuitBreakerSeconds", 30)));
    private readonly object _circuitLock = new();
    private int _consecutiveAiFailures;
    private DateTime _openUntil;

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
        return new Lease(_bulkhead);
    }

    public async Task<T> ExecuteAsync<T>(string stage, int timeoutSeconds, int retries, bool aiBound,
        Func<CancellationToken, Task<T>> action, CancellationToken requestToken)
    {
        if (aiBound && CircuitOpen()) throw new RagCircuitOpenException("RAG generation circuit is temporarily open.");
        for (var attempt = 0; ; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                var value = await action(timeout.Token);
                if (aiBound) RecordSuccess();
                return value;
            }
            catch (OperationCanceledException) when (requestToken.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) when (attempt >= retries)
            {
                if (aiBound) RecordFailure(stage);
                throw new RagStageTimeoutException(stage, timeoutSeconds);
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < retries)
            {
                logger.LogWarning(ex, "Transient RAG {Stage} failure; retry {Attempt}", stage, attempt + 1);
                await Task.Delay(TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)), requestToken);
            }
            catch
            {
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
    private sealed class Lease(SemaphoreSlim gate) : IAsyncDisposable { public ValueTask DisposeAsync() { gate.Release(); return ValueTask.CompletedTask; } }
}
