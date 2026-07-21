using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace KnowledgePortal.Api.Tests.Integration;

/// <summary>
/// API-key permission cap end-to-end: keys act with at most editor authority and
/// can never perform destructive deletes; all read/create/edit/publish flows keep working.
/// </summary>
public class ApiKeyPermissionTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _adminClient;

    public ApiKeyPermissionTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _adminClient = factory.CreateClient();
    }

    /// <summary>Creates an API key owned by the seeded admin and returns a client using it.</summary>
    private async Task<HttpClient> CreateApiKeyClientAsync(string keyName)
    {
        await TestHelpers.AuthenticateAsAdminAsync(_adminClient);
        var response = await _adminClient.PostAsJsonAsync("/api/keys", new { name = keyName });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var rawKey = body.GetProperty("key").GetString()!;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", rawKey);
        return client;
    }

    [Fact]
    public async Task ApiKey_CanCreateEditAndPublishArticle()
    {
        using var keyClient = await CreateApiKeyClientAsync("kp-cap-create");

        var createResponse = await keyClient.PostAsJsonAsync("/api/articles", new
        {
            title = "Api Key Yazma Testi",
            status = "draft"
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        var editResponse = await keyClient.PutAsJsonAsync($"/api/articles/{id}", new
        {
            title = "Api Key Yazma Testi Güncel",
            status = "published"
        });
        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);
    }

    [Fact]
    public async Task ApiKey_CannotDeleteArticle()
    {
        using var keyClient = await CreateApiKeyClientAsync("kp-cap-delete-article");

        var createResponse = await keyClient.PostAsJsonAsync("/api/articles", new
        {
            title = "Api Key Silme Denemesi",
            status = "draft"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        var deleteResponse = await keyClient.DeleteAsync($"/api/articles/{id}");
        Assert.Equal(HttpStatusCode.Forbidden, deleteResponse.StatusCode);

        // Owner can still delete via session
        var sessionDelete = await _adminClient.DeleteAsync($"/api/articles/{id}");
        Assert.Equal(HttpStatusCode.OK, sessionDelete.StatusCode);
    }

    [Fact]
    public async Task ApiKey_CannotDeleteTag()
    {
        using var keyClient = await CreateApiKeyClientAsync("kp-cap-delete-tag");

        var tagResponse = await _adminClient.PostAsJsonAsync("/api/tags", new { name = "apikey-silinemez-etiket" });
        var tag = await tagResponse.Content.ReadFromJsonAsync<JsonElement>();
        var tagId = tag.GetProperty("id").GetString();

        var deleteResponse = await keyClient.DeleteAsync($"/api/tags?id={tagId}");
        Assert.Equal(HttpStatusCode.Forbidden, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task ApiKey_CannotDeleteComment()
    {
        using var keyClient = await CreateApiKeyClientAsync("kp-cap-delete-comment");

        var createResponse = await keyClient.PostAsJsonAsync("/api/articles", new
        {
            title = "Api Key Yorum Testi",
            status = "published"
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var articleId = created.GetProperty("id").GetString();

        var commentResponse = await _adminClient.PostAsJsonAsync($"/api/articles/{articleId}/comments",
            new { comment = "test yorumu" });
        Assert.Equal(HttpStatusCode.Created, commentResponse.StatusCode);
        var commentsList = await _adminClient.GetFromJsonAsync<JsonElement>($"/api/articles/{articleId}/comments");
        var commentId = commentsList.GetProperty("comments").EnumerateArray().First()
            .GetProperty("id").GetString();

        var deleteResponse = await keyClient.DeleteAsync($"/api/articles/{articleId}/comments/{commentId}");
        Assert.Equal(HttpStatusCode.Forbidden, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task AdminOwnedApiKey_CannotReachAdminEndpoints()
    {
        using var keyClient = await CreateApiKeyClientAsync("kp-cap-admin");

        var response = await keyClient.GetAsync("/api/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ApiKey_TagAutoCreate_StillWorks()
    {
        using var keyClient = await CreateApiKeyClientAsync("kp-cap-autocreate");

        var createResponse = await keyClient.PostAsJsonAsync("/api/articles", new
        {
            title = "Api Key Oto Etiket Testi",
            status = "draft",
            tags = new[] { "apikey-otomatik-yeni-etiket" }
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        await TestHelpers.AuthenticateAsAdminAsync(_adminClient);
        var tagsResponse = await _adminClient.GetAsync("/api/tags");
        var tags = await tagsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var tagNames = tags.EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Contains("apikey-otomatik-yeni-etiket", tagNames);
    }

    [Fact]
    public async Task ApiKey_ReadEndpoints_Work()
    {
        using var keyClient = await CreateApiKeyClientAsync("kp-cap-read");

        var articles = await keyClient.GetAsync("/api/articles");
        Assert.Equal(HttpStatusCode.OK, articles.StatusCode);

        var search = await keyClient.GetAsync("/api/search?q=test");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);

        var tags = await keyClient.GetAsync("/api/tags");
        Assert.Equal(HttpStatusCode.OK, tags.StatusCode);
    }
}
