using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace KnowledgePortal.Api.Tests.Integration;

public sealed class AssistantTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient client;
    private readonly TestWebApplicationFactory factory;

    public AssistantTests(TestWebApplicationFactory factory)
    {
        this.factory = factory;
        client = factory.CreateClient();
    }

    [Fact]
    public async Task Assistant_RequiresAuthentication()
    {
        var response = await client.PostAsJsonAsync("/api/assistant", new { message = "Merhaba" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Assistant_RoutesSmallTalkWithoutCallingKnowledgeTools()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var response = await client.PostAsJsonAsync("/api/assistant", new { message = "Merhaba" });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("general_chat", json.GetProperty("route").GetString());
        Assert.Equal("deterministic", json.GetProperty("routeSource").GetString());
        Assert.Empty(json.GetProperty("toolCalls").EnumerateArray());
        Assert.Contains("Merhaba", json.GetProperty("answer").GetString());
    }

    [Fact]
    public async Task Assistant_RoutesDocumentDiscoveryThroughSharedHybridSearch()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        await client.PostAsJsonAsync("/api/articles", new
        {
            title = "Zeplin Kalibrasyon Rehberi",
            contentMarkdown = "Zeplin kalibrasyon adımları ve ölçüm değerleri.",
            status = "published"
        });

        var response = await client.PostAsJsonAsync("/api/assistant", new
            { message = "Zeplin kalibrasyon makalelerini bul" });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("knowledge_search", json.GetProperty("route").GetString());
        Assert.Contains(json.GetProperty("toolCalls").EnumerateArray(),
            item => item.GetString() == "knowledge_search");
        Assert.Contains(json.GetProperty("results").EnumerateArray(),
            item => item.GetProperty("title").GetString() == "Zeplin Kalibrasyon Rehberi");
    }

    [Fact]
    public async Task Assistant_AnalyticsRetainsSessionAndPermissionPolicy()
    {
        var token = await RegisterAndGetToken($"assistant-viewer-{Guid.NewGuid():N}@example.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var denied = await client.PostAsJsonAsync("/api/assistant",
            new { message = "Portal istatistiklerini göster" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var allowed = await client.PostAsJsonAsync("/api/assistant",
            new { message = "Portal istatistiklerini göster" });
        allowed.EnsureSuccessStatusCode();
        var json = await allowed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("analytics", json.GetProperty("route").GetString());
        Assert.True(json.GetProperty("analytics").GetProperty("overview")
            .GetProperty("totalArticles").GetInt32() >= 0);

        var keyResponse = await client.PostAsJsonAsync("/api/keys",
            new { name = $"assistant-analytics-{Guid.NewGuid():N}" });
        Assert.Equal(HttpStatusCode.Created, keyResponse.StatusCode);
        var keyJson = await keyResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var keyClient = factory.CreateClient();
        keyClient.DefaultRequestHeaders.Add("X-API-Key", keyJson.GetProperty("key").GetString());
        var keyDenied = await keyClient.PostAsJsonAsync("/api/assistant",
            new { message = "Portal istatistiklerini göster" });
        Assert.Equal(HttpStatusCode.Forbidden, keyDenied.StatusCode);
    }

    [Fact]
    public async Task Assistant_RejectsUnknownPreferredRoute()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var response = await client.PostAsJsonAsync("/api/assistant",
            new { message = "test", preferredRoute = "write_everything" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task Assistant_KillSwitchDoesNotDisableSearch()
    {
        using var disabledFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Assistant:Enabled", "false"));
        using var disabledClient = disabledFactory.CreateClient();
        await TestHelpers.AuthenticateAsAdminAsync(disabledClient);

        var assistant = await disabledClient.PostAsJsonAsync("/api/assistant",
            new { message = "test" });
        var search = await disabledClient.GetAsync("/api/search?q=test&type=fulltext");

        Assert.Equal(HttpStatusCode.NotFound, assistant.StatusCode);
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
    }

    private async Task<string> RegisterAndGetToken(string email)
    {
        await client.PostAsJsonAsync("/api/auth/register", new
        {
            name = "Assistant Viewer", email, password = "password123"
        });
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email, password = "password123"
        });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("token").GetString()!;
    }
}
