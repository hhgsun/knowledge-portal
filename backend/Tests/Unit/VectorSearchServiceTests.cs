using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgePortal.Api.Tests.Unit;

public class VectorSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_RejectsQueryEmbeddingWithUnexpectedDimensionsBeforeDatabaseQuery()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ollama:EmbeddingModel"] = "wrong-dimension-model",
            ["Ollama:EmbeddingDimensions"] = "1024"
        }).Build();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        await using var provider = services.BuildServiceProvider();
        var service = new VectorSearchService(new FixedDimensionGenerator(768),
            provider.GetRequiredService<IServiceScopeFactory>(), config);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SearchAsync("test query", 10));

        Assert.Contains("returned 768 dims, expected 1024", error.Message);
        Assert.Contains("wrong-dimension-model", error.Message);
    }

    private sealed class FixedDimensionGenerator(int dimensions)
        : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                values.Select(_ => new Embedding<float>(new float[dimensions])).ToList()));

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }
}
