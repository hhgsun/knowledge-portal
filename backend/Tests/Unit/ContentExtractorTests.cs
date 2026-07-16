using KnowledgePortal.Api.Helpers;

namespace KnowledgePortal.Api.Tests.Unit;

public class ContentExtractorTests
{
    [Fact]
    public void ExtractPlainText_ReturnsTextNodes()
    {
        const string json = """
            {"type":"doc","content":[
                {"type":"paragraph","content":[{"type":"text","text":"Birinci paragraf."}]},
                {"type":"paragraph","content":[{"type":"text","text":"İkinci paragraf."}]}
            ]}
            """;

        var text = ContentExtractor.ExtractPlainText(json);

        Assert.NotNull(text);
        Assert.Contains("Birinci paragraf.", text);
        Assert.Contains("İkinci paragraf.", text);
    }

    [Fact]
    public void ExtractPlainText_IgnoresNodeAttributes()
    {
        // Link href, image src, and style attrs are metadata — they must not leak
        // into the searchable text (they cause false-positive matches)
        const string json = """
            {"type":"doc","content":[
                {"type":"paragraph","content":[
                    {"type":"text","text":"tıklanabilir bağlantı","marks":[{"type":"link","attrs":{"href":"https://gizli-sunucu.example/yol"}}]}
                ]},
                {"type":"image","attrs":{"src":"/api/attachments/abc123/download","alt":"diyagram"}},
                {"type":"paragraph","attrs":{"textAlign":"center"},"content":[{"type":"text","text":"ortalanmış metin"}]}
            ]}
            """;

        var text = ContentExtractor.ExtractPlainText(json);

        Assert.NotNull(text);
        Assert.Contains("tıklanabilir bağlantı", text);
        Assert.Contains("ortalanmış metin", text);
        Assert.DoesNotContain("gizli-sunucu", text);
        Assert.DoesNotContain("attachments", text);
        Assert.DoesNotContain("center", text);
    }
}

public class SearchSnippetHelperTests
{
    [Fact]
    public void Build_ReturnsWindowAroundMatch_WithEllipses()
    {
        var filler = string.Join(" ", Enumerable.Repeat("dolgu", 100));
        var text = $"{filler} hedefkelime sonrası devam ediyor {filler}";

        var snippet = SearchSnippetHelper.Build(text, ["hedefkelime"]);

        Assert.NotNull(snippet);
        Assert.Contains("hedefkelime", snippet);
        Assert.StartsWith("…", snippet);
        Assert.EndsWith("…", snippet);
        Assert.True(snippet!.Length < text.Length);
    }

    [Fact]
    public void Build_IsAccentAndCaseInsensitive()
    {
        var snippet = SearchSnippetHelper.Build("Yıllık İzin Politikası detayları burada.", ["POLİTİKASI"]);

        Assert.NotNull(snippet);
        Assert.Contains("Politikası", snippet);
    }

    [Fact]
    public void Build_FallsBackToTokenPrefix_ForStemmedMatches()
    {
        // Query has a suffixed form; text has the stem — prefix retry should still hit
        var snippet = SearchSnippetHelper.Build("Şirket güvenlik politika dokümanı.", ["politikası"]);

        Assert.NotNull(snippet);
        Assert.Contains("politika", snippet);
    }

    [Fact]
    public void Build_ReturnsNull_WhenNoMatch()
    {
        Assert.Null(SearchSnippetHelper.Build("Tamamen alakasız bir metin.", ["bulunmayankelime"]));
        Assert.Null(SearchSnippetHelper.Build(null, ["kelime"]));
        Assert.Null(SearchSnippetHelper.Build("metin", []));
    }
}
