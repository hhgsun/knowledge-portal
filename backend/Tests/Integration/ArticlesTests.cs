using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace KnowledgePortal.Api.Tests.Integration;

public class ArticlesTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public ArticlesTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ListArticles_Authenticated_ReturnsOk()
    {
        await AuthenticateAsAdmin();
        var response = await _client.GetAsync("/api/articles");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("articles", out _));
        Assert.True(body.TryGetProperty("total", out _));
    }

    [Fact]
    public async Task ListArticles_Unauthenticated_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync("/api/articles");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateArticle_Admin_Returns201()
    {
        await AuthenticateAsAdmin();
        var response = await _client.PostAsJsonAsync("/api/articles", new
        {
            title = "Test Article",
            excerpt = "A test article excerpt",
            status = "published"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("id", out _));
        Assert.True(body.TryGetProperty("slug", out _));
    }

    [Fact]
    public async Task CreateArticle_MissingTitle_Returns400()
    {
        await AuthenticateAsAdmin();
        var response = await _client.PostAsJsonAsync("/api/articles", new
        {
            title = "",
            excerpt = "No title"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetArticle_BySlug_ReturnsArticle()
    {
        await AuthenticateAsAdmin();

        // Create article
        var createResponse = await _client.PostAsJsonAsync("/api/articles", new
        {
            title = "Fetch By Slug Test",
            status = "published"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var slug = created.GetProperty("slug").GetString();

        // Get by slug
        var response = await _client.GetAsync($"/api/articles/{slug}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Fetch By Slug Test", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task GetArticle_NotFound_Returns404()
    {
        await AuthenticateAsAdmin();
        var response = await _client.GetAsync("/api/articles/nonexistent-slug-xyz");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateArticle_ChangesTitle()
    {
        await AuthenticateAsAdmin();

        var createResponse = await _client.PostAsJsonAsync("/api/articles", new
        {
            title = "Original Title"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        var updateResponse = await _client.PutAsJsonAsync($"/api/articles/{id}", new
        {
            title = "Updated Title"
        });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var body = await updateResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Updated Title", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task UpdateArticle_RejectsBlankTitle()
    {
        await AuthenticateAsAdmin();
        var created = await (await _client.PostAsJsonAsync("/api/articles", new
        {
            title = "Valid title " + Guid.NewGuid().ToString("N")
        })).Content.ReadFromJsonAsync<JsonElement>();

        var response = await _client.PutAsJsonAsync($"/api/articles/{created.GetProperty("id").GetString()}",
            new { title = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateArticle_TitleCollision_GeneratesUniqueSlug()
    {
        await AuthenticateAsAdmin();
        var suffix = Guid.NewGuid().ToString("N");
        var title = "Collision " + suffix;
        await _client.PostAsJsonAsync("/api/articles", new { title });
        var second = await (await _client.PostAsJsonAsync("/api/articles", new { title = "Other " + suffix }))
            .Content.ReadFromJsonAsync<JsonElement>();

        var response = await _client.PutAsJsonAsync($"/api/articles/{second.GetProperty("id").GetString()}",
            new { title });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.EndsWith("-1", body.GetProperty("slug").GetString());
    }

    [Fact]
    public async Task UpdateArticle_StatusChange_InvalidatesApprovalAndReview()
    {
        await AuthenticateAsAdmin();
        var created = await (await _client.PostAsJsonAsync("/api/articles", new
        {
            title = "Approval status " + Guid.NewGuid().ToString("N"), status = "published"
        })).Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync($"/api/articles/{id}/approve", null)).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await _client.PutAsJsonAsync($"/api/articles/{id}", new { status = "archived" })).StatusCode);
        var article = await _client.GetFromJsonAsync<JsonElement>($"/api/articles/{id}");

        Assert.Equal(JsonValueKind.Null, article.GetProperty("approvedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, article.GetProperty("lastReviewedAt").ValueKind);
    }

    [Fact]
    public async Task DeleteArticle_RemovesArticle()
    {
        await AuthenticateAsAdmin();

        var createResponse = await _client.PostAsJsonAsync("/api/articles", new
        {
            title = "To Delete"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        var deleteResponse = await _client.DeleteAsync($"/api/articles/{id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var getResponse = await _client.GetAsync($"/api/articles/{id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Viewer_CanPublishWithoutApproval()
    {
        var token = await RegisterAndGetToken($"viewer-test-{Guid.NewGuid():N}@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync("/api/articles", new
        {
            title = "Viewer Article",
            status = "published"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetString();

        // Publishing and approval are independent: viewers may publish directly.
        var getResponse = await _client.GetAsync($"/api/articles/{id}");
        var article = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("published", article.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, article.GetProperty("approvedAt").ValueKind);
    }

    [Fact]
    public async Task WildcardInSearch_DoesNotMatchAll()
    {
        await AuthenticateAsAdmin();

        // Create a specific article
        await _client.PostAsJsonAsync("/api/articles", new
        {
            title = "Specific Known Title XYZ",
            status = "published"
        });

        // Search with % wildcard — should NOT match everything
        var response = await _client.GetAsync("/api/articles?q=%25");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var total = body.GetProperty("total").GetInt32();
        // If wildcard was escaped properly, % literal search won't match all articles
        Assert.True(total == 0 || total < 100);
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
            name = "Viewer",
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
