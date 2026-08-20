using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using KnowledgePortal.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
        Assert.Equal("1.0.0", status.GetProperty("datasetVersion").GetString());
        Assert.Equal(RagService.PromptVersion,
            status.GetProperty("runtimeSnapshot").GetProperty("promptVersion").GetString());
        Assert.True(status.GetProperty("runtimeSnapshot").TryGetProperty("corpusFingerprint", out _));
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

    [Fact]
    public async Task ExpiredRunningEvaluation_IsReclaimedAfterWorkerCrash()
    {
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            var worker = services.SingleOrDefault(x => x.ImplementationType == typeof(RagEvaluationWorker));
            if (worker != null) services.Remove(worker);
        }));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var owner = db.Users.First(x => x.Role == "admin");
        var dataset = new RagEvaluationDataset
        {
            Name = "Recovery " + Guid.NewGuid().ToString("N"),
            CasesJson = "[]",
            ThresholdsJson = "{}"
        };
        var stale = new RagEvaluationRun
        {
            Dataset = dataset,
            RequestedById = owner.Id,
            Status = "running",
            WorkerId = "dead-worker",
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            CompletedCases = 7,
            ResultsJson = "[]",
            CasesSnapshotJson = "[]",
            ThresholdsSnapshotJson = "{}",
            RuntimeSnapshotJson = "{}"
        };
        db.Add(stale);
        await db.SaveChangesAsync();

        var claimed = await scope.ServiceProvider.GetRequiredService<RagEvaluationService>()
            .ClaimNextAsync("recovery-worker", TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.Equal(stale.Id, claimed);
        Assert.Equal("recovery-worker", stale.WorkerId);
        Assert.Equal(1, stale.AttemptCount);
        Assert.Equal(0, stale.CompletedCases);
        Assert.Null(stale.ResultsJson);
    }
}
