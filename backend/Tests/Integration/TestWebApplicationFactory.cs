using KnowledgePortal.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgePortal.Api.Tests.Integration;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"kp_test_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            // Use a temporary PostgreSQL database for test isolation
            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseNpgsql($"Host=192.168.84.21;Port=5432;Database={_dbName};Username=hasan.gunes;Password=Hasan123.!");
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Drop the temporary test database
            try
            {
                using var conn = new Npgsql.NpgsqlConnection("Host=192.168.84.21;Port=5432;Database=postgres;Username=hasan.gunes;Password=Hasan123.!");
                conn.Open();
                using var cmd = conn.CreateCommand();
                // Terminate existing connections
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
