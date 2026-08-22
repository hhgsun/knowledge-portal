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
}
