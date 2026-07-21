using System.Diagnostics.Metrics;
using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

/// <summary>
/// Application metrics exposed via OpenTelemetry → Prometheus (/metrics).
/// Kept alive as a singleton for the process lifetime — observable gauges are
/// polled on every scrape.
/// </summary>
public sealed class PortalMetrics
{
    public const string MeterName = "KnowledgePortal";

    private readonly Meter _meter = new(MeterName);

    /// <summary>Total embedding failures since process start (incremented by the background service).</summary>
    public Counter<long> EmbeddingFailures { get; }

    public PortalMetrics(IServiceScopeFactory scopeFactory)
    {
        EmbeddingFailures = _meter.CreateCounter<long>(
            "kp_embedding_failures",
            description: "Embedding attempts that failed (per-article, before backoff retry)");

        _meter.CreateObservableGauge(
            "kp_pending_embeddings",
            () =>
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                return db.Articles.Count(a => a.Status == "published" && a.IndexedAt == null);
            },
            description: "Published articles waiting to be embedded");
    }
}
