using System.Collections.Concurrent;
using System.Diagnostics;

namespace KnowledgePortal.Api.Services;

public sealed record AssistantClassifierCacheValue(AssistantRoute Route, double Confidence,
    string ReasonCode, bool IncludeSearchResults);

/// <summary>
/// Instance-local bounded classifier execution and exact-query cache. The supported production
/// topology is one backend instance; raw queries are never used as cache keys or retained.
/// </summary>
public sealed class AssistantClassifierResilienceService(
    IConfiguration config,
    PortalMetrics metrics,
    ILogger<AssistantClassifierResilienceService> logger)
{
    private readonly SemaphoreSlim slots = new(Math.Clamp(
        config.GetValue("AgenticRouting:ClassifierConcurrencyLimit", 2), 1, 16));
    private readonly ConcurrentDictionary<string, CacheEntry> cache = new();
    private readonly object circuitLock = new();
    private int consecutiveFailures;
    private DateTime circuitOpenUntil;

    public bool TryGet(string fingerprint, out AssistantClassifierCacheValue value)
    {
        value = default!;
        if (!cache.TryGetValue(fingerprint, out var entry)) return false;
        if (entry.ExpiresAt <= DateTime.UtcNow)
        {
            cache.TryRemove(fingerprint, out _);
            return false;
        }
        value = entry.Value;
        metrics.AssistantClassifierRequests.Add(1,
            new KeyValuePair<string, object?>("outcome", "cache_hit"));
        return true;
    }

    public void Set(string fingerprint, AssistantClassifierCacheValue value)
    {
        var seconds = Math.Clamp(config.GetValue("AgenticRouting:ClassifierCacheSeconds", 300), 0, 3600);
        if (seconds == 0) return;
        var maxEntries = Math.Clamp(config.GetValue("AgenticRouting:ClassifierCacheMaxEntries", 1000), 10, 10_000);
        if (cache.Count >= maxEntries)
        {
            foreach (var key in cache.OrderBy(pair => pair.Value.ExpiresAt)
                         .Take(Math.Max(1, maxEntries / 10)).Select(pair => pair.Key))
                cache.TryRemove(key, out _);
        }
        cache[fingerprint] = new(value, DateTime.UtcNow.AddSeconds(seconds));
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action,
        CancellationToken requestToken)
    {
        lock (circuitLock)
        {
            if (circuitOpenUntil > DateTime.UtcNow)
            {
                metrics.AssistantClassifierRequests.Add(1,
                    new KeyValuePair<string, object?>("outcome", "circuit_open"));
                throw new InvalidOperationException("Assistant classifier circuit is open.");
            }
        }

        var queueSeconds = Math.Clamp(config.GetValue(
            "AgenticRouting:ClassifierQueueTimeoutSeconds", 2), 1, 15);
        using var queueBudget = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
        queueBudget.CancelAfter(TimeSpan.FromSeconds(queueSeconds));
        try
        {
            await slots.WaitAsync(queueBudget.Token);
        }
        catch (OperationCanceledException) when (!requestToken.IsCancellationRequested)
        {
            metrics.AssistantClassifierRequests.Add(1,
                new KeyValuePair<string, object?>("outcome", "busy"));
            throw new InvalidOperationException("Assistant classifier capacity is full.");
        }

        var watch = Stopwatch.StartNew();
        metrics.AssistantClassifierActive.Add(1);
        using var executionBudget = CancellationTokenSource.CreateLinkedTokenSource(requestToken);
        var timeoutSeconds = Math.Clamp(config.GetValue(
            "AgenticRouting:ClassifierTimeoutSeconds", 8), 1, 30);
        executionBudget.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            var result = await action(executionBudget.Token);
            watch.Stop();
            lock (circuitLock)
            {
                consecutiveFailures = 0;
                circuitOpenUntil = DateTime.MinValue;
            }
            Record("success", watch.Elapsed.TotalMilliseconds);
            return result;
        }
        catch (OperationCanceledException) when (requestToken.IsCancellationRequested)
        {
            watch.Stop();
            Record("cancelled", watch.Elapsed.TotalMilliseconds);
            throw;
        }
        catch (OperationCanceledException)
        {
            watch.Stop();
            Record("timeout", watch.Elapsed.TotalMilliseconds);
            RecordFailure();
            throw;
        }
        catch
        {
            watch.Stop();
            Record("failure", watch.Elapsed.TotalMilliseconds);
            RecordFailure();
            throw;
        }
        finally
        {
            metrics.AssistantClassifierActive.Add(-1);
            slots.Release();
        }
    }

    private void RecordFailure()
    {
        var threshold = Math.Clamp(config.GetValue(
            "AgenticRouting:ClassifierCircuitFailureThreshold", 3), 1, 20);
        lock (circuitLock)
        {
            if (++consecutiveFailures < threshold) return;
            var seconds = Math.Clamp(config.GetValue(
                "AgenticRouting:ClassifierCircuitSeconds", 30), 1, 300);
            circuitOpenUntil = DateTime.UtcNow.AddSeconds(seconds);
            consecutiveFailures = 0;
            logger.LogWarning("Assistant classifier circuit opened for {Seconds}s", seconds);
        }
    }

    private void Record(string outcome, double milliseconds)
    {
        var tag = new KeyValuePair<string, object?>("outcome", outcome);
        metrics.AssistantClassifierRequests.Add(1, tag);
        metrics.AssistantClassifierDuration.Record(milliseconds, tag);
    }

    private sealed record CacheEntry(AssistantClassifierCacheValue Value, DateTime ExpiresAt);
}
