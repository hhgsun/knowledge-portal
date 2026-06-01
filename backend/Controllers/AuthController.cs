using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, JwtService jwt) : ControllerBase
{
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { error = "Email and password are required" });

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == req.Email);
        if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { error = "Invalid email or password" });

        var token = jwt.GenerateToken(user);
        return Ok(new
        {
            token,
            user = new { user.Id, user.Name, user.Email, user.Role }
        });
    }

    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { error = "Name, email, and password are required" });

        if (req.Password.Length < 8 || req.Password.Length > 128)
            return BadRequest(new { error = "Password must be 8-128 characters" });

        if (await db.Users.AnyAsync(u => u.Email == req.Email))
            return Conflict(new { error = "Email already registered" });

        var user = new User
        {
            Name = req.Name.Trim(),
            Email = req.Email.Trim().ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password, 12),
            Role = "viewer"
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return StatusCode(201, new { user.Id, user.Name, user.Email });
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        if (User.Identity?.IsAuthenticated != true)
            return Unauthorized(new { error = "Unauthorized" });

        var userId = User.GetUserId();
        var user = await db.Users.FindAsync(userId);
        if (user == null) return Unauthorized(new { error = "User not found" });

        return Ok(new { user.Id, user.Name, user.Email, user.Role, user.Avatar });
    }
}

public record LoginRequest(string Email, string Password);
public record RegisterRequest(string Name, string Email, string Password);
