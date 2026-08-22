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
    // in detail in Unit/RagServiceTests.cs. This is the end-to-end HTTP wiring smoke test.
    [Fact]
    public async Task Rag_EndToEnd_ReturnsAnswerAndSources()
    {
        await TestHelpers.AuthenticateAsAdminAsync(_client);
        await CreatePublishedArticleAsync("Vpn Kurulum Rehberi Klmx");

        var response = await _client.GetAsync("/api/search?q=vpn%20kurulum%20klmx&type=rag");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Vpn Kurulum Rehberi Klmx", body.GetProperty("answer").GetString());
        Assert.Contains(body.GetProperty("groundingStatus").GetString(),
            new[] { "lexically_grounded", "partially_grounded" });
        Assert.True(body.GetProperty("claimSupportCoverage").GetDouble() > 0);
        var sourceTitles = body.GetProperty("sources").EnumerateArray()
            .Select(s => s.GetProperty("title").GetString())
            .ToList();
        Assert.Contains("Vpn Kurulum Rehberi Klmx", sourceTitles);
        Assert.True(body.TryGetProperty("claims", out _));
        Assert.True(body.TryGetProperty("evidence", out _));
        Assert.True(body.TryGetProperty("citationCoverage", out _));

        await Task.Delay(400); // Prometheus exporter caches scrape output briefly.
        var metrics = await _client.GetStringAsync("/metrics");
        Assert.Contains("kp_rag_requests_total", metrics);
        Assert.Contains("kp_rag_duration_ms_milliseconds_bucket", metrics);
    }

    [Fact]
    public async Task RagFeedback_IsBoundToOwnedRagQueryAndStoresReproducibilityMetadata()
    {
        await TestHelpers.AuthenticateAsAdminAsync(_client);
        await CreatePublishedArticleAsync("Rag Feedback Kaynağı " + Guid.NewGuid().ToString("N"));
        var search = await _client.GetFromJsonAsync<JsonElement>(
            "/api/search?q=rag%20feedback%20kaynağı&type=rag");
        var searchQueryId = search.GetProperty("searchQueryId").GetString()!;

        var response = await _client.PostAsJsonAsync("/api/search/rag-feedback", new
        {
            searchQueryId,
            helpful = false,
            reason = "incomplete"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var record = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .SearchQueries.FindAsync(searchQueryId);
        Assert.NotNull(record);
        Assert.Equal("not_helpful", record.RagFeedback);
        Assert.Equal("incomplete", record.RagFeedbackReason);
        Assert.NotNull(record.RagFeedbackAt);
        Assert.Equal(RagService.PromptVersion, record.RagPromptVersion);
        Assert.NotNull(record.RagIndexProfile);
        Assert.Equal(64, record.RagAnswerHash?.Length);
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

        var response = await _client.GetAsync("/api/search/rag-observability");
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

        var response = await client.GetAsync("/api/search?q=timeout%20doğrulama&type=rag");

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
            (await client.GetAsync("/api/search?q=circuit%20doğrulama&type=rag")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await client.GetAsync("/api/search?q=circuit%20doğrulama&type=rag")).StatusCode);
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
