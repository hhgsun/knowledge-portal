using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace KnowledgePortal.Api.Tests.Integration;

public class McpTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public McpTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Mcp_RequiresAuth()
    {
        var response = await _client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "ping" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Mcp_Initialize_ReturnsProtocolAndServerInfo()
    {
        await AuthenticateAsAdmin();

        var response = await _client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "initialize" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var result = body.GetProperty("result");
        Assert.False(string.IsNullOrEmpty(result.GetProperty("protocolVersion").GetString()));
        Assert.Equal("knowledge-portal", result.GetProperty("serverInfo").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Mcp_ToolsList_ContainsSearchTool()
    {
        await AuthenticateAsAdmin();

        var response = await _client.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 2, method = "tools/list" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var toolNames = body.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .ToList();
        Assert.Contains("search_articles", toolNames);
    }

    [Fact]
    public async Task Mcp_ToolCall_SearchArticles_ReturnsContent()
    {
        await AuthenticateAsAdmin();

        await _client.PostAsJsonAsync("/api/articles", new
        {
            title = "MCP Arama Testi Makalesi",
            status = "published"
        });

        var response = await _client.PostAsJsonAsync("/mcp", new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "tools/call",
            @params = new { name = "search_articles", arguments = new { query = "MCP Arama Testi" } }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var result = body.GetProperty("result");
        if (result.TryGetProperty("isError", out var isError))
            Assert.False(isError.GetBoolean());

        var text = result.GetProperty("content").EnumerateArray().First().GetProperty("text").GetString();
        Assert.Contains("MCP Arama Testi Makalesi", text);
    }

    private async Task AuthenticateAsAdmin()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "admin@finagotech.com.tr",
            password = "1q2w3E*/"
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}
