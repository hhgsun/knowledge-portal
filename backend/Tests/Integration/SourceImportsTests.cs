using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace KnowledgePortal.Api.Tests.Integration;

public class SourceImportsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient client;
    public SourceImportsTests(TestWebApplicationFactory factory) => client = factory.CreateClient();

    [Fact]
    public async Task Analyze_TextFile_ReturnsEditableMarkdownDraft()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        using var body = FileBody("Kurulum\n\nİkinci paragraf", "kurulum.txt");
        var response = await client.PostAsync("/api/source-imports/analyze", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var draft = json.GetProperty("drafts")[0];
        Assert.True(draft.GetProperty("parsed").GetBoolean());
        Assert.True(draft.GetProperty("keepOriginal").GetBoolean());
        Assert.Contains("Kurulum", draft.GetProperty("contentMarkdown").GetString());
        Assert.Contains("İkinci paragraf", draft.GetProperty("contentMarkdown").GetString());
    }

    [Fact]
    public async Task Commit_TextDraft_CreatesArticleAndOriginalAttachment()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        using var body = FileBody("Kaynak metin", "source.txt");
        body.Add(new StringContent(JsonSerializer.Serialize(new
        {
            drafts = new[] { new { sourceIndex = 0, title = $"Imported source {Guid.NewGuid():N}", contentMarkdown = "Kaynak metin", contentType = "reference", status = "draft", tags = Array.Empty<string>(), keepOriginal = true } }
        })), "manifest");
        var response = await client.PostAsync("/api/source-imports/commit", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, result.GetProperty("created").GetInt32());
        var articleId = result.GetProperty("items")[0].GetProperty("articleId").GetString();
        var attachments = await client.GetFromJsonAsync<JsonElement>($"/api/articles/{articleId}/attachments");
        Assert.Equal("source.txt", attachments.GetProperty("attachments")[0].GetProperty("fileName").GetString());
    }

    private static MultipartFormDataContent FileBody(string value, string name)
    {
        var body = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(value));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        body.Add(file, "files", name);
        return body;
    }
}
