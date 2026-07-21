using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace KnowledgePortal.Api.Tests.Integration;

public static class TestHelpers
{
    /// <summary>Logs in as the seeded admin and attaches the Bearer token to the client.</summary>
    public static async Task AuthenticateAsAdminAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "admin@finagotech.com.tr",
            password = "1q2w3E*/"
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Waits until the embedding background service has indexed every published article
    /// (search responses report indexingPending=false). The client must be authenticated.
    /// </summary>
    public static async Task WaitForIndexingAsync(HttpClient client, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync("/api/search?q=indexing-probe");
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (body.TryGetProperty("indexingPending", out var pending) && !pending.GetBoolean())
                return;
            await Task.Delay(250);
        }
        throw new TimeoutException("Embedding indexing did not complete within the timeout");
    }
}
