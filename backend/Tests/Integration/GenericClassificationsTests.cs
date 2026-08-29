using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace KnowledgePortal.Api.Tests.Integration;

public sealed class GenericClassificationsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient client;

    public GenericClassificationsTests(TestWebApplicationFactory factory)
        => client = factory.CreateClient();

    [Fact]
    public async Task CategoryAssignmentAndSearchFacet_WorkEndToEnd()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var categoryResponse = await client.PostAsJsonAsync("/api/lookups/categories", new
        {
            key = "department", label = "Department", cardinality = "single", ragBehavior = "filter"
        });
        Assert.Equal(HttpStatusCode.Created, categoryResponse.StatusCode);

        var valueResponse = await client.PostAsJsonAsync("/api/lookups", new
        {
            category = "department", value = "human-resources", label = "Human Resources"
        });
        Assert.Equal(HttpStatusCode.Created, valueResponse.StatusCode);

        var createResponse = await client.PostAsJsonAsync("/api/articles", new
        {
            title = "Department classification integration test",
            contentMarkdown = "Human resources classification evidence.",
            status = "published",
            classifications = new Dictionary<string, string[]> { ["department"] = ["human-resources"] }
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/articles/{id}");
        Assert.Equal("human-resources",
            detail.GetProperty("classifications").GetProperty("department")[0].GetString());
        Assert.Equal("reference",
            detail.GetProperty("classifications").GetProperty("content_type")[0].GetString());

        var filtered = await client.GetFromJsonAsync<JsonElement>(
            "/api/search?q=classification&type=fulltext&facet=department:human-resources");
        Assert.Contains(filtered.GetProperty("results").EnumerateArray(),
            result => result.GetProperty("id").GetString() == id);

        var unknown = await client.GetFromJsonAsync<JsonElement>(
            "/api/search?q=classification&type=fulltext&facet=department:unknown");
        Assert.Empty(unknown.GetProperty("results").EnumerateArray());
    }
}
