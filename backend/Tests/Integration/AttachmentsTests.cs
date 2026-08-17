using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace KnowledgePortal.Api.Tests.Integration;

public class AttachmentsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AttachmentsTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Upload_ValidFile_Returns201()
    {
        await AuthenticateAsAdmin();
        var articleId = await CreateArticle("Attachment Test Article");

        var content = new MultipartFormDataContent();
        var fileBytes = "Hello, World!"u8.ToArray();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "test.txt");

        var response = await _client.PostAsync($"/api/articles/{articleId}/attachments", content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("id", out _));
        Assert.Equal("test.txt", body.GetProperty("fileName").GetString());
        Assert.Equal("text/plain", body.GetProperty("contentType").GetString());
        Assert.Equal(fileBytes.Length, body.GetProperty("sizeBytes").GetInt64());
        Assert.StartsWith("/api/attachments/", body.GetProperty("downloadUrl").GetString());
    }

    [Fact]
    public async Task UploadImage_ThenSaveImageMarkdown_ReturnsOk()
    {
        await AuthenticateAsAdmin();
        var articleId = await CreateArticle("Inline Image Save Test");

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "inline.png");

        var uploadResponse = await _client.PostAsync($"/api/articles/{articleId}/attachments", content);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        var upload = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();
        var markdown = $"![inline image]({upload.GetProperty("downloadUrl").GetString()})";

        var updateResponse = await _client.PutAsJsonAsync($"/api/articles/{articleId}", new
        {
            contentMarkdown = markdown,
            changeSummary = "Added inline image"
        });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var article = await _client.GetFromJsonAsync<JsonElement>($"/api/articles/{articleId}");
        Assert.Equal(markdown, article.GetProperty("contentMarkdown").GetString());
    }

    [Fact]
    public async Task Upload_InvalidExtension_Returns400()
    {
        await AuthenticateAsAdmin();
        var articleId = await CreateArticle("Attachment Invalid Type");

        var content = new MultipartFormDataContent();
        var fileBytes = new byte[] { 0x4D, 0x5A }; // MZ header (exe)
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "file", "malware.exe");

        var response = await _client.PostAsync($"/api/articles/{articleId}/attachments", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(".exe", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task List_ReturnsAttachments()
    {
        await AuthenticateAsAdmin();
        var articleId = await CreateArticle("Attachment List Test");
        await UploadTextFile(articleId, "file1.txt", "Content 1");
        await UploadTextFile(articleId, "file2.txt", "Content 2");

        var response = await _client.GetAsync($"/api/articles/{articleId}/attachments");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("total").GetInt32());
        Assert.Equal(2, body.GetProperty("attachments").GetArrayLength());
    }

    [Fact]
    public async Task Download_ReturnsFileContent()
    {
        await AuthenticateAsAdmin();
        var articleId = await CreateArticle("Attachment Download Test");
        var fileText = "Download test content";
        var attachmentId = await UploadTextFile(articleId, "download.txt", fileText);

        var response = await _client.GetAsync($"/api/attachments/{attachmentId}/download");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);

        var downloadedContent = await response.Content.ReadAsStringAsync();
        Assert.Equal(fileText, downloadedContent);
    }

    [Fact]
    public async Task Delete_RemovesAttachment()
    {
        await AuthenticateAsAdmin();
        var articleId = await CreateArticle("Attachment Delete Test");
        var attachmentId = await UploadTextFile(articleId, "to-delete.txt", "Delete me");

        var deleteResponse = await _client.DeleteAsync($"/api/articles/{articleId}/attachments/{attachmentId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        // Verify it's gone
        var listResponse = await _client.GetAsync($"/api/articles/{articleId}/attachments");
        var body = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Upload_ViewerOtherArticle_Returns403()
    {
        // Create article as admin
        await AuthenticateAsAdmin();
        var articleId = await CreateArticle("Admin Article For Viewer Test");

        // Switch to viewer
        var viewerToken = await RegisterAndGetToken($"viewer-attach-{Guid.NewGuid():N}@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", viewerToken);

        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("test"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "unauthorized.txt");

        var response = await _client.PostAsync($"/api/articles/{articleId}/attachments", content);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Upload_Unauthenticated_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("test"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "noauth.txt");

        var response = await _client.PostAsync("/api/articles/nonexistent/attachments", content);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_MimeTypeMismatch_Returns400()
    {
        await AuthenticateAsAdmin();
        var articleId = await CreateArticle("MIME Mismatch Test");

        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("not an image"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain"); // mismatch with .png extension
        content.Add(fileContent, "file", "fake.png");

        var response = await _client.PostAsync($"/api/articles/{articleId}/attachments", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Download_NonexistentAttachment_Returns404()
    {
        await AuthenticateAsAdmin();
        var response = await _client.GetAsync("/api/attachments/nonexistentid12345678/download");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CannotAccessAnotherUsersDraftAttachmentOrFeedback()
    {
        await AuthenticateAsAdmin();
        var articleId = await CreateArticle("Private draft " + Guid.NewGuid().ToString("N"), "draft");
        var attachmentId = await UploadTextFile(articleId, "private.txt", "private knowledge");

        var viewerToken = await RegisterAndGetToken($"viewer-private-{Guid.NewGuid():N}@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", viewerToken);

        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.GetAsync($"/api/articles/{articleId}/attachments")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.GetAsync($"/api/attachments/{attachmentId}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.GetAsync($"/api/articles/{articleId}/votes")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.GetAsync($"/api/articles/{articleId}/comments")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.PostAsJsonAsync($"/api/articles/{articleId}/comments", new { comment = "probe" })).StatusCode);
    }

    // ─── Helpers ─────────────────────────────────────────────

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

    private async Task<string> CreateArticle(string title, string status = "published")
    {
        var response = await _client.PostAsJsonAsync("/api/articles", new
        {
            title,
            status
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetString()!;
    }

    private async Task<string> UploadTextFile(string articleId, string fileName, string content)
    {
        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        multipart.Add(fileContent, "file", fileName);

        var response = await _client.PostAsync($"/api/articles/{articleId}/attachments", multipart);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetString()!;
    }
}
