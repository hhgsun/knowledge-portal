using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public class RagEvaluationWorker(IServiceScopeFactory scopes, ILogger<RagEvaluationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    private readonly string _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                string? runId;
                using (var scope = scopes.CreateScope())
                {
                    runId = await scope.ServiceProvider.GetRequiredService<RagEvaluationService>()
                        .ClaimNextAsync(_workerId, Lease, stoppingToken);
                }
                if (runId == null) { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); continue; }
                using var workScope = scopes.CreateScope();
                await workScope.ServiceProvider.GetRequiredService<RagEvaluationService>()
                    .ExecuteRunAsync(runId, _workerId, Lease, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "RAG evaluation worker failed"); await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        }
    }
}
