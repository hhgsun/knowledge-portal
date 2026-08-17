using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgePortal.Api.Tests.Integration;

public class AnalyticsUsageTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AnalyticsUsageTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Analytics_ReturnsUserIntegrationChannelAndOperationBreakdowns()
    {
        var now = DateTime.UtcNow;
        var user = new User
        {
            Id = "usage-test-user",
            Name = "Usage Editor",
            Slug = "usage-editor",
            Email = "usage-editor@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123"),
            Role = "editor",
            CreatedAt = now,
            UpdatedAt = now
        };
        var key = new ApiKey
        {
            Id = "usage-test-key",
            UserId = user.Id,
            KeyHash = BCrypt.Net.BCrypt.HashPassword("kp_test"),
            KeyPrefix = "test",
            Name = "CI Integration",
            CreatedAt = now
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(user);
            db.ApiKeys.Add(key);
            db.UsageEvents.AddRange(
                Event(user.Id, null, "session", "rest", "GET api/articles", "GET", "success", 200, 40, now.AddHours(-3)),
                Event(user.Id, key.Id, "api-key", "rest", "POST api/articles", "POST", "success", 201, 80, now.AddHours(-2)),
                Event(user.Id, key.Id, "api-key", "mcp", "mcp.search_articles", "POST", "server_error", 500, 120, now.AddHours(-1)),
                Event(user.Id, key.Id, "api-key", "mcp", "mcp.search_articles", "POST", "success", 200, 60, now.AddMinutes(-30)));
            await db.SaveChangesAsync();
        }

        await AuthenticateAsAdmin();
        var response = await _client.GetAsync("/api/analytics?days=7");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var usage = json.GetProperty("usage");

        Assert.Equal(4, usage.GetProperty("totalRequests").GetInt32());
        Assert.Equal(1, usage.GetProperty("activeUsers").GetInt32());
        Assert.Equal(1, usage.GetProperty("activeIntegrations").GetInt32());
        Assert.Equal(1, usage.GetProperty("sessionRequests").GetInt32());
        Assert.Equal(3, usage.GetProperty("integrationRequests").GetInt32());
        Assert.Equal(2, usage.GetProperty("restRequests").GetInt32());
        Assert.Equal(2, usage.GetProperty("mcpCalls").GetInt32());
        Assert.Equal(7, usage.GetProperty("daily").GetArrayLength());

        var userUsage = usage.GetProperty("users").EnumerateArray().Single(x => x.GetProperty("userId").GetString() == user.Id);
        Assert.Equal(4, userUsage.GetProperty("requests").GetInt32());
        Assert.Equal(1, userUsage.GetProperty("writeRequests").GetInt32());
        Assert.Equal(1, userUsage.GetProperty("integrationsUsed").GetInt32());

        var integration = usage.GetProperty("integrations").EnumerateArray().Single();
        Assert.Equal("CI Integration", integration.GetProperty("name").GetString());
        Assert.Equal(3, integration.GetProperty("requests").GetInt32());
        Assert.Equal(2, integration.GetProperty("mcpCalls").GetInt32());
        Assert.Equal(1, integration.GetProperty("errors").GetInt32());
        Assert.Equal("mcp.search_articles", integration.GetProperty("topOperation").GetString());

        var mcpOperation = usage.GetProperty("operations").EnumerateArray()
            .Single(x => x.GetProperty("operation").GetString() == "mcp.search_articles");
        Assert.Equal(1, mcpOperation.GetProperty("uniqueUsers").GetInt32());
        Assert.Equal(1, mcpOperation.GetProperty("uniqueIntegrations").GetInt32());
    }

    private static UsageEvent Event(string userId, string? apiKeyId, string source, string channel,
        string operation, string method, string outcome, int status, long duration, DateTime occurredAt) => new()
    {
        UserId = userId,
        ApiKeyId = apiKeyId,
        AuthSource = source,
        Channel = channel,
        Operation = operation,
        HttpMethod = method,
        Outcome = outcome,
        StatusCode = status,
        DurationMs = duration,
        OccurredAt = occurredAt
    };

    private async Task AuthenticateAsAdmin()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "admin@finagotech.com.tr",
            password = "1q2w3E*/"
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
    }
}
