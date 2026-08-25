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
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("analysisError").ValueKind);
    }

    [Fact]
    public async Task Analyze_MultipleFiles_ReportsDamagedFileWithoutFailingOtherDrafts()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        using var body = FileBody("Usable source", "usable.txt");
        var damaged = new ByteArrayContent(Encoding.UTF8.GetBytes("not an Open XML package"));
        damaged.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        body.Add(damaged, "files", "damaged.docx");

        var response = await client.PostAsync("/api/source-imports/analyze", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var drafts = json.GetProperty("drafts");
        Assert.Equal(2, drafts.GetArrayLength());
        Assert.True(drafts[0].GetProperty("parsed").GetBoolean());
        Assert.Equal("damaged.docx", drafts[1].GetProperty("fileName").GetString());
        Assert.False(drafts[1].GetProperty("parsed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, drafts[1].GetProperty("warning").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(drafts[1].GetProperty("analysisError").GetString()));
    }

    [Fact]
    public async Task Analyze_UnsupportedAttachment_ReturnsWarningInsteadOfBlockingError()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        using var body = FileBody("image bytes", "diagram.png");

        var response = await client.PostAsync("/api/source-imports/analyze", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var draft = json.GetProperty("drafts")[0];
        Assert.False(draft.GetProperty("parsed").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(draft.GetProperty("warning").GetString()));
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("analysisError").ValueKind);
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

    [Fact]
    public async Task Commit_InvalidOriginal_RollsBackArticle()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var title = $"Rolled back import {Guid.NewGuid():N}";
        using var body = FileBody("invalid", "source.exe");
        body.Add(new StringContent(JsonSerializer.Serialize(new
        {
            drafts = new[] { new { sourceIndex = 0, title, contentMarkdown = "Should not persist", contentType = "reference", status = "published", tags = Array.Empty<string>(), keepOriginal = true } }
        })), "manifest");

        var response = await client.PostAsync("/api/source-imports/commit", body);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, result.GetProperty("failed").GetInt32());
        var failedItem = result.GetProperty("items")[0];
        Assert.Equal("source.exe", failedItem.GetProperty("fileName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(failedItem.GetProperty("error").GetString()));
        var list = await client.GetStringAsync($"/api/articles?q={Uri.EscapeDataString(title)}");
        Assert.DoesNotContain(title, list);
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
