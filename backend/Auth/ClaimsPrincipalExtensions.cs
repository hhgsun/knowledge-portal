using System.Security.Claims;

namespace KnowledgePortal.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    public static string GetUserId(this ClaimsPrincipal user) =>
        user.FindFirstValue("id") ?? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

    public static string GetRole(this ClaimsPrincipal user) =>
        user.FindFirstValue("role") ?? user.FindFirstValue(ClaimTypes.Role) ?? "viewer";

    public static string GetSource(this ClaimsPrincipal user) =>
        user.FindFirstValue("source") ?? "session";

    public static string? GetApiKeyId(this ClaimsPrincipal user) =>
        user.FindFirstValue("apiKeyId");

    public static string? GetApiKeyName(this ClaimsPrincipal user) =>
        user.FindFirstValue("apiKeyName");
}
