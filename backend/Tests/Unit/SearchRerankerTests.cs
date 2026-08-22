using KnowledgePortal.Api.Services;
using Microsoft.Extensions.Configuration;

namespace KnowledgePortal.Api.Tests.Unit;

public class SearchRerankerTests
{
    [Fact]
    public void Rerank_PromotesExactTitleCoverageOverSmallRetrievalLead()
    {
        var reranker = new LocalSearchReranker();
        var results = reranker.Rerank("ödeme politikası",
        [
            new("semantic", "Genel finans", null, "çeşitli süreçler", 1.0),
            new("exact", "Ödeme politikası", null, "kurumsal ödeme politikası", 0.95)
        ]);

        Assert.Equal("exact", results[0].ArticleId);
    }

    [Fact]
    public void Rerank_FoldsTurkishAccents()
    {
        var reranker = new LocalSearchReranker();
        var results = reranker.Rerank("şifre değiştirme",
        [
            new("other", "Hesap", null, "profil", 0.9),
            new("match", "Sifre degistirme", null, null, 0.8)
        ]);

        Assert.Equal("match", results[0].ArticleId);
    }

    [Fact]
    public void Rerank_UsesFreshnessIntentAfterTurkishFolding()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ollama:Ranking:FreshnessWeight"] = "1",
            ["Ollama:Ranking:FreshnessIntentMultiplier"] = "2",
            ["Ollama:Ranking:AuthorityWeight"] = "0"
        }).Build();
        var reranker = new LocalSearchReranker(config);
        var results = reranker.Rerank("güncel politika",
        [
            new("old", "Politika", null, "güncel politika", 1, DateTime.UtcNow.AddYears(-5)),
            new("fresh", "Politika", null, "güncel politika", 1, DateTime.UtcNow.AddDays(-1))
        ]);

        Assert.Equal("fresh", results[0].ArticleId);
    }

    [Fact]
    public void Rerank_UsesConfiguredSourceAuthority()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ollama:Ranking:FreshnessWeight"] = "0",
            ["Ollama:Ranking:AuthorityWeight"] = "1",
            ["Ollama:Ranking:Authority:policy"] = "1",
            ["Ollama:Ranking:Authority:faq"] = "0"
        }).Build();
        var reranker = new LocalSearchReranker(config);
        var updated = DateTime.UtcNow;
        var results = reranker.Rerank("erişim",
        [
            new("faq", "Erişim", null, "erişim", 1, updated, ContentType: "faq"),
            new("policy", "Erişim", null, "erişim", 1, updated, ContentType: "policy")
        ]);

        Assert.Equal("policy", results[0].ArticleId);
    }
}
