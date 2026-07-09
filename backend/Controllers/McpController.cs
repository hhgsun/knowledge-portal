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
    public async Task<IActionResult> CallTool()
    {
        try
        {
            using var reader = new StreamReader(Request.Body);
            var jsonBody = await reader.ReadToEndAsync();
            
            using var doc = JsonDocument.Parse(jsonBody);
            var root = doc.RootElement;
            
            var toolName = root.GetProperty("params").GetProperty("name").GetString();
            var arguments = root.GetProperty("params").GetProperty("arguments");
            var id = root.TryGetProperty("id", out var idEl) ? idEl.GetRawText() : "null";
            
            if (string.IsNullOrEmpty(toolName))
                return BadRequest(new { error = "Missing tool name" });

            string result = toolName switch
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

            return Ok(new { result = result, id = id, jsonrpc = "2.0" });
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
