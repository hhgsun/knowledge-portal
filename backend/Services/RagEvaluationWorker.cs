using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public class RagEvaluationWorker(IServiceScopeFactory scopes, ILogger<RagEvaluationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                string? runId;
                using (var scope = scopes.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    runId = await db.RagEvaluationRuns.Where(x => x.Status == "pending")
                        .OrderBy(x => x.CreatedAt).Select(x => x.Id).FirstOrDefaultAsync(stoppingToken);
                }
                if (runId == null) { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); continue; }
                using var workScope = scopes.CreateScope();
                await workScope.ServiceProvider.GetRequiredService<RagEvaluationService>().ExecuteRunAsync(runId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "RAG evaluation worker failed"); await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        }
    }
}
