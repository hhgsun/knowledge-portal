using System.Text;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

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

// ─── CORS ────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddControllers();

var app = builder.Build();

// ─── Middleware pipeline ─────────────────────────────────────
app.UseCors();

// API key middleware runs before auth — sets ClaimsPrincipal for kp_ tokens
app.UseMiddleware<ApiKeyMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// ─── Database init ───────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();    // Enable WAL mode and busy timeout for concurrent access
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;");    await DbInitializer.SeedAsync(db);
}

app.Run();
