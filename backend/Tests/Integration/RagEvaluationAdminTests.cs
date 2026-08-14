using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace KnowledgePortal.Api.Tests.Integration;

public class RagEvaluationAdminTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    public RagEvaluationAdminTests(TestWebApplicationFactory factory) { _factory = factory; _client = factory.CreateClient(); }

    [Fact]
    public async Task Admin_CanCreateRunAndReadEvaluationResult()
    {
        await TestHelpers.AuthenticateAsAdminAsync(_client);
        var create = await _client.PostAsJsonAsync("/api/admin/rag-evaluations/datasets", new
        {
            name = "Integration Quality " + Guid.NewGuid().ToString("N"), version = "1.0.0", description = "test",
            cases = new[] { new { id = "vpn", category = "focused", question = "vpn kurulum", expectedSourceSlugs = Array.Empty<string>(), expectedFacts = Array.Empty<string>(), forbiddenFacts = Array.Empty<string>(), expectedRefusal = true, filters = new { tag = Array.Empty<string>(), authorIds = Array.Empty<string>(), contentType = Array.Empty<string>() } } },
            thresholds = new { recallAtK = 0d, mrr = 0d, ndcgAtK = 0d, factCoverage = 0d, citationCoverage = 0d, refusalAccuracy = 0d, forbiddenFactPassRate = 0d, p95LatencyMs = 30000 }
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var dataset = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = dataset.GetProperty("id").GetString();

        var queued = await _client.PostAsync($"/api/admin/rag-evaluations/datasets/{id}/runs", null);
        Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
        var run = await queued.Content.ReadFromJsonAsync<JsonElement>();
        var runId = run.GetProperty("id").GetString();

        JsonElement status = default;
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(100);
            status = await _client.GetFromJsonAsync<JsonElement>($"/api/admin/rag-evaluations/runs/{runId}");
            if (status.GetProperty("status").GetString() is "completed" or "failed") break;
        }
        Assert.Equal("completed", status.GetProperty("status").GetString());
        Assert.Equal(1, status.GetProperty("completedCases").GetInt32());
        Assert.True(status.GetProperty("metrics").TryGetProperty("passed", out _));
    }

    [Fact]
    public async Task Viewer_CannotAccessEvaluationAdministration()
    {
        var email = $"rag-viewer-{Guid.NewGuid():N}@example.com";
        var register = await _client.PostAsJsonAsync("/api/auth/register", new { name = "RAG Viewer", email, password = "Secure123!" });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "Secure123!" });
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/admin/rag-evaluations/datasets")).StatusCode);
    }
}
