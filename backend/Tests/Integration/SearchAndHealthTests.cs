using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace KnowledgePortal.Api.Tests.Integration;

public class HealthCheckTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthCheckTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("healthy", body.GetProperty("status").GetString());
        Assert.True(body.TryGetProperty("timestamp", out _));
        Assert.True(body.TryGetProperty("pendingEmbeddings", out _));
    }

    [Fact]
    public async Task LivenessEndpoint_AlwaysReturns200()
    {
        var response = await _client.GetAsync("/api/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("alive", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task MetricsEndpoint_ReturnsPrometheusText()
    {
        var response = await _client.GetAsync("/metrics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("kp_pending_embeddings", text);
    }
}

public class SearchTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SearchTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Search_RequiresAuth()
    {
        var response = await _client.GetAsync("/api/search?q=test");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Search_RequiresQuery()
    {
        await AuthenticateAsAdmin();
        var response = await _client.GetAsync("/api/search");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_Fulltext_ReturnsResults()
    {
        await AuthenticateAsAdmin();

        // Create a published article
        await _client.PostAsJsonAsync("/api/articles", new
        {
            title = "Searchable Article Title",
            status = "published"
        });

        var response = await _client.GetAsync("/api/search?q=Searchable");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("results", out _));
    }

    // NOTE: Turkish snowball stemming (plural query → singular title, e.g. "toplantılar"
    // → "Toplantı") is a PostgreSQL-only FTS feature and is not reproduced by the
    // Docker-free InMemory substring fallback, so it has no automated test here. It is
    // still exercised in production against real Postgres.

    [Fact]
    public async Task Search_AccentInsensitive_FindsAccentedTitle()
    {
        await AuthenticateAsAdmin();

        await _client.PostAsJsonAsync("/api/articles", new
        {
            title = "Şirket Güvenlik Politikası",
            status = "published"
        });

        // ASCII query must match the accented Turkish title via unaccent
        var response = await _client.GetAsync("/api/search?q=sirket");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var titles = body.GetProperty("results").EnumerateArray()
            .Select(r => r.GetProperty("title").GetString())
            .ToList();
        Assert.Contains("Şirket Güvenlik Politikası", titles);
    }

    [Fact]
    public async Task Search_MultiWordQuery_RequiresAllTerms()
    {
        await AuthenticateAsAdmin();

        await _client.PostAsJsonAsync("/api/articles", new
        {
            title = "Zeplin Kalibrasyon Rehberi",
            status = "published"
        });
        await _client.PostAsJsonAsync("/api/articles", new
        {
            title = "Zeplin Envanter Listesi",
            status = "published"
        });

        // AND semantics: both terms must match — the envanter article shares only "zeplin"
        var response = await _client.GetAsync($"/api/search?q={Uri.EscapeDataString("zeplin kalibrasyon")}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var titles = body.GetProperty("results").EnumerateArray()
            .Select(r => r.GetProperty("title").GetString())
            .ToList();
        Assert.Contains("Zeplin Kalibrasyon Rehberi", titles);
        Assert.DoesNotContain("Zeplin Envanter Listesi", titles);
    }

    [Fact]
    public async Task Search_MultiWordQuery_FallsBackToAnyTermWhenAndFindsNothing()
    {
        await AuthenticateAsAdmin();

        await _client.PostAsJsonAsync("/api/articles", new
        {
            title = "Jeneratör Bakım Talimatı",
            status = "published"
        });

        // "hicbiryerdegecmeyenkelime" matches nothing → AND yields 0 → OR fallback still finds the article
        var response = await _client.GetAsync($"/api/search?q={Uri.EscapeDataString("jeneratör hicbiryerdegecmeyenkelime")}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var titles = body.GetProperty("results").EnumerateArray()
            .Select(r => r.GetProperty("title").GetString())
            .ToList();
        Assert.Contains("Jeneratör Bakım Talimatı", titles);
    }

    [Fact]
    public async Task Search_Pagination_ReturnsTrueTotalAndPages()
    {
        await AuthenticateAsAdmin();

        for (var i = 1; i <= 3; i++)
        {
            await _client.PostAsJsonAsync("/api/articles", new
            {
                title = $"Sayfalamatesti Makalesi {i}",
                status = "published"
            });
        }

        var response = await _client.GetAsync("/api/search?q=sayfalamatesti&limit=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("total").GetInt32() >= 3);
        Assert.Equal(2, body.GetProperty("results").GetArrayLength());
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.True(body.GetProperty("totalPages").GetInt32() >= 2);

        var page2 = await (await _client.GetAsync("/api/search?q=sayfalamatesti&limit=2&page=2"))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, page2.GetProperty("page").GetInt32());
        Assert.True(page2.GetProperty("results").GetArrayLength() >= 1);

        // Page 2 must not repeat page 1's results
        var page1Ids = body.GetProperty("results").EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToHashSet();
        var page2Ids = page2.GetProperty("results").EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToList();
        Assert.DoesNotContain(page2Ids, id => page1Ids.Contains(id));
    }

    [Fact]
    public async Task Search_BodyMatch_ReturnsSnippetWithContext()
    {
        await AuthenticateAsAdmin();

        var filler = string.Join(" ", Enumerable.Repeat("dolgu kelimeleri burada devam ediyor", 20));
        await _client.PostAsJsonAsync("/api/articles", new
        {
            title = "Snippet Deneme Dokümanı",
            excerpt = "Bu özette aranan kelime yok",
            contentMarkdown = $"{filler} uzaktankumanda cihazının eşleştirme adımları {filler}",
            status = "published"
        });

        var response = await _client.GetAsync("/api/search?q=uzaktankumanda");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var result = body.GetProperty("results").EnumerateArray()
            .FirstOrDefault(r => r.GetProperty("title").GetString() == "Snippet Deneme Dokümanı");
        Assert.NotEqual(JsonValueKind.Undefined, result.ValueKind);

        Assert.True(result.TryGetProperty("snippet", out var snippet));
        Assert.Contains("uzaktankumanda", snippet.GetString());
    }

    [Fact]
    public async Task Search_RejectsRagBecauseAnswerGenerationBelongsToAssistant()
    {
        await AuthenticateAsAdmin();
        var response = await _client.GetAsync("/api/search?q=test&type=rag");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("fulltext, semantic, hybrid", body.GetProperty("error").GetString());
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
