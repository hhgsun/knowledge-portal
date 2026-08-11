using KnowledgePortal.Api.Services;

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
}
