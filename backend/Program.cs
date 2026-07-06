using System.Text;
using System.Threading.RateLimiting;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Middleware;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.IdentityModel.Tokens;
using OllamaSharp;

var builder = WebApplication.CreateBuilder(args);

// ─── Database ────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

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

builder.Services.AddAuthorization();

// ─── Rate Limiting ───────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;

    var isTest = builder.Environment.EnvironmentName == "Testing";
    var authLimit = isTest ? 10000 : builder.Configuration.GetValue("RateLimiting:AuthLimit", 10);
    var searchLimit = isTest ? 10000 : builder.Configuration.GetValue("RateLimiting:SearchLimit", 30);

    options.AddFixedWindowLimiter("auth", opt =>
    {
        opt.PermitLimit = authLimit;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("search", opt =>
    {
        opt.PermitLimit = searchLimit;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
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
    var ollamaBaseUrl = builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
    var embeddingModel = builder.Configuration["Ollama:EmbeddingModel"] ?? "nomic-embed-text";
    var chatModel = builder.Configuration["Ollama:ChatModel"] ?? "llama3.2";

    var embeddingClient = new OllamaApiClient(new Uri(ollamaBaseUrl), embeddingModel);
    builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(embeddingClient);

    var chatClientInstance = new OllamaApiClient(new Uri(ollamaBaseUrl), chatModel);
    builder.Services.AddSingleton<IChatClient>(chatClientInstance);

    builder.Services.AddScoped<EmbeddingService>();
    builder.Services.AddSingleton<VectorSearchService>();
    builder.Services.AddScoped<RagService>();
    builder.Services.AddHostedService<EmbeddingBackgroundService>();
}

// ─── OpenAPI / Swagger ───────────────────────────────────────
builder.Services.AddOpenApi();

var app = builder.Build();

// ─── Middleware pipeline ─────────────────────────────────────
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/openapi/v1.json", "Knowledge Portal API v1"));
}

app.UseCors();
app.UseRateLimiter();

// API key middleware runs before auth — sets ClaimsPrincipal for kp_ tokens
app.UseMiddleware<ApiKeyMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// ─── Health Check ────────────────────────────────────────────
app.MapGet("/api/health", async (IConfiguration cfg, IServiceProvider sp) =>
{
    var ollamaEnabled = cfg.GetValue("Ollama:Enabled", false);
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
    using var dbScope = sp.CreateScope();
    var dbCtx = dbScope.ServiceProvider.GetRequiredService<AppDbContext>();
    var pendingEmbeddings = await dbCtx.Articles.CountAsync(a => a.Status == "published" && a.IndexedAt == null);
    return Results.Ok(new
    {
        status = "healthy",
        timestamp = DateTime.UtcNow.ToString("o"),
        ollamaStatus,
        embeddingModel = cfg["Ollama:EmbeddingModel"] ?? "nomic-embed-text",
        pendingEmbeddings
    });
});

// ─── Database init ───────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    // Enable WAL mode and busy timeout for concurrent access
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;");
    await DbInitializer.SeedAsync(db);
}

// ─── Ensure uploads directory ────────────────────────────────
var uploadsPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), app.Configuration["FileStorage:BasePath"] ?? "../data/uploads"));
Directory.CreateDirectory(uploadsPath);

app.Run();

// Marker class for WebApplicationFactory<Program> in integration tests
public partial class Program { }
