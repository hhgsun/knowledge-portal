using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgePortal.Api.Services;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace KnowledgePortal.Api.Mcp;

/// <summary>
/// Adapts the portal's domain-oriented tool executor to the official MCP C# SDK.
/// The SDK owns JSON-RPC, protocol negotiation, Streamable HTTP framing and
/// protocol validation; this class owns only portal tool discovery/execution.
/// </summary>
public static class KnowledgePortalMcpServer
{
    public const string ServerName = "knowledge-portal";
    public const string ServerVersion = "3.0.0";

    private static readonly JsonSerializerOptions SchemaJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly IReadOnlyList<Tool> Tools = McpToolExecutor.GetToolDefinitions().Tools
        .Select(ToProtocolTool)
        .ToArray();

    public static ValueTask<ListToolsResult> ListToolsAsync(
        RequestContext<ListToolsRequestParams> request,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(new ListToolsResult
        {
            Tools = [.. Tools],
            TimeToLive = TimeSpan.FromSeconds(30),
            CacheScope = CacheScope.Private
        });
    }

    public static async ValueTask<CallToolResult> CallToolAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken)
    {
        var parameters = request.Params
            ?? throw new McpProtocolException("Missing tool call parameters", McpErrorCode.InvalidParams);
        var toolName = parameters.Name;
        if (string.IsNullOrWhiteSpace(toolName) || !McpToolExecutor.IsKnownTool(toolName))
            throw new McpProtocolException($"Unknown tool: {toolName}", McpErrorCode.InvalidParams);

        JsonElement? arguments = parameters.Arguments is null
            ? null
            : JsonSerializer.SerializeToElement(parameters.Arguments, SchemaJsonOptions);

        var services = request.Services
            ?? throw new InvalidOperationException("MCP tool invocation has no request service scope.");
        var http = services.GetRequiredService<IHttpContextAccessor>().HttpContext
            ?? throw new InvalidOperationException("MCP tool invocation has no active HTTP context.");
        var executor = services.GetRequiredService<McpToolExecutor>();
        var auditService = services.GetRequiredService<McpAuditService>();
        var resilience = services.GetRequiredService<McpResilienceService>();
        var principal = request.User ?? http.User;

        var audit = auditService.Begin(http, toolName, arguments);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await resilience.ExecuteAsync(
                toolName,
                arguments,
                token => executor.ExecuteToolAsync(toolName, arguments, principal, token),
                cancellationToken);

            stopwatch.Stop();
            auditService.Complete(audit, result, stopwatch.ElapsedMilliseconds);
            return ToProtocolResult(result);
        }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
        {
            stopwatch.Stop();
            auditService.Cancelled(audit, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            auditService.Failed(audit, ex, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private static Tool ToProtocolTool(McpToolDefinition definition)
    {
        return new Tool
        {
            Name = definition.Name,
            Description = definition.Description,
            InputSchema = JsonSerializer.SerializeToElement(definition.InputSchema, SchemaJsonOptions),
            OutputSchema = definition.OutputSchema is null
                ? null
                : JsonSerializer.SerializeToElement(definition.OutputSchema, SchemaJsonOptions),
            Annotations = new ToolAnnotations
            {
                ReadOnlyHint = true,
                DestructiveHint = false,
                IdempotentHint = true,
                OpenWorldHint = false
            }
        };
    }

    private static CallToolResult ToProtocolResult(McpToolCallResult result)
    {
        return new CallToolResult
        {
            Content = result.Content
                .Select(content => (ContentBlock)new TextContentBlock { Text = content.Text ?? string.Empty })
                .ToList(),
            StructuredContent = result.StructuredContent is null
                ? null
                : JsonSerializer.SerializeToElement(result.StructuredContent, SchemaJsonOptions),
            IsError = result.IsError
        };
    }
}
