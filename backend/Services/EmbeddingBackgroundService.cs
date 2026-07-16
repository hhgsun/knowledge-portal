using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public class EmbeddingBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<EmbeddingBackgroundService> logger) : BackgroundService
{
    private readonly int _batchSize = config.GetValue("Ollama:BatchSize", 10);
    private readonly int _pollingInterval = config.GetValue("Ollama:PollingIntervalSeconds", 5);
    private readonly int _backoffSeconds = config.GetValue("Ollama:BackoffSeconds", 30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Embedding background service started (batch={Batch}, poll={Poll}s)",
            _batchSize, _pollingInterval);

        await InvalidateStaleModelsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
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
        List<string> staleArticleIds;
        using (var listScope = scopeFactory.CreateScope())
        {
            var listDb = listScope.ServiceProvider.GetRequiredService<AppDbContext>();
            staleArticleIds = await listDb.Articles
                .Where(a => a.Status == "published" && a.IndexedAt == null)
                .OrderBy(a => a.UpdatedAt)
                .Take(_batchSize)
                .Select(a => a.Id)
                .ToListAsync(ct);
        }

        if (staleArticleIds.Count == 0) return 0;

        var processed = 0;
        foreach (var articleId in staleArticleIds)
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
                processed++;
            }
            catch (HttpRequestException) { throw; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to embed article {ArticleId}, skipping", articleId);
            }
        }

        logger.LogInformation("Processed {Count}/{Total} articles for embedding", processed, staleArticleIds.Count);
        return processed;
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
