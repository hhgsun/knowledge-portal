using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.IO.Compression;

namespace KnowledgePortal.Api.Tests.Integration;

public class BulkTransferTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient client;
    public BulkTransferTests(TestWebApplicationFactory factory) => client = factory.CreateClient();

    [Fact]
    public async Task Import_DryRun_ValidJsonl_DoesNotPersist()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var title = $"Dry Run {Guid.NewGuid():N}";
        var jsonl = JsonSerializer.Serialize(new { title, status = "draft", contentType = "reference", tags = new[] { "tutorial" } });
        using var form = Form(jsonl, "articles.jsonl", dryRun: true);
        var response = await client.PostAsync("/api/bulk/import", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(result.GetProperty("dryRun").GetBoolean());
        Assert.Equal(1, result.GetProperty("created").GetInt32());
        var list = await client.GetAsync($"/api/articles?q={Uri.EscapeDataString(title)}");
        Assert.DoesNotContain(title, await list.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Import_ThenExport_Jsonl_RoundTripsArticle()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var title = $"Bulk Article {Guid.NewGuid():N}";
        using var form = Form(JsonSerializer.Serialize(new { title, status = "draft", contentType = "reference" }), "articles.jsonl");
        var imported = await client.PostAsync("/api/bulk/import", form);
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        var exported = await client.GetAsync("/api/bulk/export?format=jsonl&mine=true&status=draft");
        Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
        Assert.Contains(title, await exported.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ImportMarkdown_ThenExportArchive_PreservesCanonicalContent()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var title = $"Markdown Article {Guid.NewGuid():N}";
        var markdown = $$"""
            ---
            {
              "title": "{{title}}",
              "status": "draft",
              "contentType": "reference",
              "tags": ["tutorial"]
            }
            ---

            ## Kurulum

            - Birinci adım
            - İkinci adım
            """;
        using var form = Form(markdown, "article.md");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/bulk/import", form)).StatusCode);

        var response = await client.GetAsync("/api/bulk/export?format=markdown&mine=true&status=draft");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var exported = archive.Entries.Single(e => e.Name.EndsWith(".md") && ReadEntry(e).Contains(title));
        var source = ReadEntry(exported);
        Assert.Contains("\"title\": \"" + title + "\"", source);
        Assert.Contains("## Kurulum", source);
        Assert.Contains("- İkinci adım", source);
    }

    [Fact]
    public async Task Import_InvalidExtension_Returns400()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        using var form = Form("x", "articles.txt");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/bulk/import", form)).StatusCode);
    }

    [Theory]
    [InlineData("jsonl", "article-import-template.jsonl")]
    [InlineData("csv", "article-import-template.csv")]
    [InlineData("md", "article-import-template.md")]
    public async Task DownloadTemplate_ReturnsAttachment(string format, string fileName)
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var response = await client.GetAsync($"/api/bulk/templates/{format}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(fileName, response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ImportSchema_ReturnsDatabaseContentTypesAndLimits()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var response = await client.GetAsync("/api/bulk/import-schema");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var schema = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(5000, schema.GetProperty("maxRecords").GetInt32());
        Assert.Contains(schema.GetProperty("contentTypes").EnumerateArray(), x => x.GetProperty("value").GetString() == "reference");
        Assert.Contains(schema.GetProperty("fields").EnumerateArray(), x => x.GetProperty("name").GetString() == "title" && x.GetProperty("required").GetBoolean());
    }

    [Fact]
    public async Task Export_AppliesAuthorContentTypeTagAndDateFilters()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        var title = $"Filtered Export {Guid.NewGuid():N}";
        var row = JsonSerializer.Serialize(new { title, status = "draft", contentType = "how-to", tags = new[] { "export-filter" } });
        using var form = Form(row, "articles.jsonl");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/bulk/import", form)).StatusCode);

        var authors = JsonDocument.Parse(await client.GetStringAsync("/api/search/authors")).RootElement;
        var adminId = authors.EnumerateArray().First(x => x.GetProperty("name").GetString() == "Admin").GetProperty("id").GetString();
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var url = $"/api/bulk/export?format=jsonl&authorId={adminId}&contentType=how-to&tag=export-filter&dateFrom={today}&dateTo={today}";
        var exported = await client.GetStringAsync(url);
        Assert.Contains(title, exported);
    }

    [Fact]
    public async Task Export_InvalidDate_Returns400()
    {
        await TestHelpers.AuthenticateAsAdminAsync(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/bulk/export?dateFrom=not-a-date")).StatusCode);
    }


    private static MultipartFormDataContent Form(string content, string name, bool dryRun = false)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", name);
        form.Add(new StringContent(dryRun.ToString()), "dryRun");
        form.Add(new StringContent("skip"), "conflictPolicy");
        return form;
    }

    private static string ReadEntry(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

}
