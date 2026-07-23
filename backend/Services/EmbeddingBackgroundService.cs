using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public class EmbeddingBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    EmbeddingFailureTracker failureTracker,
    PortalMetrics metrics,
    ILogger<EmbeddingBackgroundService> logger) : BackgroundService
{
    private readonly int _batchSize = config.GetValue("Ollama:BatchSize", 10);
    private readonly int _pollingInterval = config.GetValue("Ollama:PollingIntervalSeconds", 5);
    private readonly int _backoffSeconds = config.GetValue("Ollama:BackoffSeconds", 30);
    // Orphan-embedding safety net: sweep how often (hours). <=0 disables it.
    private readonly int _orphanCleanupIntervalHours = config.GetValue("Ollama:OrphanCleanupIntervalHours", 24);
    private DateTime _lastOrphanCleanupUtc = DateTime.MinValue; // MinValue → runs once shortly after startup

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Embedding background service started (batch={Batch}, poll={Poll}s)",
            _batchSize, _pollingInterval);

        await InvalidateStaleModelsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await MaybeCleanupOrphansAsync(stoppingToken);

            try
            {
                var processed = await ProcessBatchAsync(stoppingToken);
                if (processed > 0) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("Ollama unavailable: {Message}. Retrying in {Backoff}s", ex.Message, _backoffSeconds);
                await SafeDelay(TimeSpan.FromSeconds(_backoffSeconds), stoppingToken);
                continue;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in embedding background service");
                await SafeDelay(TimeSpan.FromSeconds(_backoffSeconds), stoppingToken);
                continue;
            }

            await SafeDelay(TimeSpan.FromSeconds(_pollingInterval), stoppingToken);
        }

        logger.LogInformation("Embedding background service stopped");
    }

    private async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        List<(string Id, DateTime UpdatedAt)> staleArticles;
        using (var listScope = scopeFactory.CreateScope())
        {
            var listDb = listScope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Over-fetch so articles waiting out a failure backoff don't starve the batch
            var candidates = await listDb.Articles
                .Where(a => a.Status == "published" && a.IndexedAt == null)
                .OrderBy(a => a.UpdatedAt)
                .Take(_batchSize * 3)
                .Select(a => new { a.Id, a.UpdatedAt })
                .ToListAsync(ct);
            staleArticles = candidates
                .Where(a => failureTracker.ShouldAttempt(a.Id, a.UpdatedAt, nowUtc))
                .Take(_batchSize)
                .Select(a => (a.Id, a.UpdatedAt))
                .ToList();
        }

        if (staleArticles.Count == 0) return 0;

        var processed = 0;
        foreach (var (articleId, updatedAt) in staleArticles)
        {
            // One scope per article: a failed save can't poison change tracking for the rest of the batch
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var embeddingService = scope.ServiceProvider.GetRequiredService<EmbeddingService>();

            try
            {
                var article = await db.Articles
                    .FirstOrDefaultAsync(a => a.Id == articleId && a.Status == "published" && a.IndexedAt == null, ct);
                if (article == null) continue;

                await embeddingService.EmbedArticleAsync(article, ct);
                failureTracker.RecordSuccess(articleId);
                processed++;
            }
            catch (HttpRequestException) { throw; }
            catch (Exception ex)
            {
                var info = failureTracker.RecordFailure(articleId, updatedAt, DateTime.UtcNow);
                metrics.EmbeddingFailures.Add(1);
                logger.LogError(ex, "Failed to embed article {ArticleId} (failure #{Count}), next retry at {NextAttempt:o}",
                    articleId, info.Count, info.NextAttemptUtc);
            }
        }

        logger.LogInformation("Processed {Count}/{Total} articles for embedding", processed, staleArticles.Count);
        return processed;
    }

    /// <summary>
    /// Periodically deletes embeddings for articles that are no longer published (the residual
    /// unpublish-during-embedding race the transactional guard can't fully close, plus any legacy
    /// orphans). Gated to once per <see cref="_orphanCleanupIntervalHours"/>; failures are logged
    /// and never disrupt the embedding loop.
    /// </summary>
    private async Task MaybeCleanupOrphansAsync(CancellationToken ct)
    {
        if (_orphanCleanupIntervalHours <= 0) return; // disabled
        if (DateTime.UtcNow - _lastOrphanCleanupUtc < TimeSpan.FromHours(_orphanCleanupIntervalHours)) return;
        _lastOrphanCleanupUtc = DateTime.UtcNow; // set before running so a failure won't tight-loop

        try
        {
            using var scope = scopeFactory.CreateScope();
            var embeddingService = scope.ServiceProvider.GetRequiredService<EmbeddingService>();
            var removed = await embeddingService.CleanupOrphanEmbeddingsAsync(ct);
            if (removed > 0)
                logger.LogInformation("Orphan embedding cleanup removed {Count} chunk(s)", removed);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Orphan embedding cleanup failed");
        }
    }

    private async Task InvalidateStaleModelsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var embeddingService = scope.ServiceProvider.GetRequiredService<EmbeddingService>();
            await embeddingService.InvalidateStaleModelAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check for stale model embeddings on startup");
        }
    }

    private static async Task SafeDelay(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct); }
        catch (OperationCanceledException) { }
    }
}
