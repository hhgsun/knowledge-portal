using System.Security.Cryptography;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/keys")]
[Authorize]
public class ApiKeysController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [RequirePermission("api_keys:manage")]
    public async Task<IActionResult> List()
    {
        var userId = User.GetUserId();
        if (User.GetSource() == "api-key")
            return StatusCode(403, new { error = "API keys cannot be managed via API key auth" });

        var keys = await db.ApiKeys
            .Where(k => k.UserId == userId)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new
            {
                k.Id, k.Name, k.Permissions,
                LastUsedAt = k.LastUsedAt.HasValue ? k.LastUsedAt.Value.ToString("o") : null,
                ExpiresAt = k.ExpiresAt.HasValue ? k.ExpiresAt.Value.ToString("o") : null,
                CreatedAt = k.CreatedAt.ToString("o")
            })
            .ToListAsync();

        return Ok(keys);
    }

    [HttpPost]
    [RequirePermission("api_keys:manage")]
    public async Task<IActionResult> Create([FromBody] CreateKeyRequest req)
    {
        if (User.GetSource() == "api-key")
            return StatusCode(403, new { error = "API keys cannot be managed via API key auth" });

        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 100)
            return BadRequest(new { error = "Name is required (1-100 chars)" });

        var expiresInDays = Math.Clamp(req.ExpiresInDays ?? 90, 1, 365);

        // Generate raw key: kp_ + 32 random chars
        var rawKey = "kp_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        var key = new ApiKey
        {
            UserId = User.GetUserId(),
            KeyHash = BCrypt.Net.BCrypt.HashPassword(rawKey, 12),
            Name = req.Name.Trim(),
            Permissions = req.Permissions != null
                ? System.Text.Json.JsonSerializer.Serialize(req.Permissions)
                : "[\"articles:read\",\"search\"]",
            ExpiresAt = DateTime.UtcNow.AddDays(expiresInDays)
        };

        db.ApiKeys.Add(key);
        await db.SaveChangesAsync();

        return StatusCode(201, new
        {
            key.Id,
            Key = rawKey, // Only returned once
            key.Name,
            key.Permissions,
            ExpiresAt = key.ExpiresAt?.ToString("o")
        });
    }

    [HttpDelete]
    [RequirePermission("api_keys:manage")]
    public async Task<IActionResult> Delete([FromQuery] string id)
    {
        if (User.GetSource() == "api-key")
            return StatusCode(403, new { error = "API keys cannot be managed via API key auth" });

        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { error = "Key id is required" });

        var userId = User.GetUserId();
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.UserId == userId);
        if (key == null) return NotFound(new { error = "Key not found" });

        db.ApiKeys.Remove(key);
        await db.SaveChangesAsync();

        return Ok(new { success = true });
    }
}

public record CreateKeyRequest(string Name, string[]? Permissions = null, int? ExpiresInDays = null);
