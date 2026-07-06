using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, JwtService jwt, IConfiguration config) : ControllerBase
{
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { error = "Email and password are required" });

        var email = req.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
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

        var email = req.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email))
            return Conflict(new { error = "Email already registered" });

        var user = new User
        {
            Name = req.Name.Trim(),
            Slug = await DbInitializer.GenerateUniqueUserSlugAsync(db, req.Name.Trim()),
            Email = email,
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

        return Ok(new { user.Id, user.Name, user.Email, user.Role, isAzureUser = user.AzureObjectId != null });
    }

    [HttpPut("profile")]
    [Authorize]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest req)
    {
        var userId = User.GetUserId();
        var user = await db.Users.FindAsync(userId);
        if (user == null) return Unauthorized(new { error = "User not found" });

        // Update name
        if (!string.IsNullOrWhiteSpace(req.Name))
        {
            user.Name = req.Name.Trim();
            user.Slug = await DbInitializer.GenerateUniqueUserSlugAsync(db, req.Name.Trim());
        }

        // Update email
        if (!string.IsNullOrWhiteSpace(req.Email))
        {
            var normalizedEmail = req.Email.Trim().ToLowerInvariant();
            if (normalizedEmail != user.Email)
            {
                if (await db.Users.AnyAsync(u => u.Email == normalizedEmail && u.Id != userId))
                    return Conflict(new { error = "Email already in use" });
                user.Email = normalizedEmail;
            }
        }

        // Change password
        if (!string.IsNullOrWhiteSpace(req.NewPassword))
        {
            if (req.NewPassword.Length < 8 || req.NewPassword.Length > 128)
                return BadRequest(new { error = "New password must be 8-128 characters" });

            // Azure users setting password for the first time don't need currentPassword
            var isAzureUser = user.AzureObjectId != null;
            var isFirstPasswordSet = isAzureUser && string.IsNullOrWhiteSpace(req.CurrentPassword);

            if (!isFirstPasswordSet)
            {
                if (string.IsNullOrWhiteSpace(req.CurrentPassword))
                    return BadRequest(new { error = "Current password is required to change password" });

                if (!BCrypt.Net.BCrypt.Verify(req.CurrentPassword, user.PasswordHash))
                    return BadRequest(new { error = "Current password is incorrect" });
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword, 12);
        }

        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { user.Id, user.Name, user.Email, user.Role });
    }

    [HttpPost("azure-login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> AzureLogin([FromBody] AzureLoginRequest req)
    {
        if (!config.GetValue("AzureAd:Enabled", false))
            return BadRequest(new { error = "Azure AD login is not enabled" });

        if (string.IsNullOrWhiteSpace(req.AccessToken))
            return BadRequest(new { error = "Access token is required" });

        // Validate the Azure AD access token by calling Microsoft Graph
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", req.AccessToken);

        var graphResponse = await httpClient.GetAsync("https://graph.microsoft.com/v1.0/me");
        if (!graphResponse.IsSuccessStatusCode)
            return Unauthorized(new { error = "Invalid Azure AD token" });

        var graphUser = await graphResponse.Content.ReadFromJsonAsync<AzureGraphUser>();
        if (graphUser == null || string.IsNullOrWhiteSpace(graphUser.Id))
            return Unauthorized(new { error = "Could not retrieve user info from Azure AD" });

        var email = (graphUser.Mail ?? graphUser.UserPrincipalName)?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "Azure AD account has no email address" });

        // Find existing user by AzureObjectId or email
        var user = await db.Users.FirstOrDefaultAsync(u => u.AzureObjectId == graphUser.Id)
                   ?? await db.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
        {
            // Auto-create user from Azure AD profile
            user = new User
            {
                Name = graphUser.DisplayName ?? email,
                Slug = await DbInitializer.GenerateUniqueUserSlugAsync(db, graphUser.DisplayName ?? email),
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString(), 12),
                Role = "viewer",
                AzureObjectId = graphUser.Id
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }
        else
        {
            // Link Azure Object ID if not set and update profile from Azure
            var changed = false;
            if (user.AzureObjectId == null)
            {
                user.AzureObjectId = graphUser.Id;
                changed = true;
            }
            if (!string.IsNullOrWhiteSpace(graphUser.DisplayName) && user.Name != graphUser.DisplayName)
            {
                user.Name = graphUser.DisplayName;
                changed = true;
            }
            if (changed)
            {
                user.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }

        var token = jwt.GenerateToken(user);
        return Ok(new
        {
            token,
            user = new { user.Id, user.Name, user.Email, user.Role }
        });
    }

    private record AzureGraphUser(string? Id, string? DisplayName, string? Mail, string? UserPrincipalName, string? JobTitle, string? Department);
}

