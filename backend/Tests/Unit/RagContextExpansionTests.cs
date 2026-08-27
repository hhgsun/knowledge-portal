using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using KnowledgePortal.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace KnowledgePortal.Api.Tests.Unit;

public class RagContextExpansionTests
{
    [Fact]
    public async Task Expand_AddsOnlyAdjacentChunkFromSameAuthorizedParent()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new AppDbContext(options);
        db.ArticleEmbeddings.AddRange(
            Embedding("a1", 0, "section:VPN:chunk:0", "önce"),
            Embedding("a1", 1, "section:VPN:chunk:1", "eşleşme"),
            Embedding("a1", 2, "section:VPN:chunk:2", "sonra"),
            Embedding("a1", 3, "section:Başka:chunk:0", "başka bölüm"),
            Embedding("a2", 0, "section:VPN:chunk:0", "yetkisiz"));
        await db.SaveChangesAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ollama:ContextExpansion:MinSeedScore"] = ".5",
            ["Ollama:ContextExpansion:NeighborCount"] = "1"
        }).Build();
        var service = new RagContextExpansionService(config);
        var seed = new VectorChunkResult("a1", 1, .9, "eşleşme", SourceLocation: "section:VPN:chunk:1", ChunkId: "e1");

        var result = await service.ExpandAsync(db, [seed], new HashSet<string> { "a1" });

        Assert.Equal(3, result.Chunks.Count);
        Assert.Equal(0, result.ExpandedParentCount);
        Assert.DoesNotContain(result.Chunks, x => x.ArticleId == "a2" || x.SourceLocation?.Contains("Başka") == true);
    }

    [Fact]
    public async Task Expand_ReplacesStrongChildWithSingleAuthorizedParent()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new AppDbContext(options);
        db.ArticleChunkParents.AddRange(
            new ArticleChunkParent
            {
                Id = "p1", ArticleId = "a1", ParentIndex = 3, Content = "geniş yetkili bağlam",
                SourceType = "article", SourceLocation = "section:VPN:parent:0", TextHash = "h", WordCount = 3
            },
            new ArticleChunkParent
            {
                Id = "p2", ArticleId = "a2", ParentIndex = 0, Content = "yetkisiz bağlam",
                SourceType = "article", SourceLocation = "section:Gizli:parent:0", TextHash = "h", WordCount = 2
            });
        await db.SaveChangesAsync();
        var service = new RagContextExpansionService(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Ollama:ContextExpansion:MinSeedScore"] = ".5" }).Build());
        var first = new VectorChunkResult("a1", 10, .9, "eşleşen küçük parça",
            SourceLocation: "section:VPN:parent:0:child:0", ChunkId: "c1", ParentChunkId: "p1");
        var second = first with { ChunkIndex = 11, ChunkId = "c2", Score = .8 };
        var unauthorized = new VectorChunkResult("a2", 0, .99, "gizli",
            SourceLocation: "section:Gizli:parent:0:child:0", ChunkId: "c3", ParentChunkId: "p2");

        var result = await service.ExpandAsync(db, [first, second, unauthorized],
            new HashSet<string> { "a1" });

        var parent = Assert.Single(result.Chunks, x => x.ArticleId == "a1");
        Assert.Equal("p1", parent.ChunkId);
        Assert.Equal("geniş yetkili bağlam", parent.ChunkText);
        Assert.Equal(-4, parent.ChunkIndex);
        Assert.Equal(1, result.ExpandedParentCount);
        Assert.DoesNotContain(result.Chunks, x => x.ChunkText == "yetkisiz bağlam");
    }

    [Theory]
    [InlineData("section:Başlık:chunk:12", "section:Başlık", 12)]
    [InlineData("page:4:chunk:0", "page:4", 0)]
    public void Parse_DerivesParentAndChildIndex(string location, string parent, int index)
    {
        var parsed = RagContextExpansionService.Parse(location);
        Assert.NotNull(parsed);
        Assert.Equal(parent, parsed.Value.Parent);
        Assert.Equal(index, parsed.Value.Index);
    }

    private static ArticleEmbedding Embedding(string articleId, int index, string location, string content) => new()
    {
        Id = $"{articleId}-{index}", ArticleId = articleId, ChunkIndex = index, Content = content,
        SourceType = "article", SourceLocation = location, ModelName = "bge-m3", TextHash = "h", Dimensions = 1024
    };
}
