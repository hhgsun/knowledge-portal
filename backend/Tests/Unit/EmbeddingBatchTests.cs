using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using KnowledgePortal.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgePortal.Api.Tests.Unit;

/// <summary>
/// Chunk batching, covered on its own because the method that uses it —
/// <see cref="EmbeddingService.EmbedArticleAsync"/> — reads xmin through raw SQL and so cannot
/// run on the Docker-free InMemory provider. The behaviour under test is what keeps a long
/// document indexable: its chunks must be embedded in bounded requests rather than one request
/// whose duration grows with the document, which is how large attachments used to exhaust
/// Ollama:TimeoutSeconds and leave the article queued forever.
/// </summary>
public class EmbeddingBatchTests
{
    /// <summary>Records the size of every request instead of doing real work.</summary>
    private sealed class RecordingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public List<int> RequestSizes { get; } = [];
        public List<string> Received { get; } = [];

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var batch = values.ToList();
            RequestSizes.Add(batch.Count);
            Received.AddRange(batch);
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                batch.Select(_ => new Embedding<float>(new float[4])).ToList()));
        }

        public void Dispose() { }
        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType.IsInstanceOfType(this) ? this : null;
    }

    private static EmbeddingService BuildService(RecordingGenerator generator, int chunkBatchSize)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ollama:ChunkBatchSize"] = chunkBatchSize.ToString()
            })
            .Build();

        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

        return new EmbeddingService(generator, db, config, NullLogger<EmbeddingService>.Instance);
    }

    [Fact]
    public async Task GenerateInBatches_SplitsIntoRequestsOfAtMostBatchSize()
    {
        var generator = new RecordingGenerator();
        var service = BuildService(generator, chunkBatchSize: 16);
        var chunks = Enumerable.Range(0, 50).Select(i => $"chunk{i}").ToList();

        var results = await service.GenerateInBatchesAsync(chunks, CancellationToken.None);

        Assert.Equal(50, results.Count);
        Assert.Equal([16, 16, 16, 2], generator.RequestSizes);
        Assert.All(generator.RequestSizes, size => Assert.True(size <= 16));
    }

    [Fact]
    public async Task GenerateInBatches_PreservesChunkOrderAcrossRequests()
    {
        var generator = new RecordingGenerator();
        var service = BuildService(generator, chunkBatchSize: 3);
        var chunks = Enumerable.Range(0, 10).Select(i => $"chunk{i}").ToList();

        await service.GenerateInBatchesAsync(chunks, CancellationToken.None);

        // Order matters: the results are zipped back onto ChunkIndex positions by their offset.
        Assert.Equal(chunks, generator.Received);
    }

    [Fact]
    public async Task GenerateInBatches_SingleRequestWhenChunksFitInOneBatch()
    {
        var generator = new RecordingGenerator();
        var service = BuildService(generator, chunkBatchSize: 16);

        var results = await service.GenerateInBatchesAsync(["only", "three", "chunks"], CancellationToken.None);

        Assert.Equal(3, results.Count);
        Assert.Equal([3], generator.RequestSizes);
    }

    [Fact]
    public async Task GenerateInBatches_MisconfiguredBatchSizeStillMakesProgress()
    {
        // A 0 or negative setting would otherwise loop forever without advancing the offset.
        var generator = new RecordingGenerator();
        var service = BuildService(generator, chunkBatchSize: 0);

        var results = await service.GenerateInBatchesAsync(["a", "b", "c"], CancellationToken.None);

        Assert.Equal(3, results.Count);
        Assert.Equal([1, 1, 1], generator.RequestSizes);
    }

    [Fact]
    public async Task GenerateInBatches_NoChunksMakesNoRequests()
    {
        var generator = new RecordingGenerator();
        var service = BuildService(generator, chunkBatchSize: 16);

        var results = await service.GenerateInBatchesAsync([], CancellationToken.None);

        Assert.Empty(results);
        Assert.Empty(generator.RequestSizes);
    }

    [Fact]
    public async Task InvalidateStaleModel_MarksArticleDirtyWithoutDeletingPreviousChunks()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ollama:EmbeddingModel"] = "new-model",
            ["Ollama:EmbeddingDimensions"] = "1024",
            ["Ollama:ChunkingVersion"] = "markdown-structure-v1"
        }).Build();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        db.Articles.Add(new Article
        {
            Id = "article-1", Title = "Profile transition", Slug = "profile-transition",
            OwnerId = "owner", Status = "published", IndexedAt = DateTime.UtcNow
        });
        db.ArticleEmbeddings.Add(new ArticleEmbedding
        {
            Id = "old-chunk", ArticleId = "article-1", ChunkIndex = 0, ModelName = "old-model",
            TextHash = "legacy-hash", Dimensions = 1024, Content = "previous searchable content"
        });
        await db.SaveChangesAsync();
        var service = new EmbeddingService(new RecordingGenerator(), db, config,
            NullLogger<EmbeddingService>.Instance);

        var invalidated = await service.InvalidateStaleModelAsync();

        Assert.Equal(1, invalidated);
        Assert.Null((await db.Articles.FindAsync("article-1"))!.IndexedAt);
        Assert.NotNull(await db.ArticleEmbeddings.FindAsync("old-chunk"));
    }

    [Fact]
    public void ComputeIndexProfile_ChangesWhenChunkingVersionChanges()
    {
        IConfiguration Config(string version) => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ollama:EmbeddingModel"] = "bge-m3",
                ["Ollama:EmbeddingDimensions"] = "1024",
                ["Ollama:ParentChunkTargetWords"] = "1000",
                ["Ollama:ChildChunkTargetWords"] = "220",
                ["Ollama:ChildChunkOverlapWords"] = "40",
                ["Ollama:ChunkingVersion"] = version
            }).Build();

        Assert.NotEqual(EmbeddingService.ComputeIndexProfile(Config("v1")),
            EmbeddingService.ComputeIndexProfile(Config("v2")));
    }

    [Theory]
    [InlineData("900", "220", "40")]
    [InlineData("1000", "240", "40")]
    [InlineData("1000", "220", "30")]
    public void ComputeIndexProfile_ChangesForEveryHierarchyBoundary(string parent, string child,
        string overlap)
    {
        IConfiguration Config(string parentValue, string childValue, string overlapValue) =>
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ollama:EmbeddingModel"] = "bge-m3",
                ["Ollama:EmbeddingDimensions"] = "1024",
                ["Ollama:ParentChunkTargetWords"] = parentValue,
                ["Ollama:ChildChunkTargetWords"] = childValue,
                ["Ollama:ChildChunkOverlapWords"] = overlapValue,
                ["Ollama:ChunkingVersion"] = "hierarchical-parent-child-v2"
            }).Build();

        var baseline = EmbeddingService.ComputeIndexProfile(Config("1000", "220", "40"));
        Assert.NotEqual(baseline, EmbeddingService.ComputeIndexProfile(Config(parent, child, overlap)));
    }

    [Fact]
    public void ComputeIndexProfile_ChangesWhenMultimodalExtractionChanges()
    {
        IConfiguration Config(bool external, bool vision) => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ollama:EmbeddingModel"] = "bge-m3",
                ["Ollama:EmbeddingDimensions"] = "1024",
                ["Ollama:Enabled"] = "true",
                ["Ollama:ChatModel"] = "qwen2.5vl:7b",
                ["DocumentParsing:External:Enabled"] = external.ToString(),
                ["DocumentParsing:Vision:Enabled"] = vision.ToString()
            }).Build();

        var nativeVision = EmbeddingService.ComputeIndexProfile(Config(false, true));
        Assert.NotEqual(nativeVision, EmbeddingService.ComputeIndexProfile(Config(true, true)));
        Assert.NotEqual(nativeVision, EmbeddingService.ComputeIndexProfile(Config(false, false)));
    }

    [Fact]
    public void ComputeIndexProfile_ChangesWhenExtractionOrVisionBudgetChanges()
    {
        IConfiguration Config(string characters, string outputTokens) => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ollama:EmbeddingModel"] = "bge-m3",
                ["Ollama:EmbeddingDimensions"] = "1024",
                ["Ollama:Enabled"] = "true",
                ["Ollama:ChatModel"] = "qwen2.5vl:7b",
                ["DocumentParsing:Vision:Enabled"] = "true",
                ["DocumentParsing:Vision:MaxOutputTokens"] = outputTokens,
                ["FileStorage:MaxExtractedCharacters"] = characters
            }).Build();

        var baseline = EmbeddingService.ComputeIndexProfile(Config("50000", "700"));
        Assert.NotEqual(baseline, EmbeddingService.ComputeIndexProfile(Config("75000", "700")));
        Assert.NotEqual(baseline, EmbeddingService.ComputeIndexProfile(Config("50000", "900")));
    }
}
