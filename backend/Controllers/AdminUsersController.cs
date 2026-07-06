using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize]
[RequirePermission(Permissions.UsersManage)]
public class AdminUsersController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 50)
    {
        if (User.GetSource() == "api-key")
            return StatusCode(403, new { error = "User management requires session auth" });

        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);

        var query = db.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var escaped = q.Replace("%", "\\%").Replace("_", "\\_");
            query = query.Where(u => EF.Functions.Like(u.Name, $"%{escaped}%", "\\") || EF.Functions.Like(u.Email, $"%{escaped}%", "\\"));
        }

        var total = await query.CountAsync();
        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(u => new
            {
                u.Id, u.Name, u.Email, u.Role,
                CreatedAt = u.CreatedAt.ToString("o"),
                UpdatedAt = u.UpdatedAt.ToString("o")
            })
            .ToListAsync();

        return Ok(new
        {
            users,
            total
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req)
    {
        if (User.GetSource() == "api-key")
            return StatusCode(403, new { error = "User management requires session auth" });

        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { error = "Name, email, and password are required" });

        if (req.Password.Length < 8 || req.Password.Length > 128)
            return BadRequest(new { error = "Password must be 8-128 characters" });

        if (await db.Users.AnyAsync(u => u.Email == req.Email.Trim().ToLowerInvariant()))
            return Conflict(new { error = "Email already exists" });

        var user = new User
        {
            Name = req.Name.Trim(),
            Email = req.Email.Trim().ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password, 12),
            Role = req.Role ?? "viewer"
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return StatusCode(201, new { user.Id, user.Name, user.Email, user.Role, CreatedAt = user.CreatedAt.ToString("o") });
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateUserRequest req)
    {
        if (User.GetSource() == "api-key")
            return StatusCode(403, new { error = "User management requires session auth" });

        if (string.IsNullOrWhiteSpace(req.UserId))
            return BadRequest(new { error = "userId is required" });

        var user = await db.Users.FindAsync(req.UserId);
        if (user == null) return NotFound(new { error = "User not found" });

        var currentUserId = User.GetUserId();

        // Prevent self-demotion from admin
        if (user.Id == currentUserId && req.Role != null && req.Role != "admin" && user.Role == "admin")
            return BadRequest(new { error = "Cannot demote yourself from admin" });

        if (req.Name != null) user.Name = req.Name.Trim();
        if (req.Email != null)
        {
            var email = req.Email.Trim().ToLowerInvariant();
            if (email != user.Email && await db.Users.AnyAsync(u => u.Email == email))
                return Conflict(new { error = "Email already exists" });
            user.Email = email;
        }
        if (req.Role != null) user.Role = req.Role;
        if (!string.IsNullOrWhiteSpace(req.Password))
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password, 12);

        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { user.Id, user.Name, user.Email, user.Role, UpdatedAt = user.UpdatedAt.ToString("o") });
    }

    [HttpDelete]
    public async Task<IActionResult> Delete([FromQuery] string id)
    {
        if (User.GetSource() == "api-key")
            return StatusCode(403, new { error = "User management requires session auth" });

        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { error = "User id is required" });

        var currentUserId = User.GetUserId();
        if (id == currentUserId)
            return BadRequest(new { error = "Cannot delete yourself" });

        var user = await db.Users.FindAsync(id);
        if (user == null) return NotFound(new { error = "User not found" });

        db.Users.Remove(user);
        await db.SaveChangesAsync();

        return Ok(new { message = "User deleted" });
    }
}

