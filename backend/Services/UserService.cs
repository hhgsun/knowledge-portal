using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

/// <summary>
/// Shared user creation and credential logic for self-registration, Azure AD auto-provisioning,
/// and admin user management.
/// </summary>
public class UserService(AppDbContext db)
{
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static string HashPassword(string password) => BCrypt.Net.BCrypt.HashPassword(password, 12);

    public static bool VerifyPassword(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);

    public static ServiceError? ValidatePassword(string password)
        => password.Length is < 8 or > 128
            ? new ServiceError(400, "Password must be 8-128 characters")
            : null;

    public async Task<bool> IsEmailTakenAsync(string normalizedEmail, string? excludeUserId = null)
    {
        var exclude = excludeUserId ?? "";
        return await db.Users.AnyAsync(u => u.Email == normalizedEmail && u.Id != exclude);
    }

    /// <summary>Validates, creates, and persists a user with a unique slug.</summary>
    public async Task<(User? User, ServiceError? Error)> CreateAsync(string name, string email, string password, string role, string? azureObjectId = null)
    {
        if (ValidatePassword(password) is { } passwordError)
            return (null, passwordError);

        var normalizedEmail = NormalizeEmail(email);
        if (await IsEmailTakenAsync(normalizedEmail))
            return (null, new ServiceError(409, "Email already registered"));

        var user = new User
        {
            Name = name.Trim(),
            Slug = await db.GenerateUniqueUserSlugAsync(name.Trim()),
            Email = normalizedEmail,
            PasswordHash = HashPassword(password),
            Role = role,
            AzureObjectId = azureObjectId
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user, null);
    }
}
