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
}
