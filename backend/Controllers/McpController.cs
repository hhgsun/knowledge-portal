using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Mcp;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
[EnableRateLimiting("mcp")]
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
        if (!IsJsonContentType(Request.ContentType))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType);

        if (!AcceptsSupportedResponseType())
            return StatusCode(StatusCodes.Status406NotAcceptable);

        if (!IsAllowedOrigin())
            return StatusCode(StatusCodes.Status403Forbidden);

        if (Request.Headers.TryGetValue("MCP-Protocol-Version", out var headerVersion)
            && !McpConstants.SupportedProtocolVersions.Contains(headerVersion.ToString()))
        {
            return BadRequest(new { error = "Unsupported MCP-Protocol-Version" });
        }

        JsonRpcRequest? request;
        try
        {
            using var document = await JsonDocument.ParseAsync(Request.Body, cancellationToken: HttpContext.RequestAborted);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
                return JsonRpcErrorResponse(null, JsonRpcErrorCodes.InvalidRequest, "JSON-RPC batch requests are not supported by MCP");
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return JsonRpcErrorResponse(null, JsonRpcErrorCodes.InvalidRequest, "Invalid JSON-RPC request");

            request = document.RootElement.Deserialize<JsonRpcRequest>(_jsonOptions);
            if (request != null)
                request.HasId = document.RootElement.TryGetProperty("id", out _);
        }
        catch (JsonException)
        {
            return JsonRpcErrorResponse(null, JsonRpcErrorCodes.ParseError, "Invalid JSON");
        }

        if (request == null || request.Jsonrpc != "2.0" || string.IsNullOrWhiteSpace(request.Method))
            return JsonRpcErrorResponse(null, JsonRpcErrorCodes.InvalidRequest, "Invalid request: missing method");

        if (request.HasId && request.Id is { } id
            && id.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.Null))
            return JsonRpcErrorResponse(null, JsonRpcErrorCodes.InvalidRequest, "Invalid request id");

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

        // JSON-RPC notifications never receive a JSON-RPC response, including unknown methods.
        return request.HasId ? result : StatusCode(StatusCodes.Status202Accepted);
    }

    /// <summary>
    /// Streamable HTTP GET endpoint. This stateless server has no server-initiated
    /// messages, so the transport requires 405 instead of a discovery document.
    /// </summary>
    [HttpGet]
    public IActionResult GetSseEndpoint()
    {
        if (!IsAllowedOrigin())
            return StatusCode(StatusCodes.Status403Forbidden);

        Response.Headers.Allow = "POST";
        return StatusCode(StatusCodes.Status405MethodNotAllowed);
    }

    // ─── Method Handlers ───────────────────────────────────────────────

    private IActionResult HandleInitialize(JsonRpcRequest request)
    {
        // Version negotiation: echo the client's requested version when supported,
        // otherwise answer with our default and let the client decide.
        var result = new McpInitializeResult();
        if (request.Params is { } p
            && p.TryGetProperty("protocolVersion", out var requested)
            && requested.ValueKind == JsonValueKind.String
            && McpConstants.SupportedProtocolVersions.Contains(requested.GetString()))
        {
            result.ProtocolVersion = requested.GetString()!;
        }

        return JsonRpcSuccessResponse(request.Id, result);
    }

    private IActionResult HandleNotification(JsonRpcRequest request)
    {
        // Streamable HTTP: notifications (no response expected) get 202 Accepted, no body
        return StatusCode(StatusCodes.Status202Accepted);
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

    private static bool IsJsonContentType(string? contentType)
        => !string.IsNullOrWhiteSpace(contentType)
           && contentType.Split(';', 2)[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase);

    private bool AcceptsSupportedResponseType()
    {
        var accept = Request.GetTypedHeaders().Accept;
        return accept == null || accept.Count == 0 || accept.Any(value =>
            value.MediaType.Value?.Equals("*/*", StringComparison.OrdinalIgnoreCase) == true
            || value.MediaType.Value?.Equals("application/json", StringComparison.OrdinalIgnoreCase) == true
            || value.MediaType.Value?.Equals("text/event-stream", StringComparison.OrdinalIgnoreCase) == true);
    }

    private bool IsAllowedOrigin()
    {
        var originValue = Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(originValue))
            return true; // Non-browser MCP clients normally omit Origin.

        if (!Uri.TryCreate(originValue, UriKind.Absolute, out var origin))
            return false;

        var requestPort = Request.Host.Port ?? (Request.IsHttps ? 443 : 80);
        return string.Equals(origin.Scheme, Request.Scheme, StringComparison.OrdinalIgnoreCase)
               && string.Equals(origin.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase)
               && origin.Port == requestPort;
    }
}
