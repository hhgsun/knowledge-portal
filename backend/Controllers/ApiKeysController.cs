using System.Security.Cryptography;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models;
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
    [RequirePermission(Permissions.ApiKeysManage)]
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
                k.Id, k.Name,
                LastUsedAt = k.LastUsedAt.HasValue ? k.LastUsedAt.Value.ToString("o") : null,
                ExpiresAt = k.ExpiresAt.HasValue ? k.ExpiresAt.Value.ToString("o") : null,
                CreatedAt = k.CreatedAt.ToString("o")
            })
            .ToListAsync();

        return Ok(keys);
    }

    [HttpPost]
    [RequirePermission(Permissions.ApiKeysManage)]
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
            KeyPrefix = rawKey[3..11], // First 8 chars after "kp_" for indexed lookup
            Name = req.Name.Trim(),
            ExpiresAt = DateTime.UtcNow.AddDays(expiresInDays)
        };

        db.ApiKeys.Add(key);
        await db.SaveChangesAsync();

        return StatusCode(201, new
        {
            key.Id,
            Key = rawKey, // Only returned once
            key.Name,
            ExpiresAt = key.ExpiresAt?.ToString("o")
        });
    }

    [HttpDelete]
    [RequirePermission(Permissions.ApiKeysManage)]
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

        return Ok(new { message = "API key deleted" });
    }

    [HttpPost("{id}/rotate")]
    [RequirePermission(Permissions.ApiKeysManage)]
    public async Task<IActionResult> Rotate(string id)
    {
        if (User.GetSource() == "api-key")
            return StatusCode(403, new { error = "API keys cannot be managed via API key auth" });

        var userId = User.GetUserId();
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.UserId == userId);
        if (key == null) return NotFound(new { error = "Key not found" });

        // Generate new raw key
        var rawKey = "kp_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        // Update hash and prefix, reset expiration
        key.KeyHash = BCrypt.Net.BCrypt.HashPassword(rawKey, 12);
        key.KeyPrefix = rawKey[3..11];
        key.ExpiresAt = DateTime.UtcNow.AddDays(90);
        await db.SaveChangesAsync();

        return Ok(new
        {
            key.Id,
            Key = rawKey,
            key.Name,
            ExpiresAt = key.ExpiresAt?.ToString("o")
        });
    }
}

