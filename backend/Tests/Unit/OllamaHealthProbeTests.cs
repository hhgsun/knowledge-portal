using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgePortal.Api.Tests.Unit;

public class OllamaHealthProbeTests
{
    [Fact]
    public async Task CheckAsync_CachesSuccessfulProbe()
    {
        var generator = new ProbeEmbeddingGenerator();
        var (probe, provider) = Build(generator,
            ("Health:OllamaProbeTimeoutSeconds", "2"), ("Health:OllamaProbeCacheSeconds", "30"));
        await using var disposable = provider;

        Assert.True(await probe.CheckAsync(CancellationToken.None));
        Assert.True(await probe.CheckAsync(CancellationToken.None));
        Assert.Equal(1, generator.Calls);
    }

    [Fact]
    public async Task CheckAsync_BoundsSlowModelProbe()
    {
        var generator = new ProbeEmbeddingGenerator(TimeSpan.FromSeconds(10));
        var (probe, provider) = Build(generator,
            ("Health:OllamaProbeTimeoutSeconds", "1"), ("Health:OllamaProbeCacheSeconds", "30"));
        await using var disposable = provider;
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var available = await probe.CheckAsync(CancellationToken.None);

        Assert.False(available);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task CheckAsync_RejectsEmbeddingWithUnexpectedDimensions()
    {
        var generator = new ProbeEmbeddingGenerator(dimensions: 768);
        var (probe, provider) = Build(generator,
            ("Ollama:EmbeddingDimensions", "1024"),
            ("Health:OllamaProbeTimeoutSeconds", "2"),
            ("Health:OllamaProbeCacheSeconds", "30"));
        await using var disposable = provider;

        Assert.False(await probe.CheckAsync(CancellationToken.None));
        Assert.Equal(1, generator.Calls);
    }

    private static (OllamaHealthProbe Probe, ServiceProvider Provider) Build(
        IEmbeddingGenerator<string, Embedding<float>> generator,
        params (string Key, string Value)[] settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings.Select(x =>
            new KeyValuePair<string, string?>(x.Key, x.Value))).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();
        services.AddDbContext<AppDbContext>(x => x.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        services.AddSingleton(generator);
        services.AddScoped<EmbeddingService>();
        var provider = services.BuildServiceProvider();
        return (new OllamaHealthProbe(provider.GetRequiredService<IServiceScopeFactory>(), config,
            NullLogger<OllamaHealthProbe>.Instance), provider);
    }

    private sealed class ProbeEmbeddingGenerator(TimeSpan? delay = null, int dimensions = 1024)
        : IEmbeddingGenerator<string, Embedding<float>>
    {
        public int Calls { get; private set; }

        public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (delay != null) await Task.Delay(delay.Value, cancellationToken);
            return new GeneratedEmbeddings<Embedding<float>>(
                values.Select(_ => new Embedding<float>(new float[dimensions])).ToList());
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }
}
