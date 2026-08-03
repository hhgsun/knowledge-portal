using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace KnowledgePortal.Api.Tests.Integration;

public class TagPaginationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public TagPaginationTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task List_WithPagingAndSearch_ReturnsPagedEnvelope()
    {
        await TestHelpers.AuthenticateAsAdminAsync(_client);
        for (var i = 0; i < 5; i++)
            await _client.PostAsJsonAsync("/api/tags", new { name = $"scroll-tag-{i}" });

        var firstResponse = await _client.GetAsync("/api/tags?page=1&limit=3&q=scroll-tag");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var first = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(5, first.GetProperty("total").GetInt32());
        Assert.Equal(1, first.GetProperty("page").GetInt32());
        Assert.Equal(2, first.GetProperty("totalPages").GetInt32());
        Assert.Equal(3, first.GetProperty("tags").GetArrayLength());

        var second = await _client.GetFromJsonAsync<JsonElement>("/api/tags?page=2&limit=3&q=scroll-tag");
        Assert.Equal(2, second.GetProperty("tags").GetArrayLength());
    }

    [Fact]
    public async Task List_WithIds_ReturnsSelectedTagsOutsideCurrentPage()
    {
        await TestHelpers.AuthenticateAsAdminAsync(_client);
        var createdResponse = await _client.PostAsJsonAsync("/api/tags", new { name = "selected-tag-lookup" });
        var created = await createdResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        var result = await _client.GetFromJsonAsync<JsonElement>($"/api/tags?page=1&limit=100&ids={id}");

        Assert.Equal(1, result.GetProperty("total").GetInt32());
        Assert.Equal(id, result.GetProperty("tags")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task List_WithoutPaging_PreservesLegacyArrayResponse()
    {
        await TestHelpers.AuthenticateAsAdminAsync(_client);
        var result = await _client.GetFromJsonAsync<JsonElement>("/api/tags");
        Assert.Equal(JsonValueKind.Array, result.ValueKind);
    }

    [Fact]
    public async Task ArticleCreate_WithNewTagName_CreatesAndAttachesTagOnSave()
    {
        await TestHelpers.AuthenticateAsAdminAsync(_client);
        var tagName = $"deferred-tag-{Guid.NewGuid():N}";

        var articleResponse = await _client.PostAsJsonAsync("/api/articles", new
        {
            title = "Deferred tag article",
            status = "draft",
            tags = new[] { tagName }
        });
        Assert.Equal(HttpStatusCode.Created, articleResponse.StatusCode);
        var articleSummary = await articleResponse.Content.ReadFromJsonAsync<JsonElement>();

        var article = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/articles/{articleSummary.GetProperty("id").GetString()}");
        var attachedNames = article.GetProperty("tags").EnumerateArray()
            .Select(tag => tag.GetProperty("name").GetString());
        Assert.Contains(tagName, attachedNames);

        var tags = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/tags?page=1&limit=30&q={Uri.EscapeDataString(tagName)}");
        Assert.Equal(1, tags.GetProperty("total").GetInt32());
    }
}
