using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KnowledgePortal.Api.Mcp;

namespace KnowledgePortal.Api.Services;

public sealed class McpResilienceService(IConfiguration config)
{
    private readonly SemaphoreSlim _aiSlots = new(Math.Max(1, config.GetValue("Mcp:AiConcurrencyLimit", 2)));
    private readonly object _circuitLock = new();
    private int _consecutiveAiFailures;
    private DateTime _circuitOpenUntil;
    private readonly int _failureThreshold = Math.Max(1, config.GetValue("Mcp:CircuitBreakerFailureThreshold", 3));
    private readonly TimeSpan _breakDuration = TimeSpan.FromSeconds(Math.Max(1, config.GetValue("Mcp:CircuitBreakerSeconds", 30)));
    private readonly int _maxOutputBytes = Math.Max(16_384, config.GetValue("Mcp:MaxOutputBytes", 1_048_576));

    public async Task<McpToolCallResult> ExecuteAsync(string toolName, JsonElement? arguments,
        Func<CancellationToken, Task<McpToolCallResult>> action, CancellationToken requestAborted)
    {
        var aiBound = IsAiBound(toolName, arguments);
        if (aiBound && IsCircuitOpen(out var retryAfter))
            return ResilienceError("circuit_open", "AI search is temporarily unavailable after repeated failures.", true, retryAfter);

        var acquired = false;
        if (aiBound)
        {
            acquired = await _aiSlots.WaitAsync(0, requestAborted);
            if (!acquired)
                return ResilienceError("server_busy", "AI search capacity is currently full.", true, 5);
        }

        var timeoutSeconds = TimeoutSeconds(toolName, arguments);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, timeout.Token);
        try
        {
            var result = await action(linked.Token);
            if (aiBound) UpdateCircuit(IsTransientAiFailure(result));
            return EnforceOutputLimit(result);
        }
        catch (OperationCanceledException) when (!requestAborted.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            if (aiBound) UpdateCircuit(true);
            return ResilienceError("tool_timeout", $"Tool exceeded its {timeoutSeconds}s execution budget.", true, 10);
        }
        finally
        {
            if (acquired) _aiSlots.Release();
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
            "search_articles" when mode == "rag" => 120,
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

    private bool IsCircuitOpen(out int retryAfter)
    {
        lock (_circuitLock)
        {
            retryAfter = Math.Max(1, (int)Math.Ceiling((_circuitOpenUntil - DateTime.UtcNow).TotalSeconds));
            return _circuitOpenUntil > DateTime.UtcNow;
        }
    }

    private void UpdateCircuit(bool failed)
    {
        lock (_circuitLock)
        {
            if (!failed) { _consecutiveAiFailures = 0; _circuitOpenUntil = default; return; }
            if (++_consecutiveAiFailures >= _failureThreshold)
            {
                _circuitOpenUntil = DateTime.UtcNow.Add(_breakDuration);
                _consecutiveAiFailures = 0;
            }
        }
    }

    private static bool IsTransientAiFailure(McpToolCallResult result)
    {
        if (result.IsError) return true;
        var json = result.StructuredContent?.ToJsonString() ?? "";
        return json.Contains("Semantic search failed", StringComparison.OrdinalIgnoreCase)
               || json.Contains("RAG search failed", StringComparison.OrdinalIgnoreCase)
               || json.Contains("AI arama şu anda kullanılamıyor", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAiBound(string tool, JsonElement? args) => tool is
        "get_integration_guidance" or "find_authoritative_content"
        || tool == "search_articles" && GetMode(args) is "semantic" or "hybrid" or "rag";

    private static string GetMode(JsonElement? args) => args is { ValueKind: JsonValueKind.Object }
        && args.Value.TryGetProperty("type", out var mode) && mode.ValueKind == JsonValueKind.String
            ? mode.GetString()?.ToLowerInvariant() ?? "fulltext" : "fulltext";

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
