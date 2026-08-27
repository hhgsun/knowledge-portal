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
    public const string ActivitySourceName = "KnowledgePortal.Rag";
    public static readonly ActivitySource RagActivities = new(ActivitySourceName);

    private readonly Meter _meter = new(MeterName);

    /// <summary>Total embedding failures since process start (incremented by the background service).</summary>
    public Counter<long> EmbeddingFailures { get; }
    public Counter<long> McpToolCalls { get; }
    public Counter<long> McpToolErrors { get; }
    public Histogram<double> McpToolDuration { get; }
    public Histogram<long> McpToolOutputBytes { get; }
    public Counter<long> UsageRequests { get; }
    public Histogram<double> UsageDuration { get; }
    public Counter<long> UsageTrackingFailures { get; }
    public Counter<long> RagRequests { get; }
    public Histogram<double> RagDuration { get; }
    public Histogram<double> RagStageDuration { get; }
    public Histogram<long> RagCandidates { get; }
    public Histogram<long> RagContextChunks { get; }
    public Histogram<long> RagContextWords { get; }
    public Histogram<long> RagContextTokens { get; }
    public Counter<long> RagLlmCalls { get; }
    public Counter<long> RagRefusals { get; }
    public Counter<long> RagPartialResults { get; }
    public Counter<long> RagFailures { get; }
    public Histogram<double> RagCitationCoverage { get; }
    public UpDownCounter<long> RagActiveRequests { get; }
    public Counter<long> AssistantRoutes { get; }
    public Counter<long> AssistantToolCalls { get; }
    public Histogram<double> AssistantDuration { get; }
    public Counter<long> AssistantFeedback { get; }
    public Counter<long> AssistantAuditFailures { get; }
    public Counter<long> AssistantClassifierRequests { get; }
    public Histogram<double> AssistantClassifierDuration { get; }
    public UpDownCounter<long> AssistantClassifierActive { get; }
    public Counter<long> AssistantShadowComparisons { get; }
    public Counter<long> AssistantAnswerCache { get; }

    public PortalMetrics(IServiceScopeFactory scopeFactory, IConfiguration? config = null)
    {
        EmbeddingFailures = _meter.CreateCounter<long>(
            "kp_embedding_failures",
            description: "Embedding attempts that failed (per-article, before backoff retry)");
        McpToolCalls = _meter.CreateCounter<long>("kp_mcp_tool_calls", description: "MCP tool calls by tool and outcome");
        McpToolErrors = _meter.CreateCounter<long>("kp_mcp_tool_errors", description: "Failed MCP tool calls by tool");
        McpToolDuration = _meter.CreateHistogram<double>("kp_mcp_tool_duration_ms", unit: "ms", description: "MCP tool execution duration");
        McpToolOutputBytes = _meter.CreateHistogram<long>("kp_mcp_tool_output_bytes", unit: "By", description: "Serialized MCP tool result size");
        UsageRequests = _meter.CreateCounter<long>("kp_usage_requests", description: "Authenticated usage by channel and outcome");
        UsageDuration = _meter.CreateHistogram<double>("kp_usage_request_duration_ms", unit: "ms", description: "Authenticated request duration by channel and outcome");
        UsageTrackingFailures = _meter.CreateCounter<long>("kp_usage_tracking_failures", description: "Usage events that could not be persisted");
        RagRequests = _meter.CreateCounter<long>("kp_rag_requests", description: "RAG requests by mode and outcome");
        RagDuration = _meter.CreateHistogram<double>("kp_rag_duration_ms", "ms", "End-to-end RAG request duration");
        RagStageDuration = _meter.CreateHistogram<double>("kp_rag_stage_duration_ms", "ms", "RAG stage duration by stage and outcome");
        RagCandidates = _meter.CreateHistogram<long>("kp_rag_candidates", description: "Candidate chunks returned by retrieval");
        RagContextChunks = _meter.CreateHistogram<long>("kp_rag_context_chunks", description: "Chunks supplied to generation");
        RagContextWords = _meter.CreateHistogram<long>("kp_rag_context_words", description: "Approximate words supplied to generation");
        RagContextTokens = _meter.CreateHistogram<long>("kp_rag_context_tokens", description: "Model-calibrated estimated tokens supplied to generation");
        RagLlmCalls = _meter.CreateCounter<long>("kp_rag_llm_calls", description: "RAG LLM calls by stage and outcome");
        RagRefusals = _meter.CreateCounter<long>("kp_rag_refusals", description: "RAG insufficient-context responses by mode");
        RagPartialResults = _meter.CreateCounter<long>("kp_rag_partial_results", description: "RAG responses produced from partial stage results");
        RagFailures = _meter.CreateCounter<long>("kp_rag_failures", description: "RAG failures by stage and error type");
        RagCitationCoverage = _meter.CreateHistogram<double>("kp_rag_citation_coverage", description: "Fraction of claims linked to valid evidence");
        RagActiveRequests = _meter.CreateUpDownCounter<long>("kp_rag_active_requests", description: "RAG requests currently inside the process bulkhead");
        AssistantRoutes = _meter.CreateCounter<long>("kp_assistant_routes",
            description: "Assistant route decisions by route and classifier source");
        AssistantToolCalls = _meter.CreateCounter<long>("kp_assistant_tool_calls",
            description: "Bounded read-only assistant tool calls by tool and outcome");
        AssistantDuration = _meter.CreateHistogram<double>("kp_assistant_duration_ms", "ms",
            "End-to-end assistant orchestration duration by route and outcome");
        AssistantFeedback = _meter.CreateCounter<long>("kp_assistant_feedback",
            description: "Assistant feedback by bounded outcome and reason");
        AssistantAuditFailures = _meter.CreateCounter<long>("kp_assistant_audit_failures",
            description: "Assistant interaction audit records that could not be persisted");
        AssistantClassifierRequests = _meter.CreateCounter<long>("kp_assistant_classifier_requests",
            description: "Assistant classifier executions by outcome");
        AssistantClassifierDuration = _meter.CreateHistogram<double>("kp_assistant_classifier_duration_ms", "ms",
            "Assistant classifier execution duration by outcome");
        AssistantClassifierActive = _meter.CreateUpDownCounter<long>("kp_assistant_classifier_active",
            description: "Assistant classifier requests currently holding a concurrency slot");
        AssistantShadowComparisons = _meter.CreateCounter<long>("kp_assistant_shadow_comparisons",
            description: "Asynchronous primary/shadow route comparison outcomes");
        AssistantAnswerCache = _meter.CreateCounter<long>("kp_assistant_answer_cache",
            description: "ACL and corpus-version scoped semantic answer cache outcomes");

        _meter.CreateObservableGauge(
            "kp_pending_embeddings",
            () =>
            {
                if (!(config?.GetValue("Ollama:Enabled", false) ?? false)) return 0;
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                return db.Articles.Count(a => a.Status == "published" && a.IndexedAt == null);
            },
            description: "Published articles waiting to be embedded");
    }
}
