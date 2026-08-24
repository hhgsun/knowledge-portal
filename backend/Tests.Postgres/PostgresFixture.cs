using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace KnowledgePortal.Api.PostgresTests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RAG_FIDELITY_CONNECTION_STRING")))
            Skip = "Set RAG_FIDELITY_CONNECTION_STRING to run real PostgreSQL/pgvector tests.";
    }
}

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly string? _baseConnection = Environment.GetEnvironmentVariable("RAG_FIDELITY_CONNECTION_STRING");
    public string Schema { get; } = "kp_fidelity_" + Guid.NewGuid().ToString("N");
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        if (_baseConnection == null) return;
        await using (var connection = new NpgsqlConnection(_baseConnection))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE SCHEMA \"{Schema}\"", connection);
            await command.ExecuteNonQueryAsync();
        }
        var builder = new NpgsqlConnectionStringBuilder(_baseConnection) { SearchPath = $"{Schema},public" };
        ConnectionString = builder.ConnectionString;
        await using var db = CreateDb();
        await db.Database.MigrateAsync();
    }

    public AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(ConnectionString, o =>
        {
            // Keep migration history inside the disposable schema. EF Core 10 treats this
            // test-only history-table schema override as a model delta even though the
            // application model matches its production snapshot (`dotnet ef migrations
            // has-pending-model-changes` remains the authoritative drift check).
            o.MigrationsHistoryTable("__ef_migrations_history", Schema);
            o.UseVector();
        })
        .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
        .Options);

    public async Task DisposeAsync()
    {
        if (_baseConnection == null) return;
        await using var connection = new NpgsqlConnection(_baseConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{Schema}\" CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }
}
