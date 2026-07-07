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

    [Fact]
    public async Task Search_RagPlaceholder_ReturnsStub()
    {
        await AuthenticateAsAdmin();
        var response = await _client.GetAsync("/api/search?q=test&type=rag");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("answer", out _));
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
