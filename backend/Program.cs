using System.Text;
using System.Diagnostics;
using System.Threading.RateLimiting;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Mcp;
using KnowledgePortal.Api.Middleware;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OllamaSharp;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ─── Reverse proxy (TLS terminates at the company proxy) ─────
// X-Forwarded-For/Proto are only honored from proxies listed in configuration —
// required for correct client IPs (rate limiting) and https scheme (HSTS).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
    foreach (var network in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
    {
        var parts = network.Split('/');
        options.KnownIPNetworks.Add(new System.Net.IPNetwork(
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

// ─── Metrics/traces (Prometheus + optional OTLP) ─────────────
builder.Services.AddSingleton<PortalMetrics>();
var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
if (string.IsNullOrWhiteSpace(otlpEndpoint))
    otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
var telemetry = builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics
    .AddAspNetCoreInstrumentation()
    .AddMeter(PortalMetrics.MeterName)
    .AddPrometheusExporter());
telemetry.WithTracing(tracing =>
{
    tracing.AddAspNetCoreInstrumentation().AddSource(PortalMetrics.ActivitySourceName);
    if (Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var endpoint))
        tracing.AddOtlpExporter(options => options.Endpoint = endpoint);
});

// ─── Database ────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        o =>
        {
            o.MigrationsHistoryTable("__ef_migrations_history");
            o.UseVector();
        }));

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
builder.Services.AddHttpClient<AttachmentProcessingService>((services, client) =>
    client.Timeout = TimeSpan.FromSeconds(Math.Max(5,
        services.GetRequiredService<IConfiguration>().GetValue("DocumentParsing:External:TimeoutSeconds", 180))));

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
    builder.Services.AddScoped<IRagRetriever, HybridRagRetriever>();
    builder.Services.AddSingleton<IRagTokenCounter, RagTokenCounter>();
    builder.Services.AddSingleton<IRagContextBuilder, RagContextBuilder>();
    builder.Services.AddSingleton<RagQueryUnderstandingService>();
    builder.Services.AddSingleton<RagContextExpansionService>();
    builder.Services.AddSingleton<LocalRagChunkReranker>();
    builder.Services.AddHttpClient<ExternalRagChunkReranker>();
    builder.Services.AddScoped<IRagChunkReranker>(sp => sp.GetRequiredService<ExternalRagChunkReranker>());
}

// ─── OpenAPI / Swagger ───────────────────────────────────────
builder.Services.AddOpenApi();

// ─── MCP Server ──────────────────────────────────────────────
// The official MCP ASP.NET Core SDK owns JSON-RPC, protocol negotiation and
// Streamable HTTP framing. Portal services provide tool discovery/execution.
builder.Services.AddHttpContextAccessor();
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation
        {
            Name = KnowledgePortalMcpServer.ServerName,
            Version = KnowledgePortalMcpServer.ServerVersion
        };
        options.ServerInstructions =
            "Use project/team/module tags to narrow context before general search. " +
            "Prefer approved and recently reviewed portal sources; cite returned articles.";
    })
    .WithHttpTransport(options =>
    {
        options.Stateless = true;
    })
    .WithListToolsHandler(KnowledgePortalMcpServer.ListToolsAsync)
    .WithCallToolHandler(KnowledgePortalMcpServer.CallToolAsync)
    .AddAuthorizationFilters();

// ─── Full-Text Search ────────────────────────────────────────
builder.Services.AddScoped<FullTextSearchService>();
builder.Services.AddScoped<SearchDiagnosticsService>();
builder.Services.AddHostedService<EmbeddingBackgroundService>();

