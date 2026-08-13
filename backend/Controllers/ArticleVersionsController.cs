using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/articles/{articleId}/versions")]
[Authorize]
public class ArticleVersionsController(AppDbContext db, ArticleService articleService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(string articleId)
    {
        if (await GetViewableArticleAsync(articleId) == null)
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
        if (await GetViewableArticleAsync(articleId) == null)
            return NotFound(new { error = "Article not found" });

        var version = await db.ArticleVersions
            .Where(v => v.ArticleId == articleId && v.Id == versionId)
            .FirstOrDefaultAsync();

        if (version == null) return NotFound(new { error = "Version not found" });

        return Ok(new
        {
            version.Id, version.Version, version.Title, version.ChangeSummary,
            version.ChangedBy,
            ContentMarkdown = version.Content,
            CreatedAt = version.CreatedAt.ToString("o")
        });
    }

    [HttpPost("{versionId}/restore")]
    public async Task<IActionResult> Restore(string articleId, string versionId)
    {
        var article = await db.Articles.FindAsync(articleId);
        if (article == null) return NotFound(new { error = "Article not found" });

        var userId = User.GetUserId();

        // Check edit permission (same logic as article update)
        if (!RbacService.CanEditArticle(User, article.OwnerId == userId))
            return StatusCode(403, new { error = "You do not have permission to edit this article" });

        var version = await db.ArticleVersions
            .Where(v => v.ArticleId == articleId && v.Id == versionId)
            .FirstOrDefaultAsync();

        if (version == null) return NotFound(new { error = "Version not found" });

        // Apply version content to article
        article.Title = version.Title;
        article.Content = version.Content;
        article.UpdatedAt = DateTime.UtcNow;
        article.ReadTimeMinutes = ContentExtractor.CalculateReadTime(version.Content);
        article.ApprovedById = null;
        article.ApprovedAt = null;

        // Create a new version recording the restore
        var newVersion = await articleService.AddVersionAsync(
            articleId, version.Title, version.Content, userId, $"Restored to version {version.Version}");

        await db.SaveChangesAsync();

        // Restored content on published article → re-embed + FTS sync (same as article update)
        await articleService.QueueReindexAsync(article);

        return Ok(new { message = "Article restored to selected version", version = newVersion });
    }

    /// <summary>Loads the article if it exists and the caller may view it (viewers: published or own only).</summary>
    private async Task<Models.Entities.Article?> GetViewableArticleAsync(string articleId)
    {
        var article = await db.Articles.FindAsync(articleId);
        if (article == null) return null;
        return RbacService.CanViewArticle(User, article.Status, article.OwnerId == User.GetUserId())
            ? article
            : null;
    }
}
