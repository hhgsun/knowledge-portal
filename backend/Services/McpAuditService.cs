using System.Diagnostics;
using System.Text;
using System.Text.Json;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Mcp;

namespace KnowledgePortal.Api.Services;

public sealed record McpAuditContext(
    string TraceId,
    string ToolName,
    string AuthSource,
    string? UserId,
    string? ApiKeyId,
    string? Client,
    string? ProtocolVersion,
    string ArgumentSummary);

/// <summary>Structured MCP audit events. Raw argument values and result content are never logged.</summary>
public sealed class McpAuditService(ILogger<McpAuditService> logger, PortalMetrics metrics)
{
    public McpAuditContext Begin(HttpContext http, string toolName, JsonElement? arguments)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? http.TraceIdentifier;
        return new McpAuditContext(
            traceId,
            toolName,
            http.User.GetSource(),
            NullIfEmpty(http.User.GetUserId()),
            http.User.GetApiKeyId(),
            Truncate(http.Request.Headers.UserAgent.ToString(), 160),
            Truncate(http.Request.Headers["MCP-Protocol-Version"].ToString(), 20),
            SummarizeArguments(arguments));
    }

    public void Complete(McpAuditContext audit, McpToolCallResult result, long elapsedMs)
    {
        var outcome = result.IsError ? "error" : "success";
        var outputBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(result));
        var tags = new TagList { { "mcp.tool", audit.ToolName }, { "mcp.outcome", outcome }, { "auth.source", audit.AuthSource } };
        metrics.McpToolCalls.Add(1, tags);
        metrics.McpToolDuration.Record(elapsedMs, tags);
        metrics.McpToolOutputBytes.Record(outputBytes, tags);
        if (result.IsError) metrics.McpToolErrors.Add(1, tags);

        logger.LogInformation(
            "MCP audit traceId={TraceId} tool={ToolName} outcome={Outcome} authSource={AuthSource} userId={UserId} apiKeyId={ApiKeyId} client={Client} protocol={ProtocolVersion} arguments={ArgumentSummary} durationMs={DurationMs} outputBytes={OutputBytes}",
            audit.TraceId, audit.ToolName, outcome, audit.AuthSource, audit.UserId, audit.ApiKeyId,
            audit.Client, audit.ProtocolVersion, audit.ArgumentSummary, elapsedMs, outputBytes);
    }

    public void Failed(McpAuditContext audit, Exception exception, long elapsedMs)
    {
        var tags = new TagList { { "mcp.tool", audit.ToolName }, { "mcp.outcome", "exception" }, { "auth.source", audit.AuthSource } };
        metrics.McpToolCalls.Add(1, tags);
        metrics.McpToolErrors.Add(1, tags);
        metrics.McpToolDuration.Record(elapsedMs, tags);
        logger.LogError(exception,
            "MCP audit traceId={TraceId} tool={ToolName} outcome=exception authSource={AuthSource} userId={UserId} apiKeyId={ApiKeyId} client={Client} protocol={ProtocolVersion} arguments={ArgumentSummary} durationMs={DurationMs}",
            audit.TraceId, audit.ToolName, audit.AuthSource, audit.UserId, audit.ApiKeyId,
            audit.Client, audit.ProtocolVersion, audit.ArgumentSummary, elapsedMs);
    }

    internal static string SummarizeArguments(JsonElement? arguments)
    {
        if (arguments is not { ValueKind: JsonValueKind.Object }) return "none";
        var parts = new List<string>();
        foreach (var property in arguments.Value.EnumerateObject().OrderBy(p => p.Name))
        {
            var kind = property.Value.ValueKind.ToString().ToLowerInvariant();
            var detail = property.Value.ValueKind switch
            {
                JsonValueKind.String => $"string:length={property.Value.GetString()?.Length ?? 0}",
                JsonValueKind.Array => $"array:count={property.Value.GetArrayLength()}",
                JsonValueKind.Number => "number",
                JsonValueKind.True or JsonValueKind.False => "boolean",
                _ => kind
            };
            parts.Add($"{property.Name}({detail})");
        }
        return string.Join(',', parts);
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static string? Truncate(string value, int length) => string.IsNullOrWhiteSpace(value) ? null : value[..Math.Min(length, value.Length)];
}
