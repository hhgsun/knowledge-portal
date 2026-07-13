using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Services;
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
public class AuthController(AppDbContext db, JwtService jwt, IConfiguration config, UserService userService) : ControllerBase
{
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { error = "Email and password are required" });

        var email = UserService.NormalizeEmail(req.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null || !UserService.VerifyPassword(req.Password, user.PasswordHash))
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

        var (user, error) = await userService.CreateAsync(req.Name, req.Email, req.Password, "viewer");
        if (error != null) return error.ToActionResult();

        return StatusCode(201, new { user!.Id, user.Name, user.Email });
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

        var isAzureUser = user.AzureObjectId != null;

        // Name and email of Azure AD users are re-synced from Microsoft Graph on every login
        if (isAzureUser)
        {
            if (!string.IsNullOrWhiteSpace(req.Name) && req.Name.Trim() != user.Name)
                return BadRequest(new { error = "Name is managed by your Microsoft account and cannot be changed" });
            if (!string.IsNullOrWhiteSpace(req.Email) && UserService.NormalizeEmail(req.Email) != user.Email)
                return BadRequest(new { error = "Email is managed by your Microsoft account and cannot be changed" });
        }
        else
        {
            // Update name
            if (!string.IsNullOrWhiteSpace(req.Name))
            {
                user.Name = req.Name.Trim();
                user.Slug = await db.GenerateUniqueUserSlugAsync(req.Name.Trim());
            }

            // Update email
            if (!string.IsNullOrWhiteSpace(req.Email))
            {
                var normalizedEmail = UserService.NormalizeEmail(req.Email);
                if (normalizedEmail != user.Email)
                {
                    if (await userService.IsEmailTakenAsync(normalizedEmail, userId))
                        return Conflict(new { error = "Email already in use" });
                    user.Email = normalizedEmail;
                }
            }
        }

        // Change password
        if (!string.IsNullOrWhiteSpace(req.NewPassword))
        {
            if (UserService.ValidatePassword(req.NewPassword) != null)
                return BadRequest(new { error = "New password must be 8-128 characters" });

            // Azure users setting password for the first time don't need currentPassword
            var isFirstPasswordSet = isAzureUser && string.IsNullOrWhiteSpace(req.CurrentPassword);

            if (!isFirstPasswordSet)
            {
                if (string.IsNullOrWhiteSpace(req.CurrentPassword))
                    return BadRequest(new { error = "Current password is required to change password" });

                if (!UserService.VerifyPassword(req.CurrentPassword, user.PasswordHash))
                    return BadRequest(new { error = "Current password is incorrect" });
            }

            user.PasswordHash = UserService.HashPassword(req.NewPassword);
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

        var rawEmail = graphUser.Mail ?? graphUser.UserPrincipalName;
        if (string.IsNullOrWhiteSpace(rawEmail))
            return BadRequest(new { error = "Azure AD account has no email address" });
        var email = UserService.NormalizeEmail(rawEmail);

        // Find existing user by AzureObjectId or email
        var user = await db.Users.FirstOrDefaultAsync(u => u.AzureObjectId == graphUser.Id)
                   ?? await db.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
        {
            // Auto-create user from Azure AD profile (random password — login happens via Azure)
            var (created, createError) = await userService.CreateAsync(
                graphUser.DisplayName ?? email, email, Guid.NewGuid().ToString(), "viewer", graphUser.Id);
            if (createError != null) return createError.ToActionResult();
            user = created!;
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

