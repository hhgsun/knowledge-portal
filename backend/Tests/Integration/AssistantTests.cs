using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgePortal.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

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
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("searchQueryId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("interactionId").GetString()));
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

    [Fact]
    public async Task Capabilities_ReflectRuntimeAssistantKillSwitch()
    {
        using var disabledFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Assistant:Enabled", "false"));
        using var disabledClient = disabledFactory.CreateClient();
        await TestHelpers.AuthenticateAsAdminAsync(disabledClient);

        var response = await disabledClient.GetAsync("/api/capabilities");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(json.GetProperty("enabled").GetBoolean());
        Assert.True(json.GetProperty("supportedModes").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Feedback_IsPersistedAndOwnershipProtected()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var assistant = await client.PostAsJsonAsync("/api/assistant",
            new { message = "Merhaba" });
        assistant.EnsureSuccessStatusCode();
        var body = await assistant.Content.ReadFromJsonAsync<JsonElement>();
        var interactionId = body.GetProperty("interactionId").GetString();

        var recorded = await client.PostAsJsonAsync("/api/assistant/feedback", new
        {
            interactionId, helpful = false, reason = "wrong_route", correctedRoute = "search"
        });
        Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var interaction = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
                .AssistantInteractions.FindAsync(interactionId);
            Assert.NotNull(interaction);
            Assert.Equal(64, interaction.QueryFingerprint.Length);
            Assert.Equal("wrong_route", interaction.FeedbackReason);
            Assert.Equal("knowledge_search", interaction.CorrectedRoute);
            Assert.False(interaction.Helpful);
        }

        var summary = await client.GetFromJsonAsync<JsonElement>(
            "/api/admin/rag-evaluations/feedback-summary?days=30");
        Assert.True(summary.GetProperty("assistant").GetProperty("total").GetInt32() >= 1);

        using var otherClient = factory.CreateClient();
        var otherToken = await RegisterAndGetToken(
            $"assistant-feedback-{Guid.NewGuid():N}@example.com", otherClient);
        otherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);
        var denied = await otherClient.PostAsJsonAsync("/api/assistant/feedback", new
            { interactionId, helpful = true });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task AnswerRoute_FallsBackToHybridWhenAiIsDisabled()
    {
        using var disabledFactory = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Ollama:Enabled", "false"));
        using var disabledClient = disabledFactory.CreateClient();
        await TestHelpers.AuthenticateAsAdminAsync(disabledClient);

        var response = await disabledClient.PostAsJsonAsync("/api/assistant", new
            { message = "VPN politikası nedir?" });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("knowledge_search", json.GetProperty("route").GetString());
        Assert.Equal("rag_failure_safe_fallback", json.GetProperty("reasonCode").GetString());
        Assert.Equal(2, json.GetProperty("toolCalls").GetArrayLength());
    }

    [Fact]
    public async Task AssistantSearch_DoesNotExposeAnotherUsersDraft()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var title = $"Gizli Taslak {Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/articles", new
        {
            title, contentMarkdown = "yalnız taslakta bulunan benzersiz-gizli-terim", status = "draft"
        });

        using var viewerClient = factory.CreateClient();
        var viewerToken = await RegisterAndGetToken(
            $"assistant-acl-{Guid.NewGuid():N}@example.com", viewerClient);
        viewerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", viewerToken);
        var response = await viewerClient.PostAsJsonAsync("/api/assistant", new
            { message = "benzersiz-gizli-terim dokümanını bul", preferredRoute = "search" });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.DoesNotContain(json.GetProperty("results").EnumerateArray(),
            item => item.GetProperty("title").GetString() == title);
    }

    [Fact]
    public async Task Assistant_RejectsMessageOverConfiguredLimit()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);

        var response = await client.PostAsJsonAsync("/api/assistant",
            new { message = new string('x', 4001) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RoutePreview_IsAdminOnlyAndDoesNotExecuteTools()
    {
        var viewerToken = await RegisterAndGetToken(
            $"assistant-preview-{Guid.NewGuid():N}@example.com");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", viewerToken);
        var denied = await client.PostAsJsonAsync("/api/assistant/route-preview",
            new { message = "VPN politikasını açıkla" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var response = await client.PostAsJsonAsync("/api/assistant/route-preview",
            new { message = "VPN politikasını açıkla" });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("knowledge_answer", json.GetProperty("route").GetString());
        Assert.False(json.TryGetProperty("toolCalls", out _));
    }

    private Task<string> RegisterAndGetToken(string email) => RegisterAndGetToken(email, client);

    private static async Task<string> RegisterAndGetToken(string email, HttpClient targetClient)
    {
        await targetClient.PostAsJsonAsync("/api/auth/register", new
        {
            name = "Assistant Viewer", email, password = "password123"
        });
        var response = await targetClient.PostAsJsonAsync("/api/auth/login", new
        {
            email, password = "password123"
        });
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("token").GetString()!;
    }
}
