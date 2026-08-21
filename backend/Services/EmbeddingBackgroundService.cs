using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

/// <summary>
/// Consumes the durable PostgreSQL index queue with bounded parallelism. The historical class
/// name is retained to avoid breaking test-host service removal and operational dashboards.
/// </summary>
public class EmbeddingBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    PortalMetrics metrics,
    ILogger<EmbeddingBackgroundService> logger) : BackgroundService
{
    private readonly int _parallelism = Math.Max(1, config.GetValue("Indexing:WorkerCount", 4));
    private readonly int _claimBatchSize = Math.Max(1, config.GetValue("Indexing:ClaimBatchSize", 20));
    private readonly int _pollingInterval = Math.Max(1, config.GetValue("Indexing:PollingIntervalSeconds", 2));
    private readonly int _leaseMinutes = Math.Max(1, config.GetValue("Indexing:LeaseMinutes", 15));
    private readonly int _reconciliationInterval = Math.Max(5,
        config.GetValue("Indexing:ReconciliationIntervalSeconds", 60));
    private readonly int _orphanCleanupIntervalHours = config.GetValue("Ollama:OrphanCleanupIntervalHours", 24);
    private DateTime _lastReconciliationUtc = DateTime.MinValue;
    private DateTime _lastOrphanCleanupUtc = DateTime.MinValue;
    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Durable index worker started (parallelism={Parallelism}, batch={Batch})",
            _parallelism, _claimBatchSize);

        await InitializeAsync(stoppingToken);
        using var gate = new SemaphoreSlim(_parallelism);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MaybeReconcileDirtyArticlesAsync(stoppingToken);
                await MaybeCleanupOrphansAsync(stoppingToken);
                List<IndexJobClaim> claims;
                using (var scope = scopeFactory.CreateScope())
                {
                    var queue = scope.ServiceProvider.GetRequiredService<IndexJobQueue>();
                    claims = await queue.ClaimAsync(_workerId, _claimBatchSize,
                        TimeSpan.FromMinutes(_leaseMinutes), stoppingToken);
                }

                if (claims.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_pollingInterval), stoppingToken);
                    continue;
                }

                var tasks = claims.Select(async claim =>
                {
                    await gate.WaitAsync(stoppingToken);
                    try { await ProcessAsync(claim, stoppingToken); }
                    finally { gate.Release(); }
                });
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Durable index worker loop failed");
                await SafeDelay(TimeSpan.FromSeconds(_pollingInterval), stoppingToken);
            }
        }
    }

    private async Task ProcessAsync(IndexJobClaim claim, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<IndexJobQueue>();
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Lease renewal uses its own scope/DbContext. EF DbContext is not thread-safe and the
        // main scope is simultaneously used by FTS/embedding work below.
        var heartbeat = KeepLeaseAliveAsync(claim, heartbeatCts.Token);
        try
        {
            var article = await db.Articles.FirstOrDefaultAsync(a => a.Id == claim.ArticleId, ct);
            if (article != null)
            {
                var fts = scope.ServiceProvider.GetRequiredService<FullTextSearchService>();
                var embeddings = scope.ServiceProvider.GetService<EmbeddingService>();
                await fts.SyncArticleAsync(article, ct);
                if (article.Status == "published" && embeddings != null)
                    await embeddings.EmbedArticleAsync(article, ct);
                else if (embeddings != null)
                    await embeddings.RemoveEmbeddingAsync(article.Id, ct);
            }
            await queue.CompleteAsync(claim, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            metrics.EmbeddingFailures.Add(1);
            logger.LogError(ex, "Index job failed for article {ArticleId}, generation {Generation}",
                claim.ArticleId, claim.Generation);
            await queue.FailAsync(claim, ex, CancellationToken.None);
        }
        finally
        {
            heartbeatCts.Cancel();
            try { await heartbeat; } catch (OperationCanceledException) { }
        }
    }

    private async Task KeepLeaseAliveAsync(IndexJobClaim claim, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var queue = scope.ServiceProvider.GetRequiredService<IndexJobQueue>();
        var interval = TimeSpan.FromSeconds(Math.Max(10, _leaseMinutes * 60 / 3));
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(interval, ct);
            if (await queue.RenewLeaseAsync(claim, ct) == 0) return;
        }
    }

    private async Task InitializeAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var embeddings = scope.ServiceProvider.GetService<EmbeddingService>();
            if (embeddings != null) await embeddings.InvalidateStaleModelAsync(ct);
            var queued = await scope.ServiceProvider.GetRequiredService<IndexJobQueue>()
                .BackfillDirtyArticlesAsync(ct);
            if (queued > 0) logger.LogInformation("Queued {Count} dirty articles for indexing", queued);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Index queue startup initialization failed");
        }
    }

    private async Task MaybeCleanupOrphansAsync(CancellationToken ct)
    {
        if (_orphanCleanupIntervalHours <= 0 ||
            DateTime.UtcNow - _lastOrphanCleanupUtc < TimeSpan.FromHours(_orphanCleanupIntervalHours)) return;
        _lastOrphanCleanupUtc = DateTime.UtcNow;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var embeddings = scope.ServiceProvider.GetService<EmbeddingService>();
            if (embeddings == null) return;
            var removed = await embeddings.CleanupOrphanEmbeddingsAsync(ct);
            if (removed > 0) logger.LogInformation("Removed {Count} orphan embedding chunks", removed);
        }
        catch (Exception ex) { logger.LogError(ex, "Orphan embedding cleanup failed"); }
    }

    private async Task MaybeReconcileDirtyArticlesAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow - _lastReconciliationUtc < TimeSpan.FromSeconds(_reconciliationInterval)) return;

        // Set the timestamp only after a successful database round-trip. If PostgreSQL is down,
        // the worker retries on its normal polling cadence instead of leaving a queue gap until
        // the next long reconciliation interval.
        using var scope = scopeFactory.CreateScope();
        var reconciled = await scope.ServiceProvider.GetRequiredService<IndexJobQueue>()
            .ReconcileDirtyArticlesAsync(ct);
        _lastReconciliationUtc = DateTime.UtcNow;
        if (reconciled > 0)
            logger.LogWarning("Reconciled {Count} dirty articles missing from the durable index queue", reconciled);
    }

    private static async Task SafeDelay(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { }
    }
}
