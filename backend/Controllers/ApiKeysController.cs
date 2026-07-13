using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/keys")]
[Authorize]
[RequirePermission(Permissions.ApiKeysManage)]
[RequireSessionAuth]
public class ApiKeysController(AppDbContext db, ApiKeyService apiKeyService) : ControllerBase
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
        var userId = User.GetUserId();

        if (await apiKeyService.ValidateNameAsync(req.Name, userId) is { } error)
            return error.ToActionResult();

        var (key, rawKey) = await apiKeyService.CreateAsync(userId, req.Name, req.ExpiresInDays);

        return StatusCode(201, new
        {
            key.Id,
            Key = rawKey, // Only returned once
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

        if (await apiKeyService.UpdateAsync(key, req.Name, req.ExpiresInDays) is { } error)
            return error.ToActionResult();

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

        if (await apiKeyService.DeleteAsync(key) is { } error)
            return error.ToActionResult();

        return Ok(new { message = "API key deleted" });
    }

    [HttpPost("{id}/rotate")]
    public async Task<IActionResult> Rotate(string id)
    {
        var userId = User.GetUserId();
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.UserId == userId);
        if (key == null) return NotFound(new { error = "Key not found" });

        var rawKey = await apiKeyService.RotateAsync(key);

        return Ok(new
        {
            key.Id,
            Key = rawKey,
            key.Name,
            ExpiresAt = key.ExpiresAt?.ToString("o")
        });
    }
}

