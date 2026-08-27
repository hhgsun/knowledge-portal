using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;

namespace KnowledgePortal.Api.Services;

public sealed record AssistantShadowRequest(string Query, string PrimaryRoute,
    double PrimaryConfidence);

public sealed class AssistantShadowRoutingQueue(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    PortalMetrics metrics,
    ILogger<AssistantShadowRoutingQueue> logger) : BackgroundService
{
    private readonly Channel<AssistantShadowRequest> queue = Channel.CreateBounded<AssistantShadowRequest>(
        new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true, SingleWriter = false });

    public void TryEnqueue(string query, AssistantRouteDecision primary)
    {
        if (!config.GetValue("AgenticRouting:Shadow:Enabled", false)) return;
        var percentage = Math.Clamp(config.GetValue("AgenticRouting:Shadow:SamplePercentage", 10), 0, 100);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(query.Trim()));
        if (hash[0] / 255d * 100 >= percentage) return;
        if (!queue.Writer.TryWrite(new(query, AssistantRouterService.RouteName(primary.Route),
                primary.Confidence)))
            metrics.AssistantShadowComparisons.Add(1,
                new KeyValuePair<string, object?>("outcome", "queue_dropped"));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var router = scope.ServiceProvider.GetRequiredService<AssistantRouterService>();
                var shadow = await router.RouteShadowAsync(item.Query, stoppingToken);
                if (shadow == null) continue;
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var primaryModel = config["AgenticRouting:Model"] ?? config["Ollama:ChatModel"] ?? "unknown";
                var shadowModel = config["AgenticRouting:Shadow:Model"] ?? primaryModel;
                db.AssistantRoutingShadowSamples.Add(new AssistantRoutingShadowSample
                {
                    QueryFingerprint = Convert.ToHexString(SHA256.HashData(
                        Encoding.UTF8.GetBytes(item.Query.Trim()))).ToLowerInvariant(),
                    PrimaryRoute = item.PrimaryRoute,
                    PrimaryConfidence = item.PrimaryConfidence,
                    ShadowRoute = AssistantRouterService.RouteName(shadow.Route),
                    ShadowConfidence = shadow.Confidence,
                    PrimaryModel = primaryModel,
                    ShadowModel = shadowModel,
                    Agreed = item.PrimaryRoute == AssistantRouterService.RouteName(shadow.Route)
                });
                await db.SaveChangesAsync(stoppingToken);
                metrics.AssistantShadowComparisons.Add(1,
                    new KeyValuePair<string, object?>("outcome",
                        item.PrimaryRoute == AssistantRouterService.RouteName(shadow.Route)
                            ? "agree" : "disagree"));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Assistant shadow routing evaluation failed");
                metrics.AssistantShadowComparisons.Add(1,
                    new KeyValuePair<string, object?>("outcome", "failure"));
            }
        }
    }
}
