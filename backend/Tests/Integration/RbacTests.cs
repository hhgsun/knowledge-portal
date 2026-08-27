using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace KnowledgePortal.Api.Tests.Integration;

public class RbacTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public RbacTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Admin_CanAccessAdminUsers()
    {
        await AuthenticateAsAdmin();
        var response = await _client.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CannotAccessAdminUsers()
    {
        var token = await RegisterAndGetToken($"viewer-rbac-{Guid.NewGuid():N}@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CannotAccessAnalytics()
    {
        var token = await RegisterAndGetToken($"viewer-analytics-{Guid.NewGuid():N}@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/analytics");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanAccessAnalytics()
    {
        await AuthenticateAsAdmin();
        var response = await _client.GetAsync("/api/analytics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CanCreateArticle()
    {
        var token = await RegisterAndGetToken($"viewer-create-{Guid.NewGuid():N}@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync("/api/articles", new
        {
            title = "Viewer's Article"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CanCreateAndAttachNewTagThroughArticle()
    {
        var token = await RegisterAndGetToken($"viewer-article-tag-{Guid.NewGuid():N}@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var tagName = $"viewer-tag-{Guid.NewGuid():N}";

        var createResponse = await _client.PostAsJsonAsync("/api/articles", new
        {
            title = "Viewer article with a new tag",
            tags = new[] { tagName }
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var article = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/articles/{created.GetProperty("id").GetString()}");
        Assert.Contains(article.GetProperty("tags").EnumerateArray(),
            tag => tag.GetProperty("name").GetString() == tagName);
    }

    [Fact]
    public async Task Viewer_CannotManageTags()
    {
        var token = await RegisterAndGetToken($"viewer-tags-{Guid.NewGuid():N}@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync("/api/tags", new { name = "new-tag" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CannotDeleteSelf()
    {
        await AuthenticateAsAdmin();

        // Get admin user id
        var meResponse = await _client.GetAsync("/api/auth/me");
        var me = await meResponse.Content.ReadFromJsonAsync<JsonElement>();
        var adminId = me.GetProperty("id").GetString();

        var response = await _client.DeleteAsync($"/api/admin/users?id={adminId}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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

    private async Task<string> RegisterAndGetToken(string email)
    {
        await _client.PostAsJsonAsync("/api/auth/register", new
        {
            name = "Test Viewer",
            email,
            password = "password123"
        });
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "password123"
        });
        var body = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }
}
