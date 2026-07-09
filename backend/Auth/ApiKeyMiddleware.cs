using System.Security.Claims;
using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Auth;

public class ApiKeyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        var apiKeyHeader = context.Request.Headers["X-API-Key"].ToString();
        if (!string.IsNullOrEmpty(apiKeyHeader) && apiKeyHeader.StartsWith("kp_"))
        {
            var rawKey = apiKeyHeader;
            var prefix = rawKey.Length >= 11 ? rawKey[3..11] : rawKey[3..]; // 8 chars after "kp_"

            // Prefix-indexed lookup: only load keys matching this prefix
            var candidates = await db.ApiKeys
                .Include(k => k.User)
                .Where(k => k.KeyPrefix == prefix)
                .ToListAsync();

            foreach (var key in candidates)
            {
                if (key.ExpiresAt.HasValue && key.ExpiresAt.Value < DateTime.UtcNow)
                    continue;

                if (BCrypt.Net.BCrypt.Verify(rawKey, key.KeyHash))
                {
                    // Update last used
                    key.LastUsedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();

                    var claims = new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, key.User.Id),
                        new Claim("id", key.User.Id),
                        new Claim(ClaimTypes.Name, key.User.Name),
                        new Claim(ClaimTypes.Email, key.User.Email),
                        new Claim(ClaimTypes.Role, key.User.Role),
                        new Claim("role", key.User.Role),
                        new Claim("source", "api-key"),
                        new Claim("apiKeyId", key.Id),
                        new Claim("apiKeyName", key.Name),
                    };

                    context.User = new ClaimsPrincipal(
                        new ClaimsIdentity(claims, "ApiKey"));
                    break;
                }
            }
        }

        await next(context);
    }
}
