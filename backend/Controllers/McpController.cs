using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Diagnostics;
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
/// Usage with Cursor, VS Code, or other MCP clients that support static headers:
///   POST /mcp with JSON-RPC 2.0 body
///   Headers: X-API-Key: kp_xxx OR Authorization: Bearer xxx
/// </summary>
[ApiController]
[Route("mcp")]
[Authorize]
[EnableRateLimiting("mcp")]
public class McpController : ControllerBase
{
    private const long MaxRequestBytes = 262_144;
    private readonly McpToolExecutor _toolExecutor;
    private readonly McpAuditService _audit;
    private readonly McpResilienceService _resilience;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public McpController(McpToolExecutor toolExecutor, McpAuditService audit, McpResilienceService resilience)
    {
        _toolExecutor = toolExecutor;
        _audit = audit;
        _resilience = resilience;
    }

    [HttpPost]
    [RequestSizeLimit(262_144)]
    public async Task<IActionResult> HandleRequest()
    {
        if (Request.ContentLength > MaxRequestBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge);

        if (!IsJsonContentType(Request.ContentType))
            return StatusCode(StatusCodes.Status415UnsupportedMediaType);

        if (!AcceptsSupportedResponseType())
            return StatusCode(StatusCodes.Status406NotAcceptable);

        if (!IsAllowedOrigin())
            return StatusCode(StatusCodes.Status403Forbidden);

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

        var protocolVersion = Request.Headers["MCP-Protocol-Version"].ToString();
        if (!string.IsNullOrWhiteSpace(protocolVersion)
            && !McpConstants.SupportedProtocolVersions.Contains(protocolVersion))
        {
            return JsonRpcErrorResponse(request.Id, JsonRpcErrorCodes.UnsupportedProtocolVersion,
                "Unsupported MCP protocol version",
                new { supported = McpConstants.SupportedProtocolVersions, requested = protocolVersion },
                StatusCodes.Status400BadRequest);
        }

        var modern = protocolVersion == McpConstants.ProtocolVersion;
        if (modern && TryValidateModernRequest(request, out var modernError))
            return modernError!;

        // MCP operations are requests, not fire-and-forget notifications. Do not execute a
        // tool when its result cannot be correlated by the client.
        if (!request.HasId && request.Method != "notifications/initialized")
            return StatusCode(StatusCodes.Status202Accepted);

        // Route to handler based on method
        var result = request.Method switch
        {
            "server/discover" when modern => HandleDiscover(request),
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
            && McpConstants.LegacyProtocolVersions.Contains(requested.GetString()))
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

    private IActionResult HandleDiscover(JsonRpcRequest request)
    {
        return JsonRpcSuccessResponse(request.Id, new
        {
            supportedVersions = new[] { McpConstants.ProtocolVersion },
            capabilities = new McpCapabilities()
        });
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
        if (paramsEl.ValueKind != JsonValueKind.Object)
            return JsonRpcErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams, "Params must be an object");

        // Extract tool name
        if (!paramsEl.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
            return JsonRpcErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams, "Missing or invalid 'name' in params");

        var toolName = nameEl.GetString()!;

        if (!McpToolExecutor.IsKnownTool(toolName))
            return JsonRpcErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams, $"Unknown tool: {toolName}");

        // Extract arguments (optional)
        JsonElement? arguments = paramsEl.TryGetProperty("arguments", out var argsEl)
            ? argsEl
            : null;

        if (arguments is { ValueKind: not JsonValueKind.Object and not JsonValueKind.Null })
            return JsonRpcErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams, "Tool arguments must be an object");

        var audit = _audit.Begin(HttpContext, toolName, arguments);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await _resilience.ExecuteAsync(toolName, arguments,
                token => _toolExecutor.ExecuteToolAsync(toolName, arguments, User, token),
                HttpContext.RequestAborted);
            stopwatch.Stop();
            _audit.Complete(audit, result, stopwatch.ElapsedMilliseconds);
            Response.Headers["X-Trace-Id"] = audit.TraceId;
            return JsonRpcSuccessResponse(request.Id, result);
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            stopwatch.Stop();
            _audit.Cancelled(audit, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _audit.Failed(audit, ex, stopwatch.ElapsedMilliseconds);
            throw;
        }
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
            Result = IsModernRequest() ? BuildModernResult(result) : result
        };
        return new JsonResult(response, _jsonOptions);
    }

    private IActionResult JsonRpcErrorResponse(JsonElement? id, int code, string message,
        object? data = null, int statusCode = StatusCodes.Status200OK)
    {
        var response = new JsonRpcResponse
        {
            Id = id,
            Error = new JsonRpcError { Code = code, Message = message, Data = data }
        };
        return new JsonResult(response, _jsonOptions) { StatusCode = statusCode };
    }

    private bool IsModernRequest() =>
        Request.Headers["MCP-Protocol-Version"].ToString() == McpConstants.ProtocolVersion;

    private object BuildModernResult(object result)
    {
        var node = JsonSerializer.SerializeToNode(result, _jsonOptions) as JsonObject ?? new JsonObject();
        node["resultType"] = "complete";
        node["_meta"] = new JsonObject
        {
            ["io.modelcontextprotocol/serverInfo"] = new JsonObject
            {
                ["name"] = McpConstants.ServerName,
                ["version"] = McpConstants.ServerVersion
            }
        };

        var method = HttpContext.Items["McpMethod"] as string;
        if (method is "server/discover" or "tools/list")
        {
            node["ttlMs"] = 30_000;
            node["cacheScope"] = "private";
        }

        return node;
    }

    private bool TryValidateModernRequest(JsonRpcRequest request, out IActionResult? error)
    {
        HttpContext.Items["McpMethod"] = request.Method;

        var methodHeader = Request.Headers["Mcp-Method"].ToString();
        if (string.IsNullOrWhiteSpace(methodHeader) || methodHeader != request.Method)
        {
            error = JsonRpcErrorResponse(request.Id, JsonRpcErrorCodes.HeaderMismatch,
                "Mcp-Method header is missing or does not match the request method",
                new { header = methodHeader, body = request.Method }, StatusCodes.Status400BadRequest);
            return true;
        }

        if (request.Params is not { ValueKind: JsonValueKind.Object } parameters
            || !parameters.TryGetProperty("_meta", out var meta)
            || meta.ValueKind != JsonValueKind.Object
            || !meta.TryGetProperty("io.modelcontextprotocol/protocolVersion", out var metaVersion)
            || metaVersion.ValueKind != JsonValueKind.String
            || metaVersion.GetString() != McpConstants.ProtocolVersion)
        {
            error = JsonRpcErrorResponse(request.Id, JsonRpcErrorCodes.HeaderMismatch,
                "Modern MCP requests must carry matching protocol metadata",
                new { expected = McpConstants.ProtocolVersion }, StatusCodes.Status400BadRequest);
            return true;
        }

        if (request.Method == "tools/call")
        {
            var name = parameters.TryGetProperty("name", out var nameElement)
                && nameElement.ValueKind == JsonValueKind.String ? nameElement.GetString() : null;
            var nameHeader = Request.Headers["Mcp-Name"].ToString();
            if (string.IsNullOrWhiteSpace(nameHeader) || nameHeader != name)
            {
                error = JsonRpcErrorResponse(request.Id, JsonRpcErrorCodes.HeaderMismatch,
                    "Mcp-Name header is missing or does not match the requested tool",
                    new { header = nameHeader, body = name }, StatusCodes.Status400BadRequest);
                return true;
            }
        }

        error = null;
        return false;
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
