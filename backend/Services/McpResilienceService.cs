using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KnowledgePortal.Api.Mcp;

namespace KnowledgePortal.Api.Services;

public sealed class McpResilienceService(IConfiguration config)
{
    private readonly ResilienceLane _semanticSearch = new(Math.Max(1,
        config.GetValue("Mcp:SemanticConcurrencyLimit", config.GetValue("Mcp:AiConcurrencyLimit", 2))));
    private readonly ResilienceLane _knowledgeAnswer = new(Math.Max(1,
        config.GetValue("Mcp:AnswerConcurrencyLimit", config.GetValue("Mcp:AiConcurrencyLimit", 2))));
    private readonly int _failureThreshold = Math.Max(1, config.GetValue("Mcp:CircuitBreakerFailureThreshold", 3));
    private readonly TimeSpan _breakDuration = TimeSpan.FromSeconds(Math.Max(1, config.GetValue("Mcp:CircuitBreakerSeconds", 30)));
    private readonly int _maxOutputBytes = Math.Max(16_384, config.GetValue("Mcp:MaxOutputBytes", 1_048_576));

    public async Task<McpToolCallResult> ExecuteAsync(string toolName, JsonElement? arguments,
        Func<CancellationToken, Task<McpToolCallResult>> action, CancellationToken requestAborted)
    {
        var lane = LaneFor(toolName, arguments);
        if (lane != null && IsCircuitOpen(lane, out var retryAfter))
            return ResilienceError("circuit_open", "The requested AI capability is temporarily unavailable after repeated failures.", true, retryAfter);

        var acquired = false;
        if (lane != null)
        {
            acquired = await lane.Slots.WaitAsync(0, requestAborted);
            if (!acquired)
                return ResilienceError("server_busy", "The requested AI capability is at capacity.", true, 5);
        }

        var timeoutSeconds = TimeoutSeconds(toolName, arguments);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, timeout.Token);
        try
        {
            var result = await action(linked.Token);
            if (lane != null) UpdateCircuit(lane, IsTransientAiFailure(result));
            return EnforceOutputLimit(result);
        }
        catch (OperationCanceledException) when (!requestAborted.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            if (lane != null) UpdateCircuit(lane, true);
            return ResilienceError("tool_timeout", $"Tool exceeded its {timeoutSeconds}s execution budget.", true, 10);
        }
        finally
        {
            if (acquired) lane!.Slots.Release();
        }
    }

    private int TimeoutSeconds(string toolName, JsonElement? args)
    {
        var mode = GetMode(args);
        var defaultSeconds = toolName switch
        {
            "list_tags" or "get_portal_info" => 3,
            "get_article" or "list_articles" => 5,
            "compare_sources" or "get_recent_changes" => 15,
            "get_project_context" or "find_authoritative_content" => 30,
            "get_integration_guidance" => 30,
            "ask_knowledge" => 120,
            "search_articles" when mode is "semantic" or "hybrid" => 30,
            _ => 5
        };
        return Math.Max(1, config.GetValue($"Mcp:Timeouts:{toolName}:{mode}",
            config.GetValue($"Mcp:Timeouts:{toolName}", defaultSeconds)));
    }

    private McpToolCallResult EnforceOutputLimit(McpToolCallResult result)
    {
        var bytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(result));
        return bytes <= _maxOutputBytes
            ? result
            : ResilienceError("output_too_large", "Tool output exceeded the configured size limit. Reduce limit/include_content or call get_article for selected results.", false, null,
                new JsonObject { ["maxOutputBytes"] = _maxOutputBytes, ["actualOutputBytes"] = bytes });
    }

    private bool IsCircuitOpen(ResilienceLane lane, out int retryAfter)
    {
        lock (lane.CircuitLock)
        {
            retryAfter = Math.Max(1, (int)Math.Ceiling((lane.CircuitOpenUntil - DateTime.UtcNow).TotalSeconds));
            return lane.CircuitOpenUntil > DateTime.UtcNow;
        }
    }

    private void UpdateCircuit(ResilienceLane lane, bool failed)
    {
        lock (lane.CircuitLock)
        {
            if (!failed) { lane.ConsecutiveFailures = 0; lane.CircuitOpenUntil = default; return; }
            if (++lane.ConsecutiveFailures >= _failureThreshold)
            {
                lane.CircuitOpenUntil = DateTime.UtcNow.Add(_breakDuration);
                lane.ConsecutiveFailures = 0;
            }
        }
    }

    private static bool IsTransientAiFailure(McpToolCallResult result)
    {
        if (result.IsError) return true;
        var json = result.StructuredContent?.ToJsonString() ?? "";
        return json.Contains("Semantic search failed", StringComparison.OrdinalIgnoreCase)
               || json.Contains("answer generation failed", StringComparison.OrdinalIgnoreCase)
               || json.Contains("AI arama şu anda kullanılamıyor", StringComparison.OrdinalIgnoreCase);
    }

    private ResilienceLane? LaneFor(string tool, JsonElement? args)
    {
        if (tool == "ask_knowledge") return _knowledgeAnswer;
        return tool is "get_integration_guidance" or "find_authoritative_content"
               || tool == "search_articles" && GetMode(args) is "semantic" or "hybrid"
            ? _semanticSearch
            : null;
    }

    private static string GetMode(JsonElement? args) => args is { ValueKind: JsonValueKind.Object }
        && args.Value.TryGetProperty("type", out var mode) && mode.ValueKind == JsonValueKind.String
            ? mode.GetString()?.ToLowerInvariant() ?? "fulltext" : "fulltext";

    private sealed class ResilienceLane(int concurrencyLimit)
    {
        public SemaphoreSlim Slots { get; } = new(concurrencyLimit);
        public object CircuitLock { get; } = new();
        public int ConsecutiveFailures { get; set; }
        public DateTime CircuitOpenUntil { get; set; }
    }

    public static McpToolCallResult ResilienceError(string code, string message, bool retryable,
        int? retryAfterSeconds, JsonObject? details = null)
    {
        var error = new JsonObject
        {
            ["code"] = code, ["message"] = message, ["retryable"] = retryable,
            ["retryAfterSeconds"] = retryAfterSeconds, ["details"] = details
        };
        var structured = new JsonObject { ["error"] = error };
        return new McpToolCallResult
        {
            IsError = true,
            StructuredContent = structured,
            Content = [new McpContent { Type = "text", Text = structured.ToJsonString() }]
        };
    }
}
