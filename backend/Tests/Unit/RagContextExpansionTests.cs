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
        Assert.Equal(2, result.AddedNeighbors);
        Assert.DoesNotContain(result.Chunks, x => x.ArticleId == "a2" || x.SourceLocation?.Contains("Başka") == true);
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
