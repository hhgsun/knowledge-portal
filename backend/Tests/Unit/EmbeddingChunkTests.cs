using KnowledgePortal.Api.Services;

namespace KnowledgePortal.Api.Tests.Unit;

public class EmbeddingChunkTests
{
    [Fact]
    public void ChunkText_ShortText_ReturnsSingleChunk()
    {
        var text = string.Join(' ', Enumerable.Range(0, 100).Select(i => $"kelime{i}"));

        var chunks = EmbeddingService.ChunkText(text);

        Assert.Single(chunks);
        Assert.Equal(text, chunks[0]);
    }

    [Fact]
    public void ChunkText_ExactlyAtLimit_ReturnsSingleChunk()
    {
        var text = string.Join(' ', Enumerable.Range(0, 500).Select(i => $"w{i}"));

        var chunks = EmbeddingService.ChunkText(text);

        Assert.Single(chunks);
    }

    [Fact]
    public void ChunkText_LongText_ChunksWithOverlap()
    {
        // 1000 words, chunk=500, overlap=50 → step=450: [0..500), [450..950), [900..1000)
        var words = Enumerable.Range(0, 1000).Select(i => $"w{i}").ToArray();
        var text = string.Join(' ', words);

        var chunks = EmbeddingService.ChunkText(text);

        Assert.Equal(3, chunks.Count);
        Assert.StartsWith("w0 ", chunks[0]);
        Assert.EndsWith(" w499", chunks[0]);
        Assert.StartsWith("w450 ", chunks[1]);
        Assert.EndsWith(" w949", chunks[1]);
        Assert.StartsWith("w900 ", chunks[2]);
        Assert.EndsWith(" w999", chunks[2]);
    }

    [Fact]
    public void ChunkText_EveryWordAppearsInSomeChunk()
    {
        var words = Enumerable.Range(0, 1234).Select(i => $"tok{i}").ToArray();

        var chunks = EmbeddingService.ChunkText(string.Join(' ', words));

        var covered = chunks
            .SelectMany(c => c.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet();
        Assert.All(words, w => Assert.Contains(w, covered));
    }

    [Fact]
    public void ChunkMarkdown_PreservesHeadingBoundariesAndLocations()
    {
        const string markdown = """
            # VPN Kurulumu

            İstemci profili güvenli portaldan indirilir ve kurulum başlatılır.

            ## Sertifika Yenileme

            Süresi dolan sertifika yenileme ekranından değiştirilir.
            """;

        var chunks = KnowledgeChunker.ChunkMarkdown("Uzak Erişim", "Operasyon rehberi", markdown, 50, 5);

        Assert.Equal(2, chunks.Count);
        Assert.Contains("VPN Kurulumu", chunks[0].Content);
        Assert.DoesNotContain("Sertifika Yenileme", chunks[0].Content);
        Assert.Contains("Sertifika Yenileme", chunks[1].Content);
        Assert.StartsWith("section:VPN Kurulumu:chunk:", chunks[0].Location);
        Assert.StartsWith("section:Sertifika Yenileme:chunk:", chunks[1].Location);
    }

    [Fact]
    public void ChunkMarkdown_KeepsTableAndCodeContentSearchable()
    {
        const string markdown = """
            ## Hata Kodları

            | Kod | Açıklama |
            | --- | --- |
            | ERR42 | Sertifika geçersiz |

            ```powershell
            Test-NetConnection vpn.internal
            ```
            """;

        var chunk = Assert.Single(KnowledgeChunker.ChunkMarkdown("VPN", null, markdown, 100, 10));

        Assert.Contains("ERR42", chunk.Content);
        Assert.Contains("Sertifika geçersiz", chunk.Content);
        Assert.Contains("Test-NetConnection", chunk.Content);
    }

    [Fact]
    public void ChunkText_PreservesTechnicalIdentifierCharacters()
    {
        const string text = "error_code ERR_42 service_name vpn_gateway";

        var chunk = Assert.Single(KnowledgeChunker.ChunkText(text, 100, 10));

        Assert.Equal(text, chunk.Content);
    }

    [Fact]
    public void ChunkMarkdown_DoesNotCreateMetadataOnlyChunkForLongSection()
    {
        var body = string.Join(' ', Enumerable.Range(0, 80).Select(i => $"body{i}"));

        var chunks = KnowledgeChunker.ChunkMarkdown("Long Document", "Summary", $"# Operations\n\n{body}", 20, 3);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk =>
        {
            Assert.Contains("Long Document", chunk.Content);
            Assert.Contains("Operations", chunk.Content);
            Assert.Contains("body", chunk.Content);
            Assert.True(chunk.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 20);
        });
    }

    [Fact]
    public void BuildMarkdownHierarchy_SearchesChildrenAndPreservesSectionBoundedParents()
    {
        var first = string.Join(' ', Enumerable.Range(0, 700).Select(i => $"vpn{i}"));
        var second = string.Join(' ', Enumerable.Range(0, 300).Select(i => $"cert{i}"));
        var markdown = $"# VPN Kurulumu\n\n{first}\n\n## Sertifika\n\n{second}";

        var parents = KnowledgeChunker.BuildMarkdownHierarchy("Uzak Erişim", "Operasyon rehberi",
            markdown, parentTargetWords: 500, childTargetWords: 120, childOverlapWords: 20);

        Assert.True(parents.Count >= 3);
        Assert.All(parents, parent =>
        {
            Assert.NotEmpty(parent.Children);
            Assert.True(parent.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 500);
            Assert.All(parent.Children, child =>
            {
                Assert.StartsWith(parent.Location + ":child:", child.Location);
                Assert.True(child.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 120);
            });
        });
        Assert.DoesNotContain(parents, p => p.Content.Contains("vpn0") && p.Content.Contains("cert0"));
        Assert.Contains(parents, p => p.Location.StartsWith("section:VPN Kurulumu:parent:"));
        Assert.Contains(parents, p => p.Location.StartsWith("section:Sertifika:parent:"));
    }

    [Fact]
    public void BuildTextHierarchy_DoesNotCrossLayoutSegmentAndOverlapsChildren()
    {
        var words = Enumerable.Range(0, 360).Select(i => $"w{i}").ToArray();

        var parent = Assert.Single(KnowledgeChunker.BuildTextHierarchy(string.Join(' ', words),
            "page:7", parentTargetWords: 500, childTargetWords: 120, childOverlapWords: 20));

        Assert.Equal("page:7:parent:0", parent.Location);
        Assert.Equal(4, parent.Children.Count);
        Assert.EndsWith("w119", parent.Children[0].Content);
        Assert.StartsWith("w100 ", parent.Children[1].Content);
    }

    [Fact]
    public void BuildTextHierarchy_PreservesMarkdownTableRowsAndHeader()
    {
        const string table = """
            | Kod | Açıklama |
            | --- | --- |
            | ERR42 | Sertifika geçersiz |
            | ERR51 | Ağ erişimi yok |
            """;

        var parent = Assert.Single(KnowledgeChunker.BuildTextHierarchy(table, "page:2",
            parentTargetWords: 100, childTargetWords: 50, childOverlapWords: 5));
        var child = Assert.Single(parent.Children);

        Assert.Contains("| Kod | Açıklama |\n| --- | --- |", parent.Content);
        Assert.Contains("| ERR42 | Sertifika geçersiz |", child.Content);
        Assert.DoesNotContain("| Kod | Açıklama | | ---", child.Content);
    }
}
