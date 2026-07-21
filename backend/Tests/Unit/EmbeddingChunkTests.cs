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
}
