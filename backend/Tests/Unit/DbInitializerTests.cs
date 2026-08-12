using KnowledgePortal.Api.Data;

namespace KnowledgePortal.Api.Tests.Unit;

public class DbInitializerTests
{
    [Fact]
    public void ParseMarkdownSeed_ReadsMetadataAndMarkdownBody()
    {
        const string source = """
            ---
            {
              "title": "Milkdown makalesi",
              "contentType": "how-to",
              "tags": ["tutorial", "getting-started"],
              "excerpt": "Kısa açıklama",
              "status": "published"
            }
            ---

            ## Başlık

            Markdown içerik.
            """;

        var (metadata, markdown) = DbInitializer.ParseMarkdownSeed(source, "article.md");

        Assert.Equal("Milkdown makalesi", metadata.Title);
        Assert.Equal("how-to", metadata.ContentType);
        Assert.Equal(["tutorial", "getting-started"], metadata.Tags);
        Assert.Equal("Kısa açıklama", metadata.Excerpt);
        Assert.Equal("published", metadata.Status);
        Assert.Equal("## Başlık\n\nMarkdown içerik.", markdown);
    }
}
