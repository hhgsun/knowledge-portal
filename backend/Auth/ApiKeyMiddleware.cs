using System.Security.Claims;
using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Auth;

public class ApiKeyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer kp_"))
        {
            var rawKey = authHeader["Bearer ".Length..];
            var allKeys = await db.ApiKeys.Include(k => k.User).ToListAsync();

            foreach (var key in allKeys)
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
