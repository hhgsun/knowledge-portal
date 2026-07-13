using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/admin/keys")]
[Authorize]
[RequirePermission(Permissions.ApiKeysManageAny)]
[RequireSessionAuth]
public class AdminApiKeysController(AppDbContext db) : ControllerBase
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

        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 100)
            return BadRequest(new { error = "Name is required (1-100 chars)" });

        var user = await db.Users.FindAsync(req.UserId);
        if (user == null) return NotFound(new { error = "User not found" });

        var name = req.Name.Trim();
        var nameTaken = await db.ApiKeys.AnyAsync(k => k.UserId == user.Id && k.Name.ToLower() == name.ToLower());
        if (nameTaken)
            return Conflict(new { error = "This user already has an API key with this name" });

        var expiresInDays = Math.Clamp(req.ExpiresInDays ?? 90, 1, 365);

        var generated = ApiKeyGenerator.Generate();
        var key = new ApiKey
        {
            UserId = user.Id,
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

        if (req.Name != null)
        {
            if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 100)
                return BadRequest(new { error = "Name must be 1-100 chars" });

            var name = req.Name.Trim();
            var nameTaken = await db.ApiKeys.AnyAsync(k => k.UserId == key.UserId && k.Id != key.Id && k.Name.ToLower() == name.ToLower());
            if (nameTaken)
                return Conflict(new { error = "This user already has an API key with this name" });

            key.Name = name;
        }

        if (req.ExpiresInDays.HasValue)
            key.ExpiresAt = DateTime.UtcNow.AddDays(Math.Clamp(req.ExpiresInDays.Value, 1, 365));

        await db.SaveChangesAsync();

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

        var articleCount = await db.Articles.CountAsync(a => a.CreatedViaApiKeyId == key.Id);
        if (articleCount > 0)
            return Conflict(new { error = $"This API key cannot be deleted because {articleCount} article(s) were created with it" });

        db.ApiKeys.Remove(key);
        await db.SaveChangesAsync();

        return Ok(new { message = "API key deleted" });
    }
}
