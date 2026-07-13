using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/admin/keys")]
[Authorize]
[RequirePermission(Permissions.ApiKeysManageAny)]
[RequireSessionAuth]
public class AdminApiKeysController(AppDbContext db, ApiKeyService apiKeyService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? q,
        [FromQuery] string? userId,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50)
    {
        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);

        var query = db.ApiKeys.AsQueryable();
        if (!string.IsNullOrWhiteSpace(userId))
            query = query.Where(k => k.UserId == userId);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var escaped = SlugHelper.EscapeLikePattern(q);
            query = query.Where(k =>
                EF.Functions.Like(k.Name, $"%{escaped}%", "\\") ||
                EF.Functions.Like(k.User.Name, $"%{escaped}%", "\\") ||
                EF.Functions.Like(k.User.Email, $"%{escaped}%", "\\"));
        }

        var total = await query.CountAsync();
        var keys = await query
            .OrderByDescending(k => k.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(k => new
            {
                k.Id, k.Name, k.KeyPrefix,
                k.UserId,
                UserName = k.User.Name,
                UserEmail = k.User.Email,
                LastUsedAt = k.LastUsedAt.HasValue ? k.LastUsedAt.Value.ToString("o") : null,
                ExpiresAt = k.ExpiresAt.HasValue ? k.ExpiresAt.Value.ToString("o") : null,
                CreatedAt = k.CreatedAt.ToString("o")
            })
            .ToListAsync();

        return Ok(new { keys, total });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AdminCreateKeyRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.UserId))
            return BadRequest(new { error = "userId is required" });

        var user = await db.Users.FindAsync(req.UserId);
        if (user == null) return NotFound(new { error = "User not found" });

        if (await apiKeyService.ValidateNameAsync(req.Name, user.Id) is { } error)
            return error.ToActionResult();

        var (key, rawKey) = await apiKeyService.CreateAsync(user.Id, req.Name, req.ExpiresInDays);

        return StatusCode(201, new
        {
            key.Id,
            Key = rawKey, // Only returned once
            key.Name,
            key.KeyPrefix,
            key.UserId,
            UserName = user.Name,
            UserEmail = user.Email,
            ExpiresAt = key.ExpiresAt?.ToString("o"),
            CreatedAt = key.CreatedAt.ToString("o")
        });
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] AdminUpdateKeyRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Id))
            return BadRequest(new { error = "Key id is required" });

        var key = await db.ApiKeys.Include(k => k.User).FirstOrDefaultAsync(k => k.Id == req.Id);
        if (key == null) return NotFound(new { error = "Key not found" });

        if (await apiKeyService.UpdateAsync(key, req.Name, req.ExpiresInDays) is { } error)
            return error.ToActionResult();

        return Ok(new
        {
            key.Id, key.Name, key.KeyPrefix,
            key.UserId,
            UserName = key.User.Name,
            UserEmail = key.User.Email,
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

        var key = await db.ApiKeys.FindAsync(id);
        if (key == null) return NotFound(new { error = "Key not found" });

        if (await apiKeyService.DeleteAsync(key) is { } error)
            return error.ToActionResult();

        return Ok(new { message = "API key deleted" });
    }
}
