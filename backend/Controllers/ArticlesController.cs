using System.Text.Json;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/articles")]
[Authorize]
public class ArticlesController(AppDbContext db, IConfiguration config, ArticleService articleService,
    ArticleMutationService mutations, ILogger<ArticlesController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? contentType = null,
        [FromQuery] bool mine = false,
        [FromQuery] string? q = null,
        [FromQuery] string[]? tag = null,
        [FromQuery] string? dateFrom = null,
        [FromQuery] string? dateTo = null,
        [FromQuery] bool onlyOwnContent = false,
        [FromQuery] bool includeContent = false,
        [FromQuery] bool includeAttachments = false)
    {
        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);

        var role = User.GetRole();
        var userId = User.GetUserId();
        var query = db.Articles.AsQueryable();

        // Viewers see published + their own articles
        if (role == "viewer")
            query = query.Where(a => a.Status == "published" || a.OwnerId == userId);

        if (mine)
            query = query.Where(a => a.OwnerId == userId);

        // API key scoping: when onlyOwnContent=true and request via API key, filter to that key's articles
        var callerApiKeyId = User.FindFirst("apiKeyId")?.Value;
        if (onlyOwnContent && callerApiKeyId != null)
            query = query.Where(a => a.CreatedViaApiKeyId == callerApiKeyId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var statuses = status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (statuses.Length > 0)
                query = query.Where(a => statuses.Contains(a.Status));
        }

        if (!string.IsNullOrWhiteSpace(contentType))
        {
            var ctValues = contentType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (ctValues.Length > 0) query = query.WhereContentTypeIn(ctValues);
        }

        if (tag is { Length: > 0 })
        {
            var tagSlugs = tag.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (tagSlugs.Count > 0)
                query = query.WhereHasAllTags(tagSlugs);
        }

        if (DateTime.TryParse(dateFrom, out var from))
            query = query.Where(a => a.UpdatedAt >= from.ToUniversalTime());

        if (DateTime.TryParse(dateTo, out var to))
            query = query.Where(a => a.UpdatedAt < to.ToUniversalTime().AddDays(1));

        if (!string.IsNullOrWhiteSpace(q))
        {
            var escaped = SlugHelper.EscapeLikePattern(q);
            query = query.Where(a => EF.Functions.Like(a.Title, $"%{escaped}%", "\\"));
        }

        var (articles, total) = await articleService.ListAsync(query, page, limit,
            includeContent: includeContent,
            includeAttachments: includeAttachments,
            includeIndexingStatus: role is "admin" or "editor");

        return Ok(new { articles, total });
    }

    [HttpPost]
    [RequirePermission(Permissions.ArticlesCreate)]
    public async Task<IActionResult> Create([FromBody] CreateArticleRequest req)
    {
        var result = await mutations.CreateAsync(
            new CreateArticleCommand(req.Title, req.ContentMarkdown, req.Excerpt, req.Status,
                req.ContentType, req.Tags, req.ReviewIntervalDays),
            User, "Initial version", ct: HttpContext.RequestAborted);
        if (result.Error != null) return result.Error.ToActionResult();
        var article = result.Article!;

        return StatusCode(201, new { article.Id, article.Slug, article.Title });
    }

    [HttpGet("{idOrSlug}")]
    public async Task<IActionResult> Get(string idOrSlug)
    {
        var article = await articleService.GetByIdOrSlugAsync(idOrSlug);
        if (article == null)
            return NotFound(new { error = "Article not found" });

        // Viewers can only see published articles or their own
        var userId = User.GetUserId();
        if (!RbacService.CanViewArticle(User, article.Status, article.OwnerId == userId))
            return NotFound(new { error = "Article not found" });

        // Record view (deduplicated per user/article within 15 minutes)
        var fifteenMinutesAgo = DateTime.UtcNow.AddMinutes(-15);
        var recentView = await db.ArticleViews
            .AnyAsync(v => v.ArticleId == article.Id && v.UserId == userId && v.CreatedAt > fifteenMinutesAgo);

        if (!recentView)
        {
            db.ArticleViews.Add(new ArticleView
            {
                ArticleId = article.Id,
                UserId = userId
            });
            await db.SaveChangesAsync();
        }

        var includeIndexingStatus = User.GetRole() is "admin" or "editor";
        return Ok(await articleService.BuildDetailAsync(article, includeIndexingStatus));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateArticleRequest req)
    {
        var article = await db.Articles.FindAsync(id);
        if (article == null) return NotFound(new { error = "Article not found" });

        var error = await mutations.UpdateAsync(article, req, User, HttpContext.RequestAborted);
        if (error != null) return error.ToActionResult();

        return Ok(new { article.Id, article.Slug, article.Title });
    }

    [HttpDelete("{id}")]
    [RequirePermission(Permissions.ArticlesDeleteAny)]
    [RequireSessionAuth] // destructive deletes are session-only — API keys cannot delete
    public async Task<IActionResult> Delete(string id)
    {
        var article = await db.Articles.FindAsync(id);
        if (article == null) return NotFound(new { error = "Article not found" });

        // Remove from FTS index
        await articleService.RemoveFromIndexAsync(id);

        db.Articles.Remove(article);
        await db.SaveChangesAsync();
        try { AttachmentHelper.MoveArticleToTrash(config, id); }
        catch (Exception ex) { logger.LogError(ex, "Failed to move deleted article files {ArticleId} to trash", id); }

        return Ok(new { message = "Article deleted" });
    }

    [HttpPost("{id}/approve")]
    [RequirePermission(Permissions.ArticlesApprove)]
    public async Task<IActionResult> Approve(string id)
    {
        var article = await db.Articles.FindAsync(id);
        if (article == null) return NotFound(new { error = "Article not found" });

        if (article.Status != "published")
            return BadRequest(new { error = "Only published articles can be approved" });

        article.LastReviewedAt = DateTime.UtcNow;
        article.ApprovedById = User.GetUserId();
        article.ApprovedAt = DateTime.UtcNow;
        article.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { message = "Article approved", article.Id, article.Slug, approvedAt = article.ApprovedAt });
    }

    [HttpDelete("{id}/approve")]
    [HttpPost("{id}/reject")] // Backwards-compatible alias
    [RequirePermission(Permissions.ArticlesApprove)]
    public async Task<IActionResult> RemoveApproval(string id)
    {
        var article = await db.Articles.FindAsync(id);
        if (article == null) return NotFound(new { error = "Article not found" });

        if (article.ApprovedAt == null || article.ApprovedById == null)
            return BadRequest(new { error = "Article is not approved" });

        article.ApprovedById = null;
        article.ApprovedAt = null;
        article.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { message = "Article approval removed", article.Id, article.Slug });
    }

    [HttpGet("{id}/related")]
    public async Task<IActionResult> Related(string id, [FromQuery] int limit = 5)
    {
        limit = Math.Clamp(limit, 1, 20);

        if (await articleService.GetViewableByIdAsync(id, User) == null)
            return NotFound(new { error = "Article not found" });

        var articleTagIds = await db.ArticleTags
            .Where(at => at.ArticleId == id)
            .Select(at => at.TagId)
            .ToListAsync();

        if (articleTagIds.Count == 0)
            return Ok(new { articles = Array.Empty<object>() });

        var related = await db.ArticleTags
            .Where(at => articleTagIds.Contains(at.TagId) && at.ArticleId != id)
            .GroupBy(at => at.ArticleId)
            .Select(g => new { ArticleId = g.Key, SharedTags = g.Count() })
            .Join(db.Articles.Include(a => a.ArticleTags).ThenInclude(at => at.Tag),
                g => g.ArticleId,
                a => a.Id,
                (g, a) => new { Article = a, g.SharedTags })
            .Where(x => x.Article.Status == "published")
            .OrderByDescending(x => x.SharedTags)
            .ThenByDescending(x => x.Article.UpdatedAt)
            .Take(limit)
            .Select(x => new
            {
                x.Article.Id,
                x.Article.Title,
                x.Article.Slug,
                x.Article.Excerpt,
                x.Article.ContentType,
                UpdatedAt = x.Article.UpdatedAt.ToString("o"),
                Tags = x.Article.ArticleTags.Select(at => new { at.Tag.Id, at.Tag.Name, at.Tag.Slug }).ToList()
            })
            .ToListAsync();

        return Ok(new { articles = related });
    }
}

