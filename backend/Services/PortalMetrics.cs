using System.Diagnostics;
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
    public Counter<long> McpToolCalls { get; }
    public Counter<long> McpToolErrors { get; }
    public Histogram<double> McpToolDuration { get; }
    public Histogram<long> McpToolOutputBytes { get; }

    public PortalMetrics(IServiceScopeFactory scopeFactory)
    {
        EmbeddingFailures = _meter.CreateCounter<long>(
            "kp_embedding_failures",
            description: "Embedding attempts that failed (per-article, before backoff retry)");
        McpToolCalls = _meter.CreateCounter<long>("kp_mcp_tool_calls", description: "MCP tool calls by tool and outcome");
        McpToolErrors = _meter.CreateCounter<long>("kp_mcp_tool_errors", description: "Failed MCP tool calls by tool");
        McpToolDuration = _meter.CreateHistogram<double>("kp_mcp_tool_duration_ms", unit: "ms", description: "MCP tool execution duration");
        McpToolOutputBytes = _meter.CreateHistogram<long>("kp_mcp_tool_output_bytes", unit: "By", description: "Serialized MCP tool result size");

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
