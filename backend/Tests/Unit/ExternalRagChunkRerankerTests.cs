using System.Net;
using System.Text;
using KnowledgePortal.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        var service = Create(handler, config);
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
        var service = Create(handler, config);
        var candidates = new[]
        {
            Candidate("a1", "ilgisiz içerik"), Candidate("a2", "aranan ikinci ifade")
        };

        var result = await service.RerankAsync("ikinci", candidates);

        Assert.Equal("a2", result[0].Chunk.ArticleId);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task RerankAsync_NormalizesUnboundedCrossEncoderLogits()
    {
        var handler = new StubHandler("""{"scores":[-4.0,7.5]}""");
        var config = EnabledConfig();
        var result = await Create(handler, config).RerankAsync("sorgu",
            [Candidate("a1", "ilk"), Candidate("a2", "ikinci")]);

        Assert.Equal("a2", result[0].Chunk.ArticleId);
    }

    [Fact]
    public async Task RerankAsync_RejectsPartialProviderResultsBelowCoverageThreshold()
    {
        var handler = new StubHandler("""{"results":[{"index":0,"score":0.99}]}""");
        var config = EnabledConfig();
        var candidates = new[]
        {
            Candidate("a1", "ilgisiz içerik"), Candidate("a2", "aranan ikinci ifade")
        };

        var result = await Create(handler, config).RerankAsync("ikinci", candidates);

        Assert.Equal("a2", result[0].Chunk.ArticleId);
    }

    [Fact]
    public async Task RerankAsync_RetriesTransientStatusWithinBoundedBudget()
    {
        var handler = new SequenceHandler(
            new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("temporary") },
            new(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"scores":[0.1,0.9]}""", Encoding.UTF8,
                    "application/json")
            });

        var result = await Create(handler, EnabledConfig()).RerankAsync("sorgu",
            [Candidate("a1", "ilk"), Candidate("a2", "ikinci")]);

        Assert.Equal("a2", result[0].Chunk.ArticleId);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task RerankAsync_OpensCircuitAndSkipsProviderAfterConfiguredFailureThreshold()
    {
        var handler = new StubHandler("""{"results":[]}""");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Reranking:External:Enabled"] = "true",
            ["Reranking:External:Endpoint"] = "https://reranker.example.test/rerank",
            ["Reranking:External:MaxRetries"] = "0",
            ["Reranking:External:CircuitBreakerFailureThreshold"] = "1"
        }).Build();
        var service = Create(handler, config);
        var candidates = new[] { Candidate("a1", "ilk"), Candidate("a2", "ikinci") };

        await service.RerankAsync("sorgu", candidates);
        await service.RerankAsync("sorgu", candidates);

        Assert.Equal(1, handler.Calls);
    }

    private static IConfiguration EnabledConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Reranking:External:Enabled"] = "true",
            ["Reranking:External:Endpoint"] = "https://reranker.example.test/rerank",
            ["Reranking:External:ScoreWeight"] = "1"
        }).Build();

    private static ExternalRagChunkReranker Create(HttpMessageHandler handler, IConfiguration config)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var metrics = new PortalMetrics(services.GetRequiredService<IServiceScopeFactory>(), config);
        return new ExternalRagChunkReranker(new HttpClient(handler), new LocalRagChunkReranker(),
            new ExternalRerankerState(config), config, metrics,
            NullLogger<ExternalRagChunkReranker>.Instance);
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

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> queue = new(responses);
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(queue.Dequeue());
        }
    }
}
