using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/articles/{articleId}/versions")]
[Authorize]
public class ArticleVersionsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(string articleId)
    {
        // Verify article exists and user has access
        var article = await db.Articles.FindAsync(articleId);
        if (article == null) return NotFound(new { error = "Article not found" });

        var role = User.GetRole();
        var userId = User.GetUserId();
        if (role == "viewer" && article.Status != "published" && article.OwnerId != userId)
            return NotFound(new { error = "Article not found" });

        var versions = await db.ArticleVersions
            .Where(v => v.ArticleId == articleId)
            .OrderByDescending(v => v.Version)
            .Select(v => new
            {
                v.Id, v.Version, v.Title, v.ChangeSummary,
                v.ChangedBy,
                ChangedByName = db.Users.Where(u => u.Id == v.ChangedBy).Select(u => u.Name).FirstOrDefault(),
                CreatedAt = v.CreatedAt.ToString("o")
            })
            .ToListAsync();

        return Ok(versions);
    }

    [HttpGet("{versionId}")]
    public async Task<IActionResult> Get(string articleId, string versionId)
    {
        // Verify article exists and user has access
        var article = await db.Articles.FindAsync(articleId);
        if (article == null) return NotFound(new { error = "Article not found" });

        var role = User.GetRole();
        var userId = User.GetUserId();
        if (role == "viewer" && article.Status != "published" && article.OwnerId != userId)
            return NotFound(new { error = "Article not found" });

        var version = await db.ArticleVersions
            .Where(v => v.ArticleId == articleId && v.Id == versionId)
            .FirstOrDefaultAsync();

        if (version == null) return NotFound(new { error = "Version not found" });

        return Ok(new
        {
            version.Id, version.Version, version.Title, version.ChangeSummary,
            version.ChangedBy,
            Content = version.Content != null ? JsonSerializer.Deserialize<object>(version.Content) : null,
            CreatedAt = version.CreatedAt.ToString("o")
        });
    }

    [HttpPost("{versionId}/restore")]
    public async Task<IActionResult> Restore(string articleId, string versionId)
    {
        var article = await db.Articles.FindAsync(articleId);
        if (article == null) return NotFound(new { error = "Article not found" });

        var userId = User.GetUserId();
        var role = User.GetRole();

        // Check edit permission (same logic as article update)
        var isOwner = article.OwnerId == userId;
        if (!isOwner && !RbacService.HasPermission(role, Permissions.ArticlesEditAny))
            return StatusCode(403, new { error = "You do not have permission to edit this article" });
        if (isOwner && !RbacService.HasPermission(role, Permissions.ArticlesEditOwn))
            return StatusCode(403, new { error = "You do not have permission to edit this article" });

        var version = await db.ArticleVersions
            .Where(v => v.ArticleId == articleId && v.Id == versionId)
            .FirstOrDefaultAsync();

        if (version == null) return NotFound(new { error = "Version not found" });

        // Apply version content to article
        article.Title = version.Title;
        article.Content = version.Content;
        article.UpdatedAt = DateTime.UtcNow;
        article.ReadTimeMinutes = CalculateReadTime(version.Content);

        // Create a new version recording the restore
        var maxVersion = await db.ArticleVersions
            .Where(v => v.ArticleId == articleId)
            .MaxAsync(v => (int?)v.Version) ?? 0;

        db.ArticleVersions.Add(new Models.Entities.ArticleVersion
        {
            ArticleId = articleId,
            Title = version.Title,
            Content = version.Content,
            ChangedBy = userId,
            ChangeSummary = $"Restored to version {version.Version}",
            Version = maxVersion + 1
        });

        await db.SaveChangesAsync();

        return Ok(new { message = "Article restored to selected version", version = maxVersion + 1 });
    }

    private static int? CalculateReadTime(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson)) return null;
        try
        {
            var text = ExtractTextFromJson(JsonDocument.Parse(contentJson).RootElement);
            var wordCount = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            return Math.Max(1, (int)Math.Ceiling(wordCount / 200.0));
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractTextFromJson(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString() ?? "";
            case JsonValueKind.Object:
                var sb = new System.Text.StringBuilder();
                if (element.TryGetProperty("text", out var textProp))
                    sb.Append(textProp.GetString() ?? "").Append(' ');
                if (element.TryGetProperty("content", out var contentProp))
                    sb.Append(ExtractTextFromJson(contentProp));
                return sb.ToString();
            case JsonValueKind.Array:
                return string.Join(' ', element.EnumerateArray().Select(ExtractTextFromJson));
            default:
                return "";
        }
    }
}
