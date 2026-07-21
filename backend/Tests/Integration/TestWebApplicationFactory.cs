using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace KnowledgePortal.Api.Tests.Integration;

/// <summary>
/// Docker-free test host: the app runs against an EF Core InMemory database (unique
/// per test class), with the pgvector-backed vector search and the Ollama AI clients
/// replaced by deterministic in-process fakes, and the embedding background service
/// removed. Postgres-only paths (FTS raw SQL, migrations) degrade to LINQ / EnsureCreated
/// automatically via provider checks in the app.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"kp_test_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Each factory hosts its own app instance in the same process — the file logger
        // opens its log file exclusively, so every host needs a unique log directory.
        builder.UseSetting("Logging:FilePath", Path.Combine(Path.GetTempPath(), "kp-test-logs", _dbName));

        builder.ConfigureServices(services =>
        {
            // Swap the Npgsql DbContext for an isolated InMemory database. InMemory gets its
            // own EF internal service provider so its provider services don't collide with the
            // app's already-registered Npgsql provider ("only a single provider" error).
            var efInternal = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            // Remove every AppDbContext-related registration: the context itself,
            // DbContextOptions<AppDbContext>, and (EF Core 10) the provider-config service
            // IDbContextOptionsConfiguration<AppDbContext> that re-applies UseNpgsql.
            var dbDescriptors = services
                .Where(d => d.ServiceType == typeof(AppDbContext)
                         || d.ServiceType == typeof(DbContextOptions)
                         || (d.ServiceType.IsGenericType
                             && d.ServiceType.GetGenericArguments().Contains(typeof(AppDbContext))))
                .ToList();
            foreach (var d in dbDescriptors)
                services.Remove(d);
            services.AddDbContext<AppDbContext>(o =>
                o.UseInMemoryDatabase(_dbName).UseInternalServiceProvider(efInternal));

            // Deterministic in-process AI: fake embedding generator + chat client, and a
            // fake vector search so semantic/hybrid/RAG run without pgvector.
            services.RemoveAll<IEmbeddingGenerator<string, Embedding<float>>>();
            services.RemoveAll<IChatClient>();
            services.RemoveAll<IVectorSearchService>();
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new FakeEmbeddingGenerator());
            services.AddSingleton<IChatClient>(new FakeChatClient());
            services.AddSingleton<IVectorSearchService, FakeVectorSearchService>();

            // The embedding background service uses Postgres-only raw SQL (xmin) — drop it;
            // the fake vector search reads live article data instead.
            var hosted = services.SingleOrDefault(d => d.ImplementationType == typeof(EmbeddingBackgroundService));
            if (hosted != null)
                services.Remove(hosted);
        });
    }
}
