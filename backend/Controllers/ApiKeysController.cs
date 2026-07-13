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
[RequirePermission(Permissions.ApiKeysManage)]
[RequireSessionAuth]
public class ApiKeysController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var userId = User.GetUserId();

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
    public async Task<IActionResult> Create([FromBody] CreateKeyRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 100)
            return BadRequest(new { error = "Name is required (1-100 chars)" });

        var userId = User.GetUserId();
        var name = req.Name.Trim();
        var nameTaken = await db.ApiKeys.AnyAsync(k => k.UserId == userId && k.Name.ToLower() == name.ToLower());
        if (nameTaken)
            return Conflict(new { error = "An API key with this name already exists" });

        var expiresInDays = Math.Clamp(req.ExpiresInDays ?? 90, 1, 365);

        var generated = ApiKeyGenerator.Generate();
        var key = new ApiKey
        {
            UserId = userId,
            KeyHash = generated.Hash,
            KeyPrefix = generated.Prefix,
            Name = name,
            ExpiresAt = DateTime.UtcNow.AddDays(expiresInDays)
        };

        db.ApiKeys.Add(key);
        await db.SaveChangesAsync();

        return StatusCode(201, new
        {
            key.Id,
            Key = generated.RawKey, // Only returned once
            key.Name,
            ExpiresAt = key.ExpiresAt?.ToString("o")
        });
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateKeyRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Id))
            return BadRequest(new { error = "Key id is required" });

        var userId = User.GetUserId();
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == req.Id && k.UserId == userId);
        if (key == null) return NotFound(new { error = "Key not found" });

        if (req.Name != null)
        {
            if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 100)
                return BadRequest(new { error = "Name must be 1-100 chars" });

            var name = req.Name.Trim();
            var nameTaken = await db.ApiKeys.AnyAsync(k => k.UserId == userId && k.Id != key.Id && k.Name.ToLower() == name.ToLower());
            if (nameTaken)
                return Conflict(new { error = "An API key with this name already exists" });

            key.Name = name;
        }

        if (req.ExpiresInDays.HasValue)
            key.ExpiresAt = DateTime.UtcNow.AddDays(Math.Clamp(req.ExpiresInDays.Value, 1, 365));

        await db.SaveChangesAsync();

        return Ok(new
        {
            key.Id,
            key.Name,
            LastUsedAt = key.LastUsedAt?.ToString("o"),
            ExpiresAt = key.ExpiresAt?.ToString("o"),
            CreatedAt = key.CreatedAt.ToString("o")
        });
    }

    [HttpDelete]
    public async Task<IActionResult> Delete([FromQuery] string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { error = "Key id is required" });

        var userId = User.GetUserId();
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.UserId == userId);
        if (key == null) return NotFound(new { error = "Key not found" });

        var articleCount = await db.Articles.CountAsync(a => a.CreatedViaApiKeyId == key.Id);
        if (articleCount > 0)
            return Conflict(new { error = $"This API key cannot be deleted because {articleCount} article(s) were created with it" });

        db.ApiKeys.Remove(key);
        await db.SaveChangesAsync();

        return Ok(new { message = "API key deleted" });
    }

    [HttpPost("{id}/rotate")]
    public async Task<IActionResult> Rotate(string id)
    {
        var userId = User.GetUserId();
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.UserId == userId);
        if (key == null) return NotFound(new { error = "Key not found" });

        // Replace hash and prefix, reset expiration
        var generated = ApiKeyGenerator.Generate();
        key.KeyHash = generated.Hash;
        key.KeyPrefix = generated.Prefix;
        key.ExpiresAt = DateTime.UtcNow.AddDays(90);
        await db.SaveChangesAsync();

        return Ok(new
        {
            key.Id,
            Key = generated.RawKey,
            key.Name,
            ExpiresAt = key.ExpiresAt?.ToString("o")
        });
    }
}