// ─── Domain Services ─────────────────────────────────────────
builder.Services.AddScoped<ArticleService>();
builder.Services.AddScoped<ArticleMutationService>();
builder.Services.AddScoped<ContentTypeService>();
builder.Services.AddScoped<SearchExecutionService>();
builder.Services.AddScoped<AssistantRouterService>();
builder.Services.AddSingleton<AssistantPolicyService>();
builder.Services.AddScoped<AssistantOrchestratorService>();
builder.Services.AddScoped<AnalyticsReportService>();
builder.Services.AddScoped<TagService>();
builder.Services.AddScoped<ApiKeyService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<StatsService>();
builder.Services.AddScoped<UsageAnalyticsService>();
builder.Services.AddScoped<IndexJobQueue>();
builder.Services.AddSingleton<ISearchReranker, LocalSearchReranker>();
builder.Services.AddScoped<AttachmentStorageService>();
builder.Services.AddScoped<BulkTransferService>();
builder.Services.AddScoped<SourceImportService>();
builder.Services.AddScoped<RagEvaluationService>();
builder.Services.AddHostedService<RagEvaluationWorker>();
builder.Services.AddSingleton<OllamaHealthProbe>();
builder.Services.AddScoped<McpToolExecutor>();
builder.Services.AddScoped<ContentGovernanceService>();
builder.Services.AddScoped<McpAuditService>();
builder.Services.AddSingleton<McpResilienceService>();
builder.Services.AddSingleton<RagResilienceService>();

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

// Transport-independent HTTP guards retained around the official MCP endpoint.
// Kestrel enforces the endpoint metadata limit; the explicit Content-Length check
// also keeps TestServer and alternate hosts deterministic.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.Equals("/mcp", StringComparison.OrdinalIgnoreCase))
    {
        ctx.Response.Headers["X-Trace-Id"] = Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier;

        if (HttpMethods.IsPost(ctx.Request.Method) && ctx.Request.ContentLength > 262_144)
        {
            ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        var originValue = ctx.Request.Headers.Origin.ToString();
        if (!string.IsNullOrWhiteSpace(originValue)
            && (!Uri.TryCreate(originValue, UriKind.Absolute, out var origin)
                || !string.Equals(origin.Scheme, ctx.Request.Scheme, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(origin.Host, ctx.Request.Host.Host, StringComparison.OrdinalIgnoreCase)
                || origin.Port != (ctx.Request.Host.Port ?? (ctx.Request.IsHttps ? 443 : 80))))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
    }

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
app.UseMiddleware<UsageTrackingMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

app.MapMcp("/mcp")
    .RequireAuthorization(new AuthorizeAttribute())
    .RequireRateLimiting("mcp")
    .WithMetadata(new RequestSizeLimitAttribute(262_144));

// ─── Health Checks ───────────────────────────────────────────
// Liveness: process is up, no dependencies probed
app.MapGet("/api/health/live", () => Results.Ok(new { status = "alive" }));

// Readiness: DB unreachable → 503 "unhealthy" (orchestrator/deploy probes fail);
// only Ollama down → 200 "degraded" (search gracefully falls back to fulltext)
app.MapGet("/api/health", async (IConfiguration cfg, IServiceProvider sp, OllamaHealthProbe ollamaProbe,
    CancellationToken ct) =>
{
    var ollamaEnabled = cfg.GetValue("Ollama:Enabled", false);
    var embeddingModel = cfg["Ollama:EmbeddingModel"] ?? "bge-m3";
    var timestamp = DateTime.UtcNow.ToString("o");

    string ollamaStatus = "disabled";
    if (ollamaEnabled)
    {
        try
        {
            ollamaStatus = await ollamaProbe.CheckAsync(ct)
                ? "connected" : "unavailable";
        }
        catch { ollamaStatus = "unavailable"; }
    }

    int pendingEmbeddings;
    try
    {
        using var dbScope = sp.CreateScope();
        var dbCtx = dbScope.ServiceProvider.GetRequiredService<AppDbContext>();
        pendingEmbeddings = ollamaEnabled
            ? await dbCtx.Articles.CountAsync(a => a.Status == "published" && a.IndexedAt == null, ct)
            : 0;
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
    // Corpus backfill is deliberately left to the durable background queue. Application
    // readiness must not wait for attachment extraction across the entire knowledge base.
}

// ─── Ensure uploads directory ────────────────────────────────
var uploadsPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), app.Configuration["FileStorage:BasePath"] ?? "../data/uploads"));
Directory.CreateDirectory(uploadsPath);

app.Run();

// Marker class for WebApplicationFactory<Program> in integration tests
public partial class Program { }
