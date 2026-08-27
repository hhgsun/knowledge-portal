using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Services;
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
            interactionId, helpful = false, reason = "wrong_route", correctedRoute = "search",
            question = "Merhaba"
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
            Assert.Equal(AssistantRouterService.RoutingPromptVersion, interaction.RoutingPromptVersion);
            Assert.False(string.IsNullOrWhiteSpace(interaction.ClassifierModel));
            Assert.StartsWith("{", interaction.RoutingConfigSnapshotJson);
            Assert.Single(scope.ServiceProvider.GetRequiredService<AppDbContext>()
                .AssistantEvaluationCandidates.Where(x => x.InteractionId == interactionId));
        }

        var candidates = await client.GetFromJsonAsync<JsonElement>(
            "/api/admin/assistant-evaluations/candidates?status=pending");
        var candidate = candidates.GetProperty("candidates").EnumerateArray()
            .Single(x => x.GetProperty("interactionId").GetString() == interactionId);
        var approved = await client.PutAsJsonAsync(
            $"/api/admin/assistant-evaluations/candidates/{candidate.GetProperty("id").GetString()}",
            new { status = "approved", expectedRoute = "knowledge_search" });
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

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

    [Fact]
    public async Task ConversationHistory_IsOwnedAndProvidesBoundedFollowUpContext()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var created = await client.PostAsync("/api/assistant/conversations", null);
        created.EnsureSuccessStatusCode();
        var conversationId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetString()!;
        var first = await client.PostAsJsonAsync("/api/assistant", new
            { message = "Zeplin kalibrasyon dokümanını bul", preferredRoute = "search", conversationId });
        first.EnsureSuccessStatusCode();
        Assert.Equal(conversationId, (await first.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("conversationId").GetString());

        var followUp = await client.PostAsJsonAsync("/api/assistant", new
            { message = "peki detayları?", preferredRoute = "search", conversationId });
        followUp.EnsureSuccessStatusCode();
        var followUpBody = await followUp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Zeplin kalibrasyon", followUpBody.GetProperty("normalizedQuery").GetString());
        var messages = await client.GetFromJsonAsync<JsonElement>(
            $"/api/assistant/conversations/{conversationId}/messages");
        Assert.Equal(4, messages.GetProperty("messages").GetArrayLength());

        using var other = factory.CreateClient();
        var token = await RegisterAndGetToken($"assistant-history-{Guid.NewGuid():N}@example.com", other);
        other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.GetAsync($"/api/assistant/conversations/{conversationId}/messages")).StatusCode);
    }

    [Fact]
    public async Task Stream_UsesSseAndCompletesWithVerifiedResponse()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/assistant/stream")
        {
            Content = JsonContent.Create(new { message = "Merhaba", preferredRoute = "auto" })
        };
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("event: status", body);
        Assert.Contains("event: token", body);
        Assert.Contains("event: complete", body);
        Assert.Contains("general_chat", body);
        var completeBlock = body.Split("\n\n").Single(x => x.StartsWith("event: complete"));
        var completeJson = JsonDocument.Parse(completeBlock.Split('\n')
            .Single(x => x.StartsWith("data: "))[6..]);
        Assert.Equal("general_chat", completeJson.RootElement.GetProperty("route").GetString());
    }

    [Fact]
    public async Task SemanticAnswerCache_DoesNotCacheRejectedGroundingResult()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var unique = Guid.NewGuid().ToString("N")[..8];
        await client.PostAsJsonAsync("/api/articles", new { title = $"Cache Policy {unique}",
            contentMarkdown = $"Cache Policy {unique} requires verified evidence and citations.",
            status = "published" });
        var first = await client.PostAsJsonAsync("/api/assistant", new
            { message = $"Cache Policy {unique} nedir?", preferredRoute = "answer" });
        first.EnsureSuccessStatusCode();
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(firstBody.GetProperty("cacheHit").GetBoolean());

        var second = await client.PostAsJsonAsync("/api/assistant", new
            { message = $"Cache Policy {unique} nedir?", preferredRoute = "answer" });
        second.EnsureSuccessStatusCode();
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(secondBody.GetProperty("cacheHit").GetBoolean());
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
