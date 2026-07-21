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
}
