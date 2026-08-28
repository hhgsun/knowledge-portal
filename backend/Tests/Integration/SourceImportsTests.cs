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
        Assert.False(attachments.GetProperty("attachments")[0].GetProperty("includeInIndex").GetBoolean());
    }

    [Fact]
    public async Task Commit_DamagedSourceWithManualContent_CreatesArticleAndOriginalAttachment()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var title = $"Manual damaged source {Guid.NewGuid():N}";
        using var body = FileBody("not an Open XML package", "damaged.docx");
        body.Add(new StringContent(JsonSerializer.Serialize(new
        {
            drafts = new[] { new { sourceIndex = 0, title, contentMarkdown = "Manually entered content", contentType = "reference", status = "draft", tags = Array.Empty<string>(), keepOriginal = true, originalIncludeInIndex = true } }
        })), "manifest");

        var response = await client.PostAsync("/api/source-imports/commit", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, result.GetProperty("created").GetInt32());
        Assert.Equal(0, result.GetProperty("failed").GetInt32());
        var articleId = result.GetProperty("items")[0].GetProperty("articleId").GetString();
        var article = await client.GetFromJsonAsync<JsonElement>($"/api/articles/{articleId}");
        Assert.Equal("Manually entered content", article.GetProperty("contentMarkdown").GetString());
        var attachments = await client.GetFromJsonAsync<JsonElement>($"/api/articles/{articleId}/attachments");
        Assert.Equal("damaged.docx", attachments.GetProperty("attachments")[0].GetProperty("fileName").GetString());
        Assert.True(attachments.GetProperty("attachments")[0].GetProperty("includeInIndex").GetBoolean());
    }

    [Fact]
    public async Task Commit_DraftWithAdditionalAttachments_AssociatesEveryFileWithArticle()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        using var body = FileBody("Source content", "source.txt");
        var diagram = new ByteArrayContent(Encoding.UTF8.GetBytes("diagram bytes"));
        diagram.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        body.Add(diagram, "attachments", "diagram.png");
        var notes = new ByteArrayContent(Encoding.UTF8.GetBytes("Supporting notes"));
        notes.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        body.Add(notes, "attachments", "notes.txt");
        body.Add(new StringContent(JsonSerializer.Serialize(new
        {
            drafts = new[]
            {
                new
                {
                    sourceIndex = 0,
                    title = $"Source with extra files {Guid.NewGuid():N}",
                    contentMarkdown = "Source content",
                    contentType = "reference",
                    status = "draft",
                    tags = Array.Empty<string>(),
                    keepOriginal = true,
                    additionalAttachmentIndexes = new[] { 0, 1 },
                    additionalAttachmentIncludeInIndex = new[] { false, true }
                }
            }
        })), "manifest");

        var response = await client.PostAsync("/api/source-imports/commit", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, result.GetProperty("created").GetInt32());
        var articleId = result.GetProperty("items")[0].GetProperty("articleId").GetString();
        var attachments = await client.GetFromJsonAsync<JsonElement>($"/api/articles/{articleId}/attachments");
        var names = attachments.GetProperty("attachments").EnumerateArray()
            .Select(item => item.GetProperty("fileName").GetString())
            .ToArray();
        Assert.Equal(3, attachments.GetProperty("total").GetInt32());
        Assert.Contains("source.txt", names);
        Assert.Contains("diagram.png", names);
        Assert.Contains("notes.txt", names);
        var byName = attachments.GetProperty("attachments").EnumerateArray()
            .ToDictionary(item => item.GetProperty("fileName").GetString()!);
        Assert.False(byName["source.txt"].GetProperty("includeInIndex").GetBoolean());
        Assert.False(byName["diagram.png"].GetProperty("includeInIndex").GetBoolean());
        Assert.True(byName["notes.txt"].GetProperty("includeInIndex").GetBoolean());
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
