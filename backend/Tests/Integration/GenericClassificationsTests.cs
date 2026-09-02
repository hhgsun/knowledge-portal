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
            key = "department", label = "Department", cardinality = "single", sortOrder = 40
        });
        Assert.Equal(HttpStatusCode.Created, categoryResponse.StatusCode);
        var category = await categoryResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("filter", category.GetProperty("ragBehavior").GetString());
        Assert.Equal(40, category.GetProperty("sortOrder").GetInt32());

        var rejectedBoost = await client.PutAsJsonAsync("/api/lookups/categories", new
        {
            id = category.GetProperty("id").GetString(), ragBehavior = "boost"
        });
        Assert.Equal(HttpStatusCode.BadRequest, rejectedBoost.StatusCode);

        var valueResponse = await client.PostAsJsonAsync("/api/lookups", new
        {
            category = "department", value = "human-resources", label = "Human Resources",
            sortOrder = 20, authorityWeight = 75
        });
        Assert.Equal(HttpStatusCode.Created, valueResponse.StatusCode);
        var lookup = await valueResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(20, lookup.GetProperty("sortOrder").GetInt32());
        Assert.Equal(75, lookup.GetProperty("authorityWeight").GetInt32());

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

        var articleList = await client.GetFromJsonAsync<JsonElement>(
            "/api/articles?facet=department:human-resources");
        Assert.Contains(articleList.GetProperty("articles").EnumerateArray(),
            article => article.GetProperty("id").GetString() == id);

        var bulkExport = await client.GetAsync(
            "/api/bulk/export?format=jsonl&facet=department:human-resources");
        Assert.Equal(HttpStatusCode.OK, bulkExport.StatusCode);
        Assert.Contains("Department classification integration test",
            await bulkExport.Content.ReadAsStringAsync());

        var filtered = await client.GetFromJsonAsync<JsonElement>(
            "/api/search?q=classification&type=fulltext&facet=department:human-resources");
        Assert.Contains(filtered.GetProperty("results").EnumerateArray(),
            result => result.GetProperty("id").GetString() == id);

        var inlineFiltered = await client.GetFromJsonAsync<JsonElement>(
            "/api/search?q=classification%20%2Bdepartment%3Ahuman-resources&type=fulltext");
        Assert.Contains(inlineFiltered.GetProperty("results").EnumerateArray(),
            result => result.GetProperty("id").GetString() == id);

        var unknown = await client.GetFromJsonAsync<JsonElement>(
            "/api/search?q=classification&type=fulltext&facet=department:unknown");
        Assert.Empty(unknown.GetProperty("results").EnumerateArray());
    }

    [Fact]
    public async Task SeededContentTypeCategory_IsAdministratorConfigurable()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var categories = await client.GetFromJsonAsync<JsonElement>("/api/lookups/categories");
        var contentType = categories.EnumerateArray()
            .Single(category => category.GetProperty("key").GetString() == "content_type");
        var id = contentType.GetProperty("id").GetString()!;

        var update = await client.PutAsJsonAsync("/api/lookups/categories", new
        {
            id, cardinality = "multiple", isRequired = false,
            ragBehavior = "none", isActive = false
        });
        var updated = await update.Content.ReadFromJsonAsync<JsonElement>();

        var restore = await client.PutAsJsonAsync("/api/lookups/categories", new
        {
            id, cardinality = "single", isRequired = true,
            ragBehavior = "filter", isActive = true
        });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("multiple", updated.GetProperty("cardinality").GetString());
        Assert.False(updated.GetProperty("isRequired").GetBoolean());
        Assert.Equal("none", updated.GetProperty("ragBehavior").GetString());
        Assert.False(updated.GetProperty("isActive").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
    }

    [Fact]
    public async Task OptionalCategory_DefaultValueCanBeClearedExplicitly()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var categoryResponse = await client.PostAsJsonAsync("/api/lookups/categories", new
        {
            key = "clearable_default", label = "Clearable default"
        });
        Assert.Equal(HttpStatusCode.Created, categoryResponse.StatusCode);
        var category = await categoryResponse.Content.ReadFromJsonAsync<JsonElement>();
        var categoryId = category.GetProperty("id").GetString()!;

        var valueResponse = await client.PostAsJsonAsync("/api/lookups", new
        {
            category = "clearable_default", value = "configured", label = "Configured"
        });
        Assert.Equal(HttpStatusCode.Created, valueResponse.StatusCode);
        var value = await valueResponse.Content.ReadFromJsonAsync<JsonElement>();
        var valueId = value.GetProperty("id").GetString()!;

        var setDefault = await client.PutAsJsonAsync("/api/lookups/categories", new
        {
            id = categoryId, defaultValueId = valueId
        });
        Assert.Equal(HttpStatusCode.OK, setDefault.StatusCode);

        var requiredClear = await client.PutAsJsonAsync("/api/lookups/categories", new
        {
            id = categoryId, isRequired = true, clearDefaultValue = true
        });
        Assert.Equal(HttpStatusCode.BadRequest, requiredClear.StatusCode);

        var clearDefault = await client.PutAsJsonAsync("/api/lookups/categories", new
        {
            id = categoryId, clearDefaultValue = true
        });
        Assert.Equal(HttpStatusCode.OK, clearDefault.StatusCode);
        var updated = await clearDefault.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("defaultValueId").ValueKind);
    }
}
