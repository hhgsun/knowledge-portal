using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace KnowledgePortal.Api.Tests.Integration;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"kp_test_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Per-class isolated database inside the shared Testcontainers PostgreSQL instance.
        // Overriding configuration (not the DbContext registration) also covers the
        // "ensure database exists" startup block in Program.cs, which reads the same key.
        var csb = new NpgsqlConnectionStringBuilder(TestPostgresContainer.AdminConnectionString)
        {
            Database = _dbName
        };
        builder.UseSetting("ConnectionStrings:DefaultConnection", csb.ConnectionString);

        // Each factory hosts its own app instance in the same process — the file logger
        // opens its log file exclusively, so every host needs a unique log directory.
        builder.UseSetting("Logging:FilePath", Path.Combine(Path.GetTempPath(), "kp-test-logs", _dbName));

        builder.ConfigureServices(services =>
        {
            // Replace the Ollama-backed AI singletons with deterministic fakes so the
            // full embedding/RAG pipeline runs without any network dependency.
            services.RemoveAll<IEmbeddingGenerator<string, Embedding<float>>>();
            services.RemoveAll<IChatClient>();
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new FakeEmbeddingGenerator());
            services.AddSingleton<IChatClient>(new FakeChatClient());
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Drop the per-class database — best-effort; the container itself is
            // removed by the Testcontainers reaper after the test process exits.
            try
            {
                using var conn = new NpgsqlConnection(TestPostgresContainer.AdminConnectionString);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"""
                    SELECT pg_terminate_backend(pid) FROM pg_stat_activity
                    WHERE datname = '{_dbName}' AND pid <> pg_backend_pid();
                    """;
                cmd.ExecuteNonQuery();
                cmd.CommandText = $"DROP DATABASE IF EXISTS \"{_dbName}\";";
                cmd.ExecuteNonQuery();
            }
            catch
            {
                // Best-effort cleanup
            }
        }
        base.Dispose(disposing);
    }
}
