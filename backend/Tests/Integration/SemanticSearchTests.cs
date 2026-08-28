using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KnowledgePortal.Api.Tests.Integration;

/// <summary>
/// Exercises the semantic / hybrid / RAG *endpoints* end-to-end against the fake
/// vector search (Docker-free — see FakeVectorSearchService). Ranking fidelity of the
/// real pgvector similarity is not covered here (needs Postgres); RAG logic is covered
/// in Unit/RagServiceTests.cs.
/// </summary>
public class SemanticSearchTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SemanticSearchTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> CreatePublishedArticleAsync(string title, string? bodyText = null, string[]? tags = null)
    {
        var response = await _client.PostAsJsonAsync("/api/articles", new
        {
            title,
            status = "published",
            contentMarkdown = bodyText,
            tags
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task SemanticSearch_RanksRelevantArticleFirst_ExcludesUnrelated()
    {
        await TestHelpers.AuthenticateAsAdminAsync(_client);
        await CreatePublishedArticleAsync("Kubernetes Deployment Xqzw");
        await CreatePublishedArticleAsync("Makarna Pişirme Tarifi Vwyu");

        var response = await _client.GetAsync("/api/search?q=kubernetes%20deployment%20xqzw&type=semantic");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var titles = body.GetProperty("results").EnumerateArray()
            .Select(r => r.GetProperty("title").GetString())
            .ToList();

        Assert.Equal("Kubernetes Deployment Xqzw", titles.First());
        Assert.DoesNotContain("Makarna Pişirme Tarifi Vwyu", titles);
    }

    [Fact]
    public async Task HybridSearch_ReturnsMatchTypeForResults()
    {
        await TestHelpers.AuthenticateAsAdminAsync(_client);
        await CreatePublishedArticleAsync("Hibrit Arama Deneme Rqpz");

        var response = await _client.GetAsync("/api/search?q=hibrit%20arama%20rqpz&type=hybrid");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var result = body.GetProperty("results").EnumerateArray()
            .First(r => r.GetProperty("title").GetString() == "Hibrit Arama Deneme Rqpz");
        var matchType = result.GetProperty("matchType").GetString();
        Assert.Contains(matchType, new[] { "fulltext", "semantic", "both" });
    }

    [Fact]
    public async Task IndexCoverage_IsModeAware_AndScopedToActiveFilters()
    {
        await TestHelpers.AuthenticateAsAdminAsync(_client);
        var suffix = Guid.NewGuid().ToString("N");
        var tag = "coverage-" + suffix;
        var bodyTerm = "anindagovde" + suffix;
        var articleId = await CreatePublishedArticleAsync(
            "Immediate lexical availability " + suffix,
            "This term exists only in the body: " + bodyTerm,
            [tag]);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var article = (await db.Articles.FindAsync(articleId))!;
            Assert.NotNull(article.FtsIndexedAt); // eager/live lexical availability
            Assert.Null(article.IndexedAt);       // semantic work remains asynchronous
            Assert.Equal("pending", (await db.IndexJobs.FindAsync(articleId))!.Status);
        }

        var query = $"q={bodyTerm}&type=fulltext&tag={Uri.EscapeDataString(tag)}";
        var fullText = await _client.GetFromJsonAsync<JsonElement>($"/api/search?{query}");
        Assert.Contains(fullText.GetProperty("results").EnumerateArray(),
            result => result.GetProperty("id").GetString() == articleId);
        var fullTextCoverage = fullText.GetProperty("indexCoverage");
        Assert.False(fullText.GetProperty("indexingPending").GetBoolean());
        Assert.Equal("fulltext", fullTextCoverage.GetProperty("mode").GetString());
        Assert.Equal(0, fullTextCoverage.GetProperty("fullTextPending").GetInt32());
        Assert.Equal(1, fullTextCoverage.GetProperty("semanticPending").GetInt32());
        Assert.Equal(0, fullTextCoverage.GetProperty("relevantPending").GetInt32());

        var semantic = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/search?q={bodyTerm}&type=semantic&tag={Uri.EscapeDataString(tag)}");
        var semanticCoverage = semantic.GetProperty("indexCoverage");
        Assert.True(semantic.GetProperty("indexingPending").GetBoolean());
        Assert.Equal("semantic", semanticCoverage.GetProperty("mode").GetString());
        Assert.Equal(1, semanticCoverage.GetProperty("relevantPending").GetInt32());

        var hybrid = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/search?q={bodyTerm}&type=hybrid&tag={Uri.EscapeDataString(tag)}");
        Assert.True(hybrid.GetProperty("indexingPending").GetBoolean());
        Assert.Equal(1, hybrid.GetProperty("indexCoverage").GetProperty("relevantPending").GetInt32());
    }

    // RAG logic (filter enforcement, refusal, prompt-injection sanitization) is covered
    // in detail in Unit/RagServiceTests.cs. This is the Assistant HTTP wiring smoke test.
    [Fact]
    public async Task Rag_EndToEnd_ReturnsAnswerAndSources()
    {
        await TestHelpers.AuthenticateAsAdminAsync(_client);
        await CreatePublishedArticleAsync("Vpn Kurulum Rehberi Klmx",
            "Vpn Kurulum Rehberi Klmx, kurumsal VPN profilinin nasıl kurulacağını açıklar.");

        var response = await _client.PostAsJsonAsync("/api/assistant",
            new { message = "vpn kurulum klmx nedir?" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("answer").GetString()));
        var rag = body.GetProperty("rag");
        Assert.False(string.IsNullOrWhiteSpace(rag.GetProperty("groundingStatus").GetString()));
        Assert.Equal(JsonValueKind.Array, rag.GetProperty("sources").ValueKind);
        Assert.True(rag.TryGetProperty("claims", out _));
        Assert.True(rag.TryGetProperty("evidence", out _));
        Assert.True(rag.TryGetProperty("citationCoverage", out _));

        await Task.Delay(400); // Prometheus exporter caches scrape output briefly.
        var metrics = await _client.GetStringAsync("/metrics");
        Assert.Contains("kp_rag_requests_total", metrics);
        Assert.Contains("kp_rag_duration_ms_milliseconds_bucket", metrics);
    }

    [Fact]
    public async Task RagFeedback_IsBoundToAssistantInteractionAndStoresReproducibilityMetadata()
    {
        await TestHelpers.AuthenticateAsAdminAsync(_client);
        await CreatePublishedArticleAsync("Rag Feedback Kaynağı " + Guid.NewGuid().ToString("N"));
        var answerResponse = await _client.PostAsJsonAsync("/api/assistant",
            new { message = "rag feedback kaynağı nedir?" });
        answerResponse.EnsureSuccessStatusCode();
        var answer = await answerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var interactionId = answer.GetProperty("interactionId").GetString()!;

        var response = await _client.PostAsJsonAsync("/api/assistant/feedback", new
        {
            interactionId,
            helpful = false,
            reason = "incomplete"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var record = await database.AssistantInteractions.FindAsync(interactionId);
        Assert.NotNull(record);
        Assert.False(record.Helpful);
        Assert.Equal("incomplete", record.FeedbackReason);
        Assert.NotNull(record.FeedbackAt);
        Assert.Equal(RagService.PromptVersion, record.RagPromptVersion);
        Assert.Equal(RagService.RetrievalVersion, record.RagRetrievalVersion);
        Assert.Equal("local-deterministic-v1", record.RagReranker);
        Assert.NotNull(record.RagIndexProfile);
        Assert.Equal(64, record.RagAnswerHash?.Length);
        Assert.DoesNotContain(database.SearchQueries, query => query.SearchType == "rag");

        var summary = await _client.GetFromJsonAsync<JsonElement>(
            "/api/admin/rag-evaluations/feedback-summary?days=30");
        Assert.True(summary.GetProperty("total").GetInt32() >= 1);
        Assert.True(summary.GetProperty("notHelpful").GetInt32() >= 1);
    }

    [Fact]
    public async Task RagDebug_ReturnsQueryPlanCandidatesAndExactSelectedContextWithoutGeneration()
    {
        await TestHelpers.AuthenticateAsAdminAsync(_client);
        await CreatePublishedArticleAsync("VPN Debug Kaynağı", "VPN sertifika yenileme adımları burada açıklanır.");

        var response = await _client.GetAsync(
            "/api/admin/rag/debug?q=VPN%20kurulumu%20ve%20sertifika%20yenileme");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("queryPlan").GetProperty("isComplex").GetBoolean());
        Assert.True(body.GetProperty("queryPlan").GetProperty("queries").GetArrayLength() >= 2);
        Assert.True(body.GetProperty("candidates").GetArrayLength() >= 1);
        Assert.True(body.GetProperty("selectedContext").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task EmbeddingStatus_IncludesDimensionsAndFailureList()
    {
        await TestHelpers.AuthenticateAsAdminAsync(_client);

        var response = await _client.GetAsync("/api/search/embedding-status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1024, body.GetProperty("configuredDimensions").GetInt32());
        Assert.True(body.TryGetProperty("failedArticles", out _));
    }

    [Fact]
    public async Task RepairIndexing_ReopensFailedDirtyJobWithoutInvalidatingHealthyIndexes()
    {
        await TestHelpers.AuthenticateAsAdminAsync(_client);
        var articleId = await CreatePublishedArticleAsync("Repair indexing " + Guid.NewGuid().ToString("N"));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var article = (await db.Articles.FindAsync(articleId))!;
            Assert.NotNull(article.FtsIndexedAt);
            Assert.Null(article.IndexedAt);

            var job = (await db.IndexJobs.FindAsync(articleId))!;
            job.Status = "failed";
            job.AttemptCount = 10;
            job.LastError = "model unavailable";
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsync("/api/search/repair-indexing", null);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("articlesRepaired").GetInt32() >= 1);
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repaired = (await verifyDb.IndexJobs.FindAsync(articleId))!;
        Assert.Equal("pending", repaired.Status);
        Assert.Equal(0, repaired.AttemptCount);
        Assert.Null(repaired.LastError);
        var unchanged = (await verifyDb.Articles.FindAsync(articleId))!;
        Assert.NotNull(unchanged.FtsIndexedAt);
        Assert.Null(unchanged.IndexedAt);
    }

    [Fact]
    public async Task RagObservability_ReturnsRuntimeAndMetricContract()
    {
        await TestHelpers.AuthenticateAsAdminAsync(_client);

        var response = await _client.GetAsync("/api/admin/rag/observability");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("kp_rag_", body.GetProperty("metricPrefix").GetString());
        Assert.Equal("KnowledgePortal.Rag", body.GetProperty("activitySource").GetString());
        Assert.True(body.GetProperty("runtime").TryGetProperty("activeRequests", out _));
    }

    [Fact]
    public async Task Rag_GenerationTimeout_PropagatesAsGatewayTimeout()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RagResilience:GenerationTimeoutSeconds", "1");
            builder.UseSetting("RagResilience:AiRetryCount", "0");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IChatClient>();
                services.AddSingleton<IChatClient>(new ControlledChatClient(delay: TimeSpan.FromSeconds(10)));
            });
        });
        using var client = factory.CreateClient();
        await TestHelpers.AuthenticateAsAdminAsync(client);
        await CreatePublishedArticleAsync(client, "Timeout RAG Belgesi", "Timeout doğrulama içeriği.");

        var response = await client.PostAsJsonAsync("/api/assistant",
            new { message = "timeout doğrulama nedir?" });

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
    }

    [Fact]
    public async Task Rag_OpenCircuit_PropagatesAsServiceUnavailable()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RagResilience:CircuitBreakerFailureThreshold", "1");
            builder.UseSetting("RagResilience:AiRetryCount", "0");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IChatClient>();
                services.AddSingleton<IChatClient>(new ControlledChatClient(error: new InvalidOperationException("model failed")));
            });
        });
        using var client = factory.CreateClient();
        await TestHelpers.AuthenticateAsAdminAsync(client);
        await CreatePublishedArticleAsync(client, "Circuit RAG Belgesi", "Circuit doğrulama içeriği.");

        Assert.Equal(HttpStatusCode.InternalServerError,
            (await client.PostAsJsonAsync("/api/assistant", new { message = "circuit doğrulama nedir?" })).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await client.PostAsJsonAsync("/api/assistant", new { message = "circuit doğrulama nedir?" })).StatusCode);
    }

    private static async Task CreatePublishedArticleAsync(HttpClient client, string title, string content)
    {
        var response = await client.PostAsJsonAsync("/api/articles", new
        {
            title,
            status = "published",
            contentMarkdown = content,
            tags = Array.Empty<string>()
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private sealed class ControlledChatClient(TimeSpan? delay = null, Exception? error = null) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (delay != null) await Task.Delay(delay.Value, cancellationToken);
            if (error != null) throw error;
            return new ChatResponse(new ChatMessage(ChatRole.Assistant,
                "{\"answer\":\"Bilgi yok.\",\"claims\":[],\"insufficientContext\":true}"));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }
}
