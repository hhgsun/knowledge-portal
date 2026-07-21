using System.Text;
using System.Threading.RateLimiting;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Mcp;
using KnowledgePortal.Api.Middleware;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Server;
using OllamaSharp;
using OpenTelemetry.Metrics;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ─── Reverse proxy (TLS terminates at the company proxy) ─────
// X-Forwarded-For/Proto are only honored from proxies listed in configuration —
// required for correct client IPs (rate limiting) and https scheme (HSTS).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
    foreach (var network in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
    {
        var parts = network.Split('/');
        options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(
            System.Net.IPAddress.Parse(parts[0]), int.Parse(parts[1])));
    }
});

builder.Services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(365));

// ─── Logging (Serilog: console + rolling JSON file) ──────────
// File name MUST stay log_YYYYMMDD.log — LogsController's pattern validation and
// today-file protection depend on it (base name "log_.log" + daily rolling produces
// exactly that; size-based rolling stays off so no _001 suffix ever appears).
var logsPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(),
    builder.Configuration["Logging:FilePath"] ?? "../data/logs"));
Directory.CreateDirectory(logsPath);
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration) // "Serilog" section: MinimumLevel + overrides
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(new Serilog.Formatting.Compact.CompactJsonFormatter(),
        Path.Combine(logsPath, "log_.log"),
        rollingInterval: Serilog.RollingInterval.Day,
        retainedFileCountLimit: ctx.Configuration.GetValue("Logging:RetainedFileCountLimit", 30),
        rollOnFileSizeLimit: false));

// ─── Metrics (OpenTelemetry → Prometheus /metrics) ───────────
builder.Services.AddSingleton<PortalMetrics>();
builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics
    .AddAspNetCoreInstrumentation()
    .AddMeter(PortalMetrics.MeterName)
    .AddPrometheusExporter());

// ─── Database ────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        o => o.UseVector()));

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// ─── Authentication ──────────────────────────────────────────
builder.Services.AddSingleton<JwtService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Secret"]!))
        };
    });

builder.Services.AddAuthorization(options =>
{
    // MCP tools can be called with JWT or API Key
    options.AddPolicy("McpAuthorization", policy =>
        policy.RequireAssertion(context =>
            context.User.Identity?.IsAuthenticated ?? false));
});

// ─── Rate Limiting ───────────────────────────────────────────
// Partitioned per client (API key > user > client IP) so one noisy caller can't
// exhaust everyone's budget and login brute-force is throttled per source IP.
// Requires ForwardedHeaders (above) for real client IPs behind the reverse proxy,
// and UseRateLimiter after authentication so the principal is available.
static string RateLimitClientKey(HttpContext ctx) =>
    ctx.User.FindFirst("apiKeyId")?.Value
    ?? ctx.User.FindFirst("id")?.Value
    ?? ctx.Connection.RemoteIpAddress?.ToString()
    ?? "unknown";

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;

    var isTest = builder.Environment.EnvironmentName == "Testing";
    var authLimit = isTest ? 10000 : builder.Configuration.GetValue("RateLimiting:AuthLimit", 10);
    var searchLimit = isTest ? 10000 : builder.Configuration.GetValue("RateLimiting:SearchLimit", 30);
    var mcpLimit = isTest ? 10000 : builder.Configuration.GetValue("RateLimiting:McpLimit", 60);

    void AddPartitionedPolicy(string name, int permitLimit) =>
        options.AddPolicy(name, ctx => RateLimitPartition.GetFixedWindowLimiter(
            RateLimitClientKey(ctx),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    AddPartitionedPolicy("auth", authLimit);
    AddPartitionedPolicy("search", searchLimit);
    AddPartitionedPolicy("mcp", mcpLimit);
});

// ─── CORS ────────────────────────────────────────────────────
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddControllers();

// ─── Ollama AI Services ──────────────────────────────────────
if (builder.Configuration.GetValue("Ollama:Enabled", false))
{
    // OllamaSharp appends relative paths (api/chat, api/embed); without a trailing slash
    // HttpClient drops the base URL's last path segment when resolving them
    var ollamaBaseUrl = builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
    if (!ollamaBaseUrl.EndsWith('/'))
        ollamaBaseUrl += "/";
    // Defaults must match the vector(1024) column — bge-m3 produces 1024-dim embeddings.
    // A different-dimension model requires migrating the column (see Ollama:EmbeddingDimensions guard).
    var embeddingModel = builder.Configuration["Ollama:EmbeddingModel"] ?? "bge-m3";
    var chatModel = builder.Configuration["Ollama:ChatModel"] ?? "qwen2.5vl:7b";
    // Local LLM cold starts (model load + generation) routinely exceed HttpClient's
    // default 100s timeout — make it configurable
    var ollamaTimeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Ollama:TimeoutSeconds", 300));

    var embeddingClient = new OllamaApiClient(
        new HttpClient { BaseAddress = new Uri(ollamaBaseUrl), Timeout = ollamaTimeout }, embeddingModel);
    builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(embeddingClient);

    var chatClientInstance = new OllamaApiClient(
        new HttpClient { BaseAddress = new Uri(ollamaBaseUrl), Timeout = ollamaTimeout }, chatModel);
    builder.Services.AddSingleton<IChatClient>(chatClientInstance);

    builder.Services.AddScoped<EmbeddingService>();
    builder.Services.AddSingleton<IVectorSearchService, VectorSearchService>();
    builder.Services.AddScoped<RagService>();
    builder.Services.AddSingleton<EmbeddingFailureTracker>();
    builder.Services.AddHostedService<EmbeddingBackgroundService>();
}

