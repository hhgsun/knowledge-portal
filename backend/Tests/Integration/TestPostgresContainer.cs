using Testcontainers.PostgreSql;

namespace KnowledgePortal.Api.Tests.Integration;

/// <summary>
/// Single shared PostgreSQL (pgvector) container for the whole test run.
/// Started lazily on first use; Testcontainers' resource reaper (Ryuk) removes it
/// after the test process exits, so no explicit disposal is needed. Each
/// TestWebApplicationFactory creates its own isolated database inside this container.
/// </summary>
internal static class TestPostgresContainer
{
    private static readonly Lazy<PostgreSqlContainer> _container = new(() =>
    {
        var container = new PostgreSqlBuilder()
            .WithImage("pgvector/pgvector:pg17")
            .WithUsername("kp_test")
            .WithPassword("kp_test")
            .WithDatabase("postgres")
            .Build();
        container.StartAsync().GetAwaiter().GetResult();
        return container;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Connection string to the container's admin database ("postgres").</summary>
    public static string AdminConnectionString => _container.Value.GetConnectionString();
}
