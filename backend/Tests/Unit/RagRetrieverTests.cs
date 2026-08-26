using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using KnowledgePortal.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgePortal.Api.Tests.Unit;

public class RagRetrieverTests
{
    private sealed class FakeVectors(List<VectorChunkResult> chunks) : IVectorSearchService
    {
        public Task<List<VectorSearchResult>> SearchAsync(string queryText, int limit, CancellationToken ct = default, double? minScore = null, ArticleFilter? filter = null) =>
            Task.FromResult(chunks.Take(limit).Select(x => new VectorSearchResult(x.ArticleId, x.Score, x.ChunkIndex)).ToList());
        public Task<List<VectorChunkResult>> SearchChunksAsync(string queryText, int maxChunks, CancellationToken ct = default, double? minScore = null, int maxPerArticle = 3, ArticleFilter? filter = null) =>
            Task.FromResult(chunks.Take(maxChunks).ToList());
    }

    [Fact]
    public async Task ChunkReranker_PromotesExactTitleAndBodyEvidence()
    {
        var reranker = new LocalRagChunkReranker();
        var candidates = new[]
        {
            new RagChunkCandidate(new VectorChunkResult("semantic", 0, .95, "başka konu"), "Genel Belge", null, .95, "semantic"),
            new RagChunkCandidate(new VectorChunkResult("exact", 0, .70, "vpn sertifika kurulumu adımları"), "VPN Sertifika Kurulumu", null, .70, "both")
        };

        var ranked = await reranker.RerankAsync("vpn sertifika kurulumu", candidates);

        Assert.Equal("exact", ranked[0].Chunk.ArticleId);
    }

    [Fact]
    public void InterleaveByArticle_PreventsOneLongArticleFromMonopolizingContext()
    {
        var ranked = new List<RagRetrievalChunk>
        {
            Item("a", 0, .9), Item("a", 1, .8), Item("a", 2, .7), Item("b", 0, .6), Item("c", 0, .5)
        };

        var selected = HybridRagRetriever.InterleaveByArticle(ranked, 4, 3);

        Assert.Equal(["a", "b", "c", "a"], selected.Select(x => x.Chunk.ArticleId));
    }

    [Fact]
    public async Task HybridRetriever_RecoversLexicalOnlyArticleAndMarksBothMatches()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new AppDbContext(options);
        db.Articles.AddRange(
            Article("lexical", "ERR42 VPN çözümü", "ERR42 kodunda sertifika yenilenir."),
            Article("semantic", "ERR42 VPN genel bakış", "VPN hakkında genel bilgi."));
        await db.SaveChangesAsync();
        var config = new ConfigurationBuilder().Build();
        var fts = new FullTextSearchService(db, config, NullLogger<FullTextSearchService>.Instance);
        var vectors = new FakeVectors([new VectorChunkResult("semantic", 0, .8, "ERR42 için VPN genel bilgi")]);
        var retriever = new HybridRagRetriever(vectors, fts, db, new LocalRagChunkReranker(), config, NullLogger<HybridRagRetriever>.Instance);

        var plan = new RagQueryPlan("ERR42 VPN", "ERR42 VPN", ["ERR42 VPN"],
            new([], [], []), [], false, false, null);
        var result = await retriever.RetrieveAsync(plan, 10, .3, 3);

        Assert.Contains(result, x => x.Chunk.ArticleId == "lexical");
        Assert.Contains(result, x => x.Chunk.ArticleId == "semantic" && x.MatchType == "both");
    }

    [Fact]
    public async Task HybridRetriever_UsesDynamicLookupAuthorityInsteadOfContentTypeConfig()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new AppDbContext(options);
        var updated = DateTime.UtcNow;
        var high = Article("high", "VPN Politikası", "VPN erişim kuralı.");
        high.ContentType = "dynamic-high";
        high.UpdatedAt = updated;
        var low = Article("low", "VPN Politikası", "VPN erişim kuralı.");
        low.ContentType = "dynamic-low";
        low.UpdatedAt = updated;
        db.Articles.AddRange(high, low);
        db.LookupValues.AddRange(
            new LookupValue { Id = "lh", Category = "content_type", Value = "dynamic-high", Label = "High", AuthorityWeight = 100 },
            new LookupValue { Id = "ll", Category = "content_type", Value = "dynamic-low", Label = "Low", AuthorityWeight = 0 });
        await db.SaveChangesAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ollama:Ranking:AuthorityWeight"] = ".2"
        }).Build();
        var fts = new FullTextSearchService(db, config, NullLogger<FullTextSearchService>.Instance);
        var vectors = new FakeVectors([
            new VectorChunkResult("low", 0, .8, "VPN erişim kuralı"),
            new VectorChunkResult("high", 0, .8, "VPN erişim kuralı")
        ]);
        var retriever = new HybridRagRetriever(vectors, fts, db, new LocalRagChunkReranker(), config,
            NullLogger<HybridRagRetriever>.Instance);
        var plan = new RagQueryPlan("VPN erişim", "VPN erişim", ["VPN erişim"],
            new([], [], []), [], false, false, null);

        var result = await retriever.RetrieveAsync(plan, 10, .3, 3);

        Assert.Equal("high", result[0].Chunk.ArticleId);
    }

    private static RagRetrievalChunk Item(string article, int chunk, double score) =>
        new(new VectorChunkResult(article, chunk, score, $"content {article} {chunk}"), score, "semantic");
    private static Article Article(string id, string title, string content) => new() { Id = id, Title = title, Slug = id, Content = content, Status = "published", OwnerId = "owner", UpdatedAt = DateTime.UtcNow };
}
