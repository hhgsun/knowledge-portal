using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Mcp;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgePortal.Api.Controllers;

/// <summary>
/// MCP (Model Context Protocol) server endpoint.
/// Implements JSON-RPC 2.0 over HTTP (Streamable HTTP transport).
/// Protocol version: see <see cref="McpConstants.ProtocolVersion"/>
/// 
/// Authentication: X-API-Key header or Bearer token (no OAuth).
/// 
/// Usage with Claude Desktop / Cursor / other MCP clients:
///   POST /mcp with JSON-RPC 2.0 body
///   Headers: X-API-Key: kp_xxx OR Authorization: Bearer xxx
/// </summary>
[ApiController]
[Route("mcp")]
[Authorize]
public class McpController : ControllerBase
{
    private readonly McpToolExecutor _toolExecutor;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public McpController(McpToolExecutor toolExecutor)
    {
        _toolExecutor = toolExecutor;
    }

    [HttpPost]
    public async Task<IActionResult> HandleRequest()
    {
        JsonRpcRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<JsonRpcRequest>(
                Request.Body, _jsonOptions);
        }
        catch (JsonException)
        {
            return JsonRpcErrorResponse(null, JsonRpcErrorCodes.ParseError, "Invalid JSON");
        }

        if (request == null || string.IsNullOrEmpty(request.Method))
            return JsonRpcErrorResponse(null, JsonRpcErrorCodes.InvalidRequest, "Invalid request: missing method");

        // Route to handler based on method
        var result = request.Method switch
        {
            "initialize" => HandleInitialize(request),
            "notifications/initialized" => HandleNotification(request),
            "tools/list" => HandleToolsList(request),
            "tools/call" => await HandleToolCall(request),
            "ping" => HandlePing(request),
            _ => JsonRpcErrorResponse(request.Id, JsonRpcErrorCodes.MethodNotFound, $"Method not found: {request.Method}")
        };

        return result;
    }

    /// <summary>
    /// SSE endpoint for MCP clients that prefer Server-Sent Events transport.
    /// Returns the POST endpoint URL for tool calls.
    /// </summary>
    [HttpGet]
    public IActionResult GetSseEndpoint()
    {
        // For clients that probe GET /mcp to discover transport
        // Return endpoint info as JSON (not SSE stream, since we're stateless)
        return Ok(new
        {
            message = "Knowledge Portal MCP Server",
            transport = "http",
            endpoint = "/mcp",
            method = "POST",
            authentication = new[] { "X-API-Key: kp_xxx", "Authorization: Bearer <token>" },
            protocolVersion = McpConstants.ProtocolVersion,
            serverName = McpConstants.ServerName,
            serverVersion = McpConstants.ServerVersion
        });
    }

    // ─── Method Handlers ───────────────────────────────────────────────

    private IActionResult HandleInitialize(JsonRpcRequest request)
    {
        // Defaults come from McpConstants via McpInitializeResult/McpServerInfo
        var result = new McpInitializeResult();

        return JsonRpcSuccessResponse(request.Id, result);
    }

    private IActionResult HandleNotification(JsonRpcRequest request)
    {
        // Notifications don't require a response per JSON-RPC spec,
        // but since this is HTTP we return empty success
        return Ok();
    }

    private IActionResult HandleToolsList(JsonRpcRequest request)
    {
        var tools = McpToolExecutor.GetToolDefinitions();
        return JsonRpcSuccessResponse(request.Id, tools);
    }

    private async Task<IActionResult> HandleToolCall(JsonRpcRequest request)
    {
        if (request.Params == null)
            return JsonRpcErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams, "Missing params");

        var paramsEl = request.Params.Value;

        // Extract tool name
        if (!paramsEl.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
            return JsonRpcErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams, "Missing or invalid 'name' in params");

        var toolName = nameEl.GetString()!;

        // Extract arguments (optional)
        JsonElement? arguments = paramsEl.TryGetProperty("arguments", out var argsEl)
            ? argsEl
            : null;

        var result = await _toolExecutor.ExecuteToolAsync(toolName, arguments);
        return JsonRpcSuccessResponse(request.Id, result);
    }

    private IActionResult HandlePing(JsonRpcRequest request)
    {
        return JsonRpcSuccessResponse(request.Id, new { });
    }

    // ─── Response Builders ─────────────────────────────────────────────

    private IActionResult JsonRpcSuccessResponse(JsonElement? id, object result)
    {
        var response = new JsonRpcResponse
        {
            Id = id,
            Result = result
        };
        return new JsonResult(response, _jsonOptions);
    }

    private IActionResult JsonRpcErrorResponse(JsonElement? id, int code, string message)
    {
        var response = new JsonRpcResponse
        {
            Id = id,
            Error = new JsonRpcError { Code = code, Message = message }
        };
        return new JsonResult(response, _jsonOptions) { StatusCode = 200 }; // JSON-RPC errors are still HTTP 200
    }
}
