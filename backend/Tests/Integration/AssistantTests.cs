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
        => Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/assistant", new { message = "VPN nedir?" })).StatusCode);

    [Fact]
    [Trait("Gate", "AssistantRouting")]
    public async Task Assistant_AlwaysReturnsGroundedKnowledgeAnswerWithoutSearchPayload()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var response = await client.PostAsJsonAsync("/api/assistant", new { message = "VPN politikası nedir?" });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Contains(json.GetProperty("toolCalls").EnumerateArray(), item => item.GetString() == "knowledge_rag");
        Assert.False(json.TryGetProperty("results", out _));
        Assert.False(json.TryGetProperty("analytics", out _));
        Assert.False(json.TryGetProperty("searchQueryId", out _));
        Assert.Equal("qwen2.5vl:7b", json.GetProperty("model").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("interactionId").GetString()));
        var tokenUsage = json.GetProperty("tokenUsage");
        Assert.Equal(tokenUsage.GetProperty("inputTokens").GetInt64()
                     + tokenUsage.GetProperty("outputTokens").GetInt64(),
            tokenUsage.GetProperty("totalTokens").GetInt64());
        Assert.Contains(tokenUsage.GetProperty("estimated").ValueKind,
            new[] { JsonValueKind.True, JsonValueKind.False });
    }

    [Fact]
    [Trait("Gate", "AssistantRouting")]
    public async Task LegacyRoutePreviewAndSearchRagSurfacesAreRemoved()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync("/api/assistant/route-preview", new { message = "test" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/search?q=test&type=rag")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync("/api/search/rag-feedback", new { searchQueryId = "x", helpful = true })).StatusCode);
    }

    [Fact]
    public async Task AssistantKillSwitchDoesNotDisableSearch()
    {
        using var disabledFactory = factory.WithWebHostBuilder(builder => builder.UseSetting("Assistant:Enabled", "false"));
        using var disabledClient = disabledFactory.CreateClient();
        await TestHelpers.AuthenticateAsAdminAsync(disabledClient);

        Assert.Equal(HttpStatusCode.NotFound,
            (await disabledClient.PostAsJsonAsync("/api/assistant", new { message = "test" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await disabledClient.GetAsync("/api/search?q=test&type=fulltext")).StatusCode);
    }

    [Fact]
    public async Task CapabilitiesExposeGroundedRagInsteadOfRoutingModes()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var json = await client.GetFromJsonAsync<JsonElement>("/api/capabilities");
        Assert.True(json.GetProperty("enabled").GetBoolean());
        Assert.True(json.GetProperty("groundedRagEnabled").GetBoolean());
        Assert.False(json.TryGetProperty("supportedModes", out _));
        Assert.False(json.TryGetProperty("classifierEnabled", out _));
    }

    [Fact]
    public async Task FeedbackIsPersistedOnAssistantInteractionAndOwnershipProtected()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var assistant = await client.PostAsJsonAsync("/api/assistant", new { message = "VPN politikası nedir?" });
        assistant.EnsureSuccessStatusCode();
        var interactionId = (await assistant.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("interactionId").GetString();

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/assistant/feedback",
            new { interactionId, helpful = false, reason = "wrong_source" })).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var interaction = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
                .AssistantInteractions.FindAsync(interactionId);
            Assert.NotNull(interaction);
            Assert.Equal("wrong_source", interaction.FeedbackReason);
            Assert.False(interaction.Helpful);
            Assert.Equal(RagService.PromptVersion, interaction.RagPromptVersion);
            Assert.Equal(RagService.RetrievalVersion, interaction.RagRetrievalVersion);
            Assert.False(string.IsNullOrWhiteSpace(interaction.RagAnswerHash));
        }

        var summary = await client.GetFromJsonAsync<JsonElement>("/api/admin/rag-evaluations/feedback-summary?days=30");
        Assert.True(summary.GetProperty("total").GetInt32() >= 1);
        Assert.False(summary.TryGetProperty("assistant", out _));

        using var other = factory.CreateClient();
        var token = await RegisterAndGetToken($"assistant-feedback-{Guid.NewGuid():N}@example.com", other);
        other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await other.PostAsJsonAsync("/api/assistant/feedback", new { interactionId, helpful = true })).StatusCode);
    }

    [Fact]
    [Trait("Gate", "AssistantRouting")]
    public async Task AssistantDoesNotFallBackToSearchWhenAiIsDisabled()
    {
        using var disabledFactory = factory.WithWebHostBuilder(builder => builder.UseSetting("Ollama:Enabled", "false"));
        using var disabledClient = disabledFactory.CreateClient();
        await TestHelpers.AuthenticateAsAdminAsync(disabledClient);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await disabledClient.PostAsJsonAsync("/api/assistant", new { message = "VPN politikası nedir?" })).StatusCode);
    }

    [Fact]
    public async Task AssistantRejectsMessageOverConfiguredLimit()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/assistant", new { message = new string('x', 4001) })).StatusCode);
    }

    [Fact]
    public async Task AssistantRejectsModelOutsideDiscoveredCatalog()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var response = await client.PostAsJsonAsync("/api/assistant",
            new { message = "VPN nedir?", model = "not-installed:latest" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not available", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ConversationHistoryIsOwnedAndContextualizesFollowUp()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var created = await client.PostAsync("/api/assistant/conversations", null);
        var conversationId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        (await client.PostAsJsonAsync("/api/assistant", new { message = "VPN politikası nedir?", conversationId }))
            .EnsureSuccessStatusCode();
        var followUp = await client.PostAsJsonAsync("/api/assistant", new { message = "Peki istisnaları?", conversationId });
        followUp.EnsureSuccessStatusCode();
        var body = await followUp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("VPN politikası", body.GetProperty("normalizedQuery").GetString());
        Assert.Contains(body.GetProperty("toolCalls").EnumerateArray(), item =>
            item.GetString()?.StartsWith("query_contextualization:", StringComparison.Ordinal) == true);
        var messages = await client.GetFromJsonAsync<JsonElement>($"/api/assistant/conversations/{conversationId}/messages");
        Assert.Equal(4, messages.GetProperty("messages").GetArrayLength());
    }

    [Fact]
    public async Task StartingSessionConversationPermanentlyReplacesPreviousConversation()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var first = await client.PostAsync("/api/assistant/conversations", null);
        first.EnsureSuccessStatusCode();
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var second = await client.PostAsync("/api/assistant/conversations", null);
        second.EnsureSuccessStatusCode();
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        Assert.NotEqual(firstId, secondId);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/assistant/conversations/{firstId}/messages")).StatusCode);
        var listed = await client.GetFromJsonAsync<JsonElement>("/api/assistant/conversations");
        var conversations = listed.GetProperty("conversations");
        Assert.Single(conversations.EnumerateArray());
        Assert.Equal(secondId, conversations[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task StreamCompletesWithGroundedKnowledgeAnswer()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/assistant/stream")
            { Content = JsonContent.Create(new { message = "VPN politikası nedir?" }) };
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("event: status", body);
        Assert.Contains("event: complete", body);
        Assert.Contains("knowledge_rag", body);
        Assert.Contains("tokenUsage", body);
    }

    private static async Task<string> RegisterAndGetToken(string email, HttpClient targetClient)
    {
        await targetClient.PostAsJsonAsync("/api/auth/register", new { name = "Assistant Viewer", email, password = "password123" });
        var response = await targetClient.PostAsJsonAsync("/api/auth/login", new { email, password = "password123" });
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }
}
