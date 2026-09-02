using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgePortal.Api.Tests.Integration;

/// <summary>
/// Release-gating MCP quality suite. Retrieval runs on deterministic InMemory/fake-vector
/// infrastructure; PostgreSQL stemming and real pgvector ranking require the production smoke suite.
/// </summary>
public class McpQualityGateTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public McpQualityGateTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        McpTestClient.AddAcceptHeaders(_client);
    }

    private async Task<JsonElement> CallAsync(string tool, object arguments)
    {
        var response = await McpTestClient.SendAsync(_client, new
        {
            jsonrpc = "2.0", id = Guid.NewGuid().ToString("N"), method = "tools/call",
            @params = new { name = tool, arguments }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await McpTestClient.ReadEnvelopeAsync(response);
        return envelope.GetProperty("result");
    }

    [Fact]
    [Trait("Gate", "McpConformance")]
    public async Task ToolNotification_IsAcknowledgedWithoutExecution()
    {
        await TestHelpers.AuthenticateAsAdminAsync(_client);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KnowledgePortal.Api.Data.AppDbContext>();
        var before = db.SearchQueries.Count();

        var response = await McpTestClient.SendAsync(_client, new
        {
            jsonrpc = "2.0", method = "tools/call",
            @params = new { name = "search_articles", arguments = new { query = "must-not-execute" } }
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(before, db.SearchQueries.Count());
    }

    [Fact]
    [Trait("Gate", "McpConformance")]
    public async Task NonObjectParams_ReturnsInvalidParams()
    {
        await TestHelpers.AuthenticateAsAdminAsync(_client);
        var response = await McpTestClient.SendAsync(_client,
            new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new[] { "bad" } });
        var body = await McpTestClient.ReadEnvelopeAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(-32602, body.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    [Trait("Gate", "McpSchema")]
    public async Task EveryToolSchema_IsStructurallyCompleteAndClosedAtRoot()
    {
        await TestHelpers.AuthenticateAsAdminAsync(_client);
        var response = await McpTestClient.SendAsync(_client, new { jsonrpc = "2.0", id = 1, method = "tools/list" });
        var body = await McpTestClient.ReadEnvelopeAsync(response);

        foreach (var tool in body.GetProperty("result").GetProperty("tools").EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.GetProperty("name").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(tool.GetProperty("description").GetString()));
            Assert.Equal("object", tool.GetProperty("inputSchema").GetProperty("type").GetString());
            Assert.Equal("object", tool.GetProperty("outputSchema").GetProperty("type").GetString());
            Assert.False(tool.GetProperty("inputSchema").GetProperty("additionalProperties").GetBoolean());
            Assert.True(tool.GetProperty("inputSchema").TryGetProperty("properties", out var inputProperties));
            Assert.True(tool.GetProperty("outputSchema").TryGetProperty("properties", out var outputProperties));
            Assert.True(tool.GetProperty("outputSchema").TryGetProperty("oneOf", out _));
            Assert.All(inputProperties.EnumerateObject(), property =>
                Assert.False(string.IsNullOrWhiteSpace(property.Value.GetProperty("type").GetString())));
            Assert.All(outputProperties.EnumerateObject(), property =>
                Assert.True(property.Value.TryGetProperty("type", out _), property.Name));
        }

        var search = body.GetProperty("result").GetProperty("tools").EnumerateArray()
            .First(tool => tool.GetProperty("name").GetString() == "search_articles");
        var limit = search.GetProperty("inputSchema").GetProperty("properties").GetProperty("limit");
        Assert.Equal(1, limit.GetProperty("minimum").GetInt32());
        Assert.Equal(50, limit.GetProperty("maximum").GetInt32());
        Assert.Equal(4000, search.GetProperty("inputSchema").GetProperty("properties")
            .GetProperty("query").GetProperty("maxLength").GetInt32());

        var ask = body.GetProperty("result").GetProperty("tools").EnumerateArray()
            .First(tool => tool.GetProperty("name").GetString() == "ask_knowledge");
        Assert.Equal(4000, ask.GetProperty("inputSchema").GetProperty("properties")
            .GetProperty("question").GetProperty("maxLength").GetInt32());
        var answerProfile = ask.GetProperty("inputSchema").GetProperty("properties")
            .GetProperty("answer_profile");
        Assert.Equal("balanced", answerProfile.GetProperty("default").GetString());
        Assert.Equal(["compact", "balanced", "comprehensive"], answerProfile.GetProperty("enum")
            .EnumerateArray().Select(value => value.GetString()!).ToArray());
        Assert.Equal(50, ask.GetProperty("inputSchema").GetProperty("properties")
            .GetProperty("scope").GetProperty("properties").GetProperty("tags")
            .GetProperty("maxItems").GetInt32());
    }

    [Fact]
    [Trait("Gate", "McpSchema")]
    public async Task EveryToolValidationError_UsesAdvertisedStructuredErrorContract()
    {
        await TestHelpers.AuthenticateAsAdminAsync(_client);
        var invalidCalls = new Dictionary<string, object>
        {
            ["search_articles"] = new { },
            ["ask_knowledge"] = new { },
            ["get_article"] = new { },
            ["list_articles"] = new { limit = "invalid" },
            ["list_tags"] = new { unexpected = true },
            ["get_portal_info"] = new { unexpected = true },
            ["get_project_context"] = new { },
            ["get_integration_guidance"] = new { },
            ["find_authoritative_content"] = new { },
            ["compare_sources"] = new { },
            ["get_recent_changes"] = new { unexpected = true }
        };

        foreach (var (tool, arguments) in invalidCalls)
        {
            var result = await CallAsync(tool, arguments);
            Assert.True(result.GetProperty("isError").GetBoolean(), tool);
            var structured = result.GetProperty("structuredContent");
            var error = structured.GetProperty("error");
            Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("code").GetString()), tool);
            Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()), tool);
            Assert.Contains(error.GetProperty("retryable").ValueKind,
                new[] { JsonValueKind.True, JsonValueKind.False });
            using var compatibility = JsonDocument.Parse(
                result.GetProperty("content")[0].GetProperty("text").GetString()!);
            Assert.Equal(structured.GetRawText(), compatibility.RootElement.GetRawText());
        }
    }

    [Fact]
    [Trait("Gate", "GoldenRetrieval")]
    public async Task GoldenTechnicalQueries_MeetRecallAndExclusionContract()
    {
        await TestHelpers.AuthenticateAsAdminAsync(_client);
        var cases = new[]
        {
            new GoldenCase("API anahtarı vault gkapi", "Golden API Key Guide Gkapi", "Golden Pasta Recipe Gkfood",
                "API anahtarını secret manager veya vault içinde saklayın."),
            new GoldenCase("kubernetes deployment rollout gkkube", "Golden Kubernetes Runbook Gkkube", "Golden Vacation Policy Gkhr",
                "Kubernetes deployment rollout ve rollback adımları."),
            new GoldenCase("vpn sertifika kurulumu gkvpn", "Golden VPN Setup Gkvpn", "Golden Finance Report Gkfin",
                "VPN istemci sertifikası kurulum rehberi.")
        };
        foreach (var item in cases)
        {
            await CreateAsync(item.ExpectedTitle, item.Content);
            await CreateAsync(item.ForbiddenTitle, "Bu içerik farklı bir konu hakkındadır.");
        }

        var passed = 0;
        foreach (var item in cases)
        {
            var result = await CallAsync("search_articles", new { query = item.Query, type = "hybrid", limit = 5 });
            var structured = result.GetProperty("structuredContent");
            var titles = structured.GetProperty("results").EnumerateArray().Select(r => r.GetProperty("title").GetString()).ToList();
            Assert.Contains(item.ExpectedTitle, titles);
            Assert.DoesNotContain(item.ForbiddenTitle, titles);
            var expected = structured.GetProperty("results").EnumerateArray().First(r => r.GetProperty("title").GetString() == item.ExpectedTitle);
            Assert.True(expected.GetProperty("evidenceAvailable").GetBoolean());
            Assert.True(expected.TryGetProperty("governance", out _));
            Assert.True(expected.TryGetProperty("securityAssessment", out _));
            passed++;
        }
        Assert.Equal(1d, passed / (double)cases.Length); // recall@5 gate for deterministic corpus
    }

    [Fact]
    [Trait("Gate", "PublishedCorpus")]
    public async Task McpApiKey_SearchesPublishedCorpusAcrossCreatorKeys()
    {
        await TestHelpers.AuthenticateAsAdminAsync(_client);
        var key1 = await CreateKeyClientAsync("quality-key-one");
        var key2 = await CreateKeyClientAsync("quality-key-two");
        await key1.PostAsJsonAsync("/api/articles", new { title = "Key One Isolation Qiso", status = "published" });
        await key2.PostAsJsonAsync("/api/articles", new { title = "Key Two Isolation Qiso", status = "published" });

        var response = await McpTestClient.SendAsync(key1, new
        {
            jsonrpc = "2.0", id = 1, method = "tools/call",
            @params = new { name = "search_articles", arguments = new { query = "qiso" } }
        });
        var envelope = await McpTestClient.ReadEnvelopeAsync(response);
        Assert.True(envelope.TryGetProperty("result", out var result), envelope.GetRawText());
        Assert.True(result.TryGetProperty("structuredContent", out var structured), result.GetRawText());
        var titles = structured.GetProperty("results")
            .EnumerateArray().Select(a => a.GetProperty("title").GetString()).ToList();

        Assert.Contains("Key One Isolation Qiso", titles);
        Assert.Contains("Key Two Isolation Qiso", titles);
    }

    [Fact]
    [Trait("Gate", "McpConcurrency")]
    public async Task ParallelReadOnlyClients_AllCompleteWithCorrelatableResponses()
    {
        await TestHelpers.AuthenticateAsAdminAsync(_client);
        var calls = Enumerable.Range(1, 20).Select(async id =>
        {
            var response = await McpTestClient.SendAsync(_client, new
            {
                jsonrpc = "2.0", id, method = "tools/call",
                @params = new { name = "list_tags", arguments = new { } }
            });
            var body = await McpTestClient.ReadEnvelopeAsync(response);
            return (response, body);
        });

        var responses = await Task.WhenAll(calls);
        Assert.All(responses, item =>
        {
            Assert.Equal(HttpStatusCode.OK, item.response.StatusCode);
            Assert.True(item.response.Headers.TryGetValues("X-Trace-Id", out var traceIds));
            Assert.False(string.IsNullOrWhiteSpace(traceIds.Single()));
            Assert.True(item.body.GetProperty("result").TryGetProperty("structuredContent", out _));
        });
        Assert.Equal(20, responses.Select(item => item.body.GetProperty("id").GetInt32()).Distinct().Count());
    }

    private async Task CreateAsync(string title, string content) =>
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/articles",
            new { title, contentMarkdown = content, status = "published" })).StatusCode);

    private async Task<HttpClient> CreateKeyClientAsync(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/keys", new { name });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", body.GetProperty("key").GetString());
        McpTestClient.AddAcceptHeaders(client);
        return client;
    }

    private sealed record GoldenCase(string Query, string ExpectedTitle, string ForbiddenTitle, string Content);
}
