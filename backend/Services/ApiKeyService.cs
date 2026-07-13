using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

/// <summary>
/// Shared API key lifecycle logic for the self-service (/api/keys) and admin (/api/admin/keys) endpoints.
/// Controllers keep scoping (own vs any user) and response shapes.
/// </summary>
public class ApiKeyService(AppDbContext db)
{
    public const int DefaultExpiryDays = 90;

    /// <summary>Validates name length and per-user uniqueness.</summary>
    public async Task<ServiceError?> ValidateNameAsync(string? name, string userId, string? excludeKeyId = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            return new ServiceError(400, "Name is required (1-100 chars)");

        var trimmed = name.Trim();
        var exclude = excludeKeyId ?? "";
        var nameTaken = await db.ApiKeys.AnyAsync(k =>
            k.UserId == userId && k.Id != exclude && k.Name.ToLower() == trimmed.ToLower());
        if (nameTaken)
            return new ServiceError(409, "An API key with this name already exists");

        return null;
    }

    /// <summary>Creates and persists a key. The raw key is only available here — store nothing but the hash.</summary>
    public async Task<(ApiKey Key, string RawKey)> CreateAsync(string userId, string name, int? expiresInDays)
    {
        var generated = ApiKeyGenerator.Generate();
        var key = new ApiKey
        {
            UserId = userId,
            KeyHash = generated.Hash,
            KeyPrefix = generated.Prefix,
            Name = name.Trim(),
            ExpiresAt = DateTime.UtcNow.AddDays(Math.Clamp(expiresInDays ?? DefaultExpiryDays, 1, 365))
        };

        db.ApiKeys.Add(key);
        await db.SaveChangesAsync();
        return (key, generated.RawKey);
    }

    /// <summary>Applies name and/or expiry changes after validation.</summary>
    public async Task<ServiceError?> UpdateAsync(ApiKey key, string? name, int? expiresInDays)
    {
        if (name != null)
        {
            var error = await ValidateNameAsync(name, key.UserId, key.Id);
            if (error != null) return error;
            key.Name = name.Trim();
        }

        if (expiresInDays.HasValue)
            key.ExpiresAt = DateTime.UtcNow.AddDays(Math.Clamp(expiresInDays.Value, 1, 365));

        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>Deletes the key unless articles were created with it.</summary>
    public async Task<ServiceError?> DeleteAsync(ApiKey key)
    {
        var articleCount = await db.Articles.CountAsync(a => a.CreatedViaApiKeyId == key.Id);
        if (articleCount > 0)
            return new ServiceError(409, $"This API key cannot be deleted because {articleCount} article(s) were created with it");

        db.ApiKeys.Remove(key);
        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>Replaces hash and prefix, resets expiration; returns the new raw key.</summary>
    public async Task<string> RotateAsync(ApiKey key)
    {
        var generated = ApiKeyGenerator.Generate();
        key.KeyHash = generated.Hash;
        key.KeyPrefix = generated.Prefix;
        key.ExpiresAt = DateTime.UtcNow.AddDays(DefaultExpiryDays);
        await db.SaveChangesAsync();
        return generated.RawKey;
    }
}