// ─── OpenAPI / Swagger ───────────────────────────────────────
builder.Services.AddOpenApi();

// ─── MCP Server ──────────────────────────────────────────────
// MCP tools are exposed via REST API at /api/mcp/tools/call
// See McpController for implementation

// ─── Full-Text Search ────────────────────────────────────────
builder.Services.AddScoped<FullTextSearchService>();

// ─── Domain Services ─────────────────────────────────────────
builder.Services.AddScoped<ArticleService>();
builder.Services.AddScoped<TagService>();
builder.Services.AddScoped<ApiKeyService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<StatsService>();
builder.Services.AddScoped<McpToolExecutor>();

var app = builder.Build();

// ─── Middleware pipeline ─────────────────────────────────────
// ForwardedHeaders must run first so everything downstream (HSTS, rate limiting,
// logging) sees the real client IP and https scheme from behind the company proxy.
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
    app.UseHsts();

// Security headers on every response (API responses; SPA headers are set in nginx)
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.XContentTypeOptions = "nosniff";
    ctx.Response.Headers.XFrameOptions = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/openapi/v1.json", "Knowledge Portal API v1"));
}

app.UseCors();

// API key middleware runs before auth — sets ClaimsPrincipal for kp_ tokens.
// Rate limiter runs after auth so partitioning can key on apiKeyId/userId.
app.UseMiddleware<ApiKeyMiddleware>();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

// ─── Health Checks ───────────────────────────────────────────
// Liveness: process is up, no dependencies probed
app.MapGet("/api/health/live", () => Results.Ok(new { status = "alive" }));

// Readiness: DB unreachable → 503 "unhealthy" (orchestrator/deploy probes fail);
// only Ollama down → 200 "degraded" (search gracefully falls back to fulltext)
app.MapGet("/api/health", async (IConfiguration cfg, IServiceProvider sp) =>
{
    var ollamaEnabled = cfg.GetValue("Ollama:Enabled", false);
    var embeddingModel = cfg["Ollama:EmbeddingModel"] ?? "bge-m3";
    var timestamp = DateTime.UtcNow.ToString("o");

    string ollamaStatus = "disabled";
    if (ollamaEnabled)
    {
        try
        {
            using var scope = sp.CreateScope();
            var embeddingService = scope.ServiceProvider.GetService<EmbeddingService>();
            ollamaStatus = embeddingService != null && await embeddingService.IsOllamaAvailableAsync()
                ? "connected" : "unavailable";
        }
        catch { ollamaStatus = "unavailable"; }
    }

    int pendingEmbeddings;
    try
    {
        using var dbScope = sp.CreateScope();
        var dbCtx = dbScope.ServiceProvider.GetRequiredService<AppDbContext>();
        pendingEmbeddings = await dbCtx.Articles.CountAsync(a => a.Status == "published" && a.IndexedAt == null);
    }
    catch
    {
        return Results.Json(new
        {
            status = "unhealthy",
            timestamp,
            ollamaStatus,
            embeddingModel,
            pendingEmbeddings = (int?)null,
            error = "database unreachable"
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var status = ollamaEnabled && ollamaStatus == "unavailable" ? "degraded" : "healthy";
    return Results.Ok(new
    {
        status,
        timestamp,
        ollamaStatus,
        embeddingModel,
        pendingEmbeddings
    });
});

// ─── Prometheus scrape endpoint ──────────────────────────────
// /metrics is not under /api/ — nginx only proxies /api/, so it stays internal
// (docker network / host scrapers only)
app.MapPrometheusScrapingEndpoint();
_ = app.Services.GetRequiredService<PortalMetrics>(); // instantiate so observable gauges register

// ─── Database init ───────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (db.Database.IsRelational())
    {
        // Ensure the PostgreSQL database exists before applying migrations
        {
            var connStr = builder.Configuration.GetConnectionString("DefaultConnection")!;
            var csb = new Npgsql.NpgsqlConnectionStringBuilder();
            csb.PersistSecurityInfo = true;
            csb.ConnectionString = connStr;
            var dbName = csb.Database;
            csb.Database = "postgres";
            using var conn = new Npgsql.NpgsqlConnection(csb.ConnectionString);
            await conn.OpenAsync();
            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = $"SELECT 1 FROM pg_database WHERE datname = '{dbName}'";
            var exists = await checkCmd.ExecuteScalarAsync();
            if (exists == null)
            {
                using var createCmd = conn.CreateCommand();
                createCmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
                await createCmd.ExecuteNonQueryAsync();
            }
        }

        await db.Database.MigrateAsync();
    }
    else
    {
        // Non-relational provider (EF InMemory, used by the Docker-free test suite):
        // migrations/raw-SQL FTS init don't apply — just materialize the schema.
        await db.Database.EnsureCreatedAsync();
    }

    await DbInitializer.SeedAsync(db);

    // FTS infrastructure is PostgreSQL raw SQL; the service no-ops on non-relational providers.
    var ftsService = scope.ServiceProvider.GetRequiredService<FullTextSearchService>();
    await ftsService.InitializeAsync();
    await ftsService.RebuildAsync();
}

// ─── Ensure uploads directory ────────────────────────────────
var uploadsPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), app.Configuration["FileStorage:BasePath"] ?? "../data/uploads"));
Directory.CreateDirectory(uploadsPath);

app.Run();

// Marker class for WebApplicationFactory<Program> in integration tests
public partial class Program { }
