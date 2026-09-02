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
        Assert.Equal("balanced", json.GetProperty("answerProfile").GetString());
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
        Assert.Contains(".pdf", json.GetProperty("allowedAttachmentExtensions")
            .EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(20, json.GetProperty("maxAttachmentSizeMb").GetInt32());
        Assert.Equal(20, json.GetProperty("maxAttachmentsPerArticle").GetInt32());
        Assert.Equal("balanced", json.GetProperty("defaultAnswerProfile").GetString());
        Assert.Equal(["compact", "balanced", "comprehensive"], json.GetProperty("answerProfiles")
            .EnumerateArray().Select(value => value.GetString()!).ToArray());
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
    public async Task Assistant_NeverUsesCallersOwnDraftAsRagEvidence()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var marker = $"owndraft{Guid.NewGuid():N}";
        var create = await client.PostAsJsonAsync("/api/articles", new
        {
            title = $"Private owner draft {marker}",
            contentMarkdown = $"Confidential draft evidence {marker}",
            status = "draft"
        });
        create.EnsureSuccessStatusCode();
        var articleId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var response = await client.PostAsJsonAsync("/api/assistant", new
            { message = $"What does {marker} say?" });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var rag = json.GetProperty("rag");

        Assert.DoesNotContain(rag.GetProperty("sources").EnumerateArray(),
            source => source.GetProperty("articleId").GetString() == articleId);
        Assert.DoesNotContain(rag.GetProperty("consultedSources").EnumerateArray(),
            source => source.GetProperty("articleId").GetString() == articleId);
        Assert.DoesNotContain(rag.GetProperty("evidence").EnumerateArray(),
            evidence => evidence.GetProperty("articleId").GetString() == articleId);
    }

    [Fact]
    public async Task AssistantApiKey_UsesAllPublishedArticlesAndIgnoresLegacyOnlyOwnContent()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var marker = $"allpublished{Guid.NewGuid():N}";
        var createArticle = await client.PostAsJsonAsync("/api/articles", new
        {
            title = $"Shared published knowledge {marker}",
            contentMarkdown = $"Shared published evidence {marker}",
            status = "published"
        });
        createArticle.EnsureSuccessStatusCode();
        var articleId = (await createArticle.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetString();
        var keyResponse = await client.PostAsJsonAsync("/api/keys", new
            { name = $"assistant-all-published-{marker}" });
        keyResponse.EnsureSuccessStatusCode();
        var rawKey = (await keyResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("key").GetString();

        using var keyClient = factory.CreateClient();
        keyClient.DefaultRequestHeaders.Add("X-API-Key", rawKey);
        var response = await keyClient.PostAsJsonAsync("/api/assistant", new
        {
            message = $"What does {marker} say?",
            onlyOwnContent = true
        });
        response.EnsureSuccessStatusCode();
        var rag = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rag");

        Assert.Contains(rag.GetProperty("sources").EnumerateArray(),
            source => source.GetProperty("articleId").GetString() == articleId);
    }

    [Fact]
    public async Task AssistantValidatesAndReturnsRequestedAnswerProfile()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);

        var invalid = await client.PostAsJsonAsync("/api/assistant",
            new { message = "VPN nedir?", answerProfile = "verbose" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var response = await client.PostAsJsonAsync("/api/assistant",
            new { message = "VPN nedir?", answerProfile = "comprehensive" });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("comprehensive", json.GetProperty("answerProfile").GetString());
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
    public async Task ConversationHistoryCarriesTopicIntoSubjectlessHowToFollowUp()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var created = await client.PostAsync("/api/assistant/conversations", null);
        var conversationId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        (await client.PostAsJsonAsync("/api/assistant", new { message = "MCP nedir?", conversationId }))
            .EnsureSuccessStatusCode();
        var followUp = await client.PostAsJsonAsync("/api/assistant",
            new { message = "nasıl kullanılır?", conversationId });
        followUp.EnsureSuccessStatusCode();
        var body = await followUp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Contains("MCP hakkında", body.GetProperty("normalizedQuery").GetString());
        Assert.Contains(body.GetProperty("toolCalls").EnumerateArray(), item =>
            item.GetString() == "query_contextualization:deterministic_fallback"
            || item.GetString() == "query_contextualization:deterministic_topic_guard");
    }

    [Fact]
    public async Task ConversationPresentationFollowUpReusesGroundedMcpAnswerWithoutNewRetrieval()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var created = await client.PostAsync("/api/assistant/conversations", null);
        var conversationId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetString()!;

        var initial = await client.PostAsJsonAsync("/api/assistant",
            new { message = "MCP nedir?", conversationId });
        initial.EnsureSuccessStatusCode();
        var initialBody = await initial.Content.ReadFromJsonAsync<JsonElement>();

        var followUp = await client.PostAsJsonAsync("/api/assistant",
            new { message = "sırala", conversationId });
        followUp.EnsureSuccessStatusCode();
        var body = await followUp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("list", body.GetProperty("intent").GetString());
        Assert.Equal("ordered_list", body.GetProperty("presentation").GetString());
        Assert.Equal(initialBody.GetProperty("normalizedQuery").GetString(),
            body.GetProperty("normalizedQuery").GetString());
        Assert.Contains(body.GetProperty("toolCalls").EnumerateArray(), item =>
            item.GetString() == "conversation_transform");
        Assert.Equal("ordered_list",
            body.GetProperty("contentBlocks")[0].GetProperty("type").GetString());
        Assert.DoesNotContain("rank'e göre", body.GetProperty("answer").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PresentationCommandWithoutPriorAnswerAsksForClarification()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var created = await client.PostAsync("/api/assistant/conversations", null);
        var conversationId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync("/api/assistant",
            new { message = "tablo yap", conversationId });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("turn_clarification", body.GetProperty("toolCalls")[1].GetString());
        Assert.Contains("Hangi bilgileri", body.GetProperty("answer").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("rag").ValueKind);
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
