using System.Net;
using System.Text;
using KnowledgePortal.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgePortal.Api.Tests.Unit;

public class ExternalRagChunkRerankerTests
{
    [Fact]
    public async Task RerankAsync_UsesExternalCrossEncoderScoresWhenEnabled()
    {
        var handler = new StubHandler("""{"results":[{"index":0,"relevance_score":0.1},{"index":1,"relevance_score":0.9}]}""");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Reranking:External:Enabled"] = "true",
            ["Reranking:External:Endpoint"] = "https://reranker.example.test/rerank",
            ["Reranking:External:ScoreWeight"] = "1"
        }).Build();
        var service = new ExternalRagChunkReranker(new HttpClient(handler),
            new LocalRagChunkReranker(), config, NullLogger<ExternalRagChunkReranker>.Instance);
        var candidates = new[]
        {
            Candidate("a1", "ilk"), Candidate("a2", "ikinci")
        };

        var result = await service.RerankAsync("sorgu", candidates);

        Assert.Equal("a2", result[0].Chunk.ArticleId);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RerankAsync_FallsBackToLocalRankingWhenProviderResponseIsInvalid()
    {
        var handler = new StubHandler("""{"results":[]}""");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Reranking:External:Enabled"] = "true",
            ["Reranking:External:Endpoint"] = "https://reranker.example.test/rerank"
        }).Build();
        var service = new ExternalRagChunkReranker(new HttpClient(handler),
            new LocalRagChunkReranker(), config, NullLogger<ExternalRagChunkReranker>.Instance);
        var candidates = new[]
        {
            Candidate("a1", "ilgisiz içerik"), Candidate("a2", "aranan ikinci ifade")
        };

        var result = await service.RerankAsync("ikinci", candidates);

        Assert.Equal("a2", result[0].Chunk.ArticleId);
        Assert.Equal(1, handler.Calls);
    }

    private static RagChunkCandidate Candidate(string id, string text) =>
        new(new VectorChunkResult(id, 0, .5, text), id, null, .5, "semantic");

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
