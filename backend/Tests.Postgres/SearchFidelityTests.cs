using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using KnowledgePortal.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Pgvector;

namespace KnowledgePortal.Api.PostgresTests;

public class SearchFidelityTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [PostgresFact]
    public async Task VersionAllocator_ExecutesUpdateReturningWithoutSqlComposition()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateDb();
        var owner = User("version-u" + suffix);
        var article = Article("version-a" + suffix, owner.Id, "reference");
        db.AddRange(owner, article);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().Build();
        var service = new ArticleService(
            db,
            new FullTextSearchService(db, config, NullLogger<FullTextSearchService>.Instance),
            new TagService(db),
            new IndexJobQueue(db, config),
            config,
            NullLogger<ArticleService>.Instance);

        Assert.Equal(1, await service.AddVersionAsync(article.Id, article.Title, "![image](/api/attachments/one/download)", owner.Id, null));
        await db.SaveChangesAsync();
        Assert.Equal(2, await service.AddVersionAsync(article.Id, article.Title, "![image](/api/attachments/two/download)", owner.Id, null));
        await db.SaveChangesAsync();

        var versions = await db.ArticleVersions
            .Where(version => version.ArticleId == article.Id)
            .OrderBy(version => version.Version)
            .Select(version => version.Version)
            .ToArrayAsync();
        Assert.Equal([1, 2], versions);
    }

    [PostgresFact]
    public async Task QueueReindex_EagerlyPublishesFts_ButKeepsSemanticJobDurable()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateDb();
        var owner = User("eager-fts-u" + suffix);
        var article = Article("eager-fts-a" + suffix, owner.Id, "reference",
            "Anında bulunabilir gövde " + suffix);
        db.AddRange(owner, article);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().Build();
        var fts = new FullTextSearchService(db, config, NullLogger<FullTextSearchService>.Instance);
        await fts.InitializeAsync();
        var service = new ArticleService(db, fts, new TagService(db), new IndexJobQueue(db, config),
            config, NullLogger<ArticleService>.Instance);

        await service.QueueReindexAsync(article);
        await db.Entry(article).ReloadAsync();

        Assert.NotNull(article.FtsIndexedAt);
        Assert.Null(article.IndexedAt);
        Assert.Equal("pending", (await db.IndexJobs.FindAsync(article.Id))!.Status);
        var page = await fts.SearchPagedAsync("bulunabilir", null, 1, 10);
        Assert.Contains(article.Id, page.ArticleIds);
    }

    [PostgresFact]
    public async Task RagEvaluationQueue_ClaimsPendingRunWithoutSqlComposition()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateDb();
        var owner = User("rag-eval-u" + suffix);
        var dataset = new RagEvaluationDataset
        {
            Id = "rag-eval-d" + suffix,
            Name = "RAG evaluation " + suffix
        };
        var run = new RagEvaluationRun
        {
            Id = "rag-eval-r" + suffix,
            DatasetId = dataset.Id,
            RequestedById = owner.Id,
            TotalCases = 1
        };
        db.AddRange(owner, dataset, run);
        await db.SaveChangesAsync();

        var service = new RagEvaluationService(db, null!, new ConfigurationBuilder().Build());
        var claimedId = await service.ClaimNextAsync("postgres-fidelity-worker", TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.Equal(run.Id, claimedId);
        await db.Entry(run).ReloadAsync();
        Assert.Equal("running", run.Status);
        Assert.Equal("postgres-fidelity-worker", run.WorkerId);
        Assert.Equal(1, run.AttemptCount);
        Assert.NotNull(run.LeaseExpiresAt);
    }

    [PostgresFact]
    public async Task Migrations_CreatePgvectorAndRequiredIndexes()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        Assert.True(await Scalar<bool>(connection, "SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname='vector')"));
        Assert.True(await Scalar<bool>(connection, "SELECT to_regclass('ix_article_embeddings_embedding_hnsw') IS NOT NULL"));

        await using var db = fixture.CreateDb();
        var fts = new FullTextSearchService(db, new ConfigurationBuilder().Build(), NullLogger<FullTextSearchService>.Instance);
        await fts.InitializeAsync();
        Assert.True(await Scalar<bool>(connection, "SELECT to_regclass('idx_articles_search_vector') IS NOT NULL"));
        Assert.True(await Scalar<bool>(connection, """
            SELECT count(*) = 7 FROM information_schema.columns
            WHERE table_name = 'rag_evaluation_runs'
              AND column_name IN ('AttemptCount', 'CasesSnapshotJson', 'DatasetVersion',
                  'LeaseExpiresAt', 'RuntimeSnapshotJson', 'ThresholdsSnapshotJson', 'WorkerId')
            """));
    }

    [PostgresFact]
    public async Task Pgvector_ReturnsCorrectCosineRanking_AppliesFilter_AndUsesHnswPlan()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using (var db = fixture.CreateDb())
        {
            var owner = User("u" + suffix); db.Users.Add(owner);
            var best = Article("best" + suffix, owner.Id, "runbook");
            var second = Article("second" + suffix, owner.Id, "reference");
            var noise = Article("noise" + suffix, owner.Id, "runbook");
            db.Articles.AddRange(best, second, noise); await db.SaveChangesAsync();
            db.ArticleEmbeddings.AddRange(
                Embedding(best.Id, Vector(1, 0)), Embedding(second.Id, Vector(.8f, .6f)), Embedding(noise.Id, Vector(0, 1)));
            await db.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(fixture.ConnectionString, n => n.UseVector()));
        await using var provider = services.BuildServiceProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Ollama:VectorCandidateMultiplier"] = "10", ["Ollama:HnswIterativeScan"] = "relaxed_order" }).Build();
        var search = new VectorSearchService(new DeterministicEmbeddingGenerator(),
            provider.GetRequiredService<IServiceScopeFactory>(), config);

        var ranked = await search.SearchAsync("query", 3, minScore: 0);
        Assert.Equal("best" + suffix, ranked[0].ArticleId);
        Assert.True(ranked[0].Score > ranked[1].Score);
        var filtered = await search.SearchAsync("query", 3, minScore: 0, filter: new ArticleFilter(ContentTypes: ["runbook"]));
        Assert.DoesNotContain(filtered, x => x.ArticleId == "second" + suffix);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        await using (var disableSeq = new NpgsqlCommand("SET enable_seqscan=off", connection)) await disableSeq.ExecuteNonQueryAsync();
        var queryVector = new Vector(Vector(1, 0)).ToString();
        var plan = await Plan(connection, $"EXPLAIN (COSTS OFF) SELECT \"ArticleId\" FROM article_embeddings ORDER BY \"Embedding\" <=> '{queryVector}'::vector LIMIT 2");
        Assert.Contains("ix_article_embeddings_embedding_hnsw", plan, StringComparison.OrdinalIgnoreCase);
    }

    [PostgresFact]
    public async Task PostgreSqlFts_UsesTurkishStemming_Filtering_AndGinIndex()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await using var db = fixture.CreateDb();
        var owner = User("fts-u" + suffix); db.Users.Add(owner);
        var expected = Article("fts-best" + suffix, owner.Id, "runbook", "Ağ bağlantıları için güvenli kurulum adımları");
        var excluded = Article("fts-other" + suffix, owner.Id, "policy", "Bağlantılar için farklı politika");
        db.Articles.AddRange(expected, excluded); await db.SaveChangesAsync();
        var fts = new FullTextSearchService(db, new ConfigurationBuilder().Build(), NullLogger<FullTextSearchService>.Instance);
        await fts.InitializeAsync(); await fts.SyncArticleAsync(expected); await fts.SyncArticleAsync(excluded);

        var page = await fts.SearchPagedAsync("bağlantı", new ArticleFilter(ContentTypes: ["runbook"]), 1, 10);
        Assert.Contains(expected.Id, page.ArticleIds);
        Assert.DoesNotContain(excluded.Id, page.ArticleIds);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        await using (var disableSeq = new NpgsqlCommand("SET enable_seqscan=off", connection)) await disableSeq.ExecuteNonQueryAsync();
        var plan = await Plan(connection, "EXPLAIN (COSTS OFF) SELECT \"Id\" FROM articles WHERE search_vector @@ to_tsquery('turkish','baglanti')");
        Assert.Contains("idx_articles_search_vector", plan, StringComparison.OrdinalIgnoreCase);
    }

    private static User User(string id) => new() { Id = id, Name = id, Slug = id, Email = id + "@test.local", PasswordHash = "x", Role = "admin" };
    private static Article Article(string id, string owner, string type, string? content = null) => new()
    { Id = id, Title = id, Slug = id, OwnerId = owner, Status = "published", ContentType = type, Content = content ?? id, PublishedAt = DateTime.UtcNow, IndexedAt = DateTime.UtcNow };
    private static ArticleEmbedding Embedding(string articleId, float[] vector) => new()
    { ArticleId = articleId, ChunkIndex = 0, Embedding = new Vector(vector), ModelName = "fidelity", TextHash = articleId, Content = articleId, Dimensions = 1024 };
    private static float[] Vector(float x, float y) { var value = new float[1024]; value[0] = x; value[1] = y; return value; }
    private static async Task<T> Scalar<T>(NpgsqlConnection connection, string sql) { await using var command = new NpgsqlCommand(sql, connection); return (T)(await command.ExecuteScalarAsync())!; }
    private static async Task<string> Plan(NpgsqlConnection connection, string sql)
    { await using var command = new NpgsqlCommand(sql, connection); await using var reader = await command.ExecuteReaderAsync(); var lines = new List<string>(); while (await reader.ReadAsync()) lines.Add(reader.GetString(0)); return string.Join('\n', lines); }
}
