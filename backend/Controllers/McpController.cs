using System.Text.Json;
using System.Text.Json.Serialization;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Mcp;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("mcp")]
[Authorize]
public class McpController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly FullTextSearchService _ftsService;

    public McpController(AppDbContext db, FullTextSearchService ftsService)
    {
        _db = db;
        _ftsService = ftsService;
    }
    
    [HttpPost]
    public async Task<IActionResult> HandleMcpRequest()
    {
        try
        {
            using var reader = new StreamReader(Request.Body);
            var jsonBody = await reader.ReadToEndAsync();
            
            using var doc = JsonDocument.Parse(jsonBody);
            var root = doc.RootElement;
            
            var method = root.TryGetProperty("method", out var methodEl) ? methodEl.GetString() : null;
            var id = root.TryGetProperty("id", out var idEl) ? idEl.GetRawText() : "null";
            
            if (string.IsNullOrEmpty(method))
                return BadRequest(new { error = "Missing method" });

            object resultData = method switch
            {
                "initialize" => GetInitializeResponse(),
                "tools/list" => GetToolsListResponse(),
                "tools/call" => await HandleToolCall(root),
                _ => throw new ArgumentException($"Unknown method: {method}")
            };

            return Ok(new { result = resultData, id = id, jsonrpc = "2.0" });
        }
        catch (Exception ex)
        {
            return Ok(new
            {
                error = new { code = -32603, message = ex.Message },
                jsonrpc = "2.0"
            });
        }
    }

    private object GetInitializeResponse()
    {
        return new
        {
            protocolVersion = "2024-11-05",
            capabilities = new
            {
                tools = new { }
            },
            serverInfo = new
            {
                name = "Knowledge Portal MCP Server",
                version = "1.0.0"
            }
        };
    }

    private object GetToolsListResponse()
    {
        return new
        {
            tools = new object[]
            {
                new
                {
                    name = "searchArticles",
                    description = "Full-text search articles in Knowledge Portal",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            query = new { type = "string", description = "Search query (required)" },
                            limit = new { type = "integer", description = "Max results (1-50, default 20)" },
                            tags = new { type = "string", description = "Tag slugs comma-separated" },
                            authors = new { type = "string", description = "Author slugs comma-separated" },
                            contentType = new { type = "string", description = "Content type comma-separated" },
                            includeContent = new { type = "boolean", description = "Include article content (default false)" }
                        },
                        required = new[] { "query" }
                    }
                },
                new
                {
                    name = "getArticle",
                    description = "Get article details by ID or slug",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            idOrSlug = new { type = "string", description = "Article ID or slug (required)" }
                        },
                        required = new[] { "idOrSlug" }
                    }
                },
                new
                {
                    name = "listArticles",
                    description = "List published articles with pagination",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            page = new { type = "integer", description = "Page number (default 1)" },
                            limit = new { type = "integer", description = "Items per page (1-50, default 20)" },
                            contentType = new { type = "string", description = "Content type filter" },
                            tags = new { type = "string", description = "Tag slugs comma-separated" }
                        },
                        required = new string[] { }
                    }
                },
                new
                {
                    name = "listTags",
                    description = "List all tags in Knowledge Portal",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new { },
                        required = new string[] { }
                    }
                },
                new
                {
                    name = "getPortalStats",
                    description = "Get portal statistics",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new { },
                        required = new string[] { }
                    }
                }
            }
        };
    }

    private async Task<object> HandleToolCall(JsonElement root)
    {
        var parameters = root.GetProperty("params");
        var toolName = parameters.GetProperty("name").GetString();
        var arguments = parameters.GetProperty("arguments");

        if (string.IsNullOrEmpty(toolName))
            throw new ArgumentException("Missing tool name");

        return toolName switch
        {
            "searchArticles" => await KnowledgePortalMcpTools.SearchArticles(
                arguments.TryGetProperty("query", out var q) ? q.GetString() ?? "*" : "*",
                arguments.TryGetProperty("limit", out var l) && l.TryGetInt32(out var limit) ? limit : 20,
                arguments.TryGetProperty("tags", out var t) ? t.GetString() : null,
                arguments.TryGetProperty("authors", out var a) ? a.GetString() : null,
                arguments.TryGetProperty("contentType", out var ct) ? ct.GetString() : null,
                arguments.TryGetProperty("includeContent", out var ic) && ic.GetBoolean(),
                _db,
                _ftsService),

            "getArticle" => await KnowledgePortalMcpTools.GetArticle(
                arguments.TryGetProperty("idOrSlug", out var ios) ? ios.GetString() ?? "" : "",
                _db),

            "listArticles" => await KnowledgePortalMcpTools.ListArticles(
                arguments.TryGetProperty("page", out var p) && p.TryGetInt32(out var page) ? page : 1,
                arguments.TryGetProperty("limit", out var l2) && l2.TryGetInt32(out var limit2) ? limit2 : 20,
                arguments.TryGetProperty("contentType", out var ct2) ? ct2.GetString() : null,
                arguments.TryGetProperty("tags", out var t2) ? t2.GetString() : null,
                _db),

            "listTags" => await KnowledgePortalMcpTools.ListTags(_db),

            "getPortalStats" => await KnowledgePortalMcpTools.GetPortalStats(_db),

            _ => throw new ArgumentException($"Unknown tool: {toolName}")
        };
    }
}

public class McpRequest
{
    [JsonPropertyName("jsonrpc")]
    public string? JsonRpc { get; set; }

    [JsonPropertyName("id")]
    public int? Id { get; set; }

    [JsonPropertyName("method")]
    public string? Method { get; set; }

    [JsonPropertyName("params")]
    public McpParams? Params { get; set; }
}

public class McpParams
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public Dictionary<string, object>? Arguments { get; set; }
}
