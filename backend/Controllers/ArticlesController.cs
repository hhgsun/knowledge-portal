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
    ILogger<ArticlesController> logger) : ControllerBase
{
    private static readonly HashSet<string> ValidStatuses = ["draft", "pending", "published", "archived"];

    private async Task<HashSet<string>> GetValidContentTypesAsync()
        => (await db.LookupValues.Where(l => l.Category == "content_type" && l.IsActive).Select(l => l.Value).ToListAsync()).ToHashSet();

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
            var validContentTypes = await GetValidContentTypesAsync();
            var ctValues = contentType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(ct => validContentTypes.Contains(ct)).ToList();
            if (ctValues.Count > 0)
                query = query.WhereContentTypeIn(ctValues);
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
            includeContent: includeContent, includeAttachments: includeAttachments);

        return Ok(new { articles, total });
    }

    [HttpPost]
    [RequirePermission(Permissions.ArticlesCreate)]
    public async Task<IActionResult> Create([FromBody] CreateArticleRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title) || req.Title.Length > 300)
            return BadRequest(new { error = "Title is required (1-300 chars)" });

        var validContentTypes = await GetValidContentTypesAsync();
        if (req.ContentType != null && !validContentTypes.Contains(req.ContentType))
            return BadRequest(new { error = $"Invalid contentType. Allowed: {string.Join(", ", validContentTypes)}" });

        if (req.Status != null && !ValidStatuses.Contains(req.Status))
            return BadRequest(new { error = $"Invalid status. Allowed: {string.Join(", ", ValidStatuses)}" });

        var slug = await db.GenerateUniqueArticleSlugAsync(req.Title);

        var userId = User.GetUserId();
        var role = User.GetRole();

        // Viewers can only create as draft or pending
        var articleStatus = req.Status ?? "draft";
        if (role == "viewer" && articleStatus != "draft" && articleStatus != "pending")
            articleStatus = "draft";

        var article = new Article
        {
            Title = req.Title.Trim(),
            Slug = slug,
            Content = req.ContentMarkdown?.Trim(),
            Excerpt = req.Excerpt?.Trim(),
            Status = articleStatus,
            OwnerId = userId,
            ContentType = req.ContentType ?? "reference",
            CreatedViaApiKeyId = User.GetApiKeyId(),
            PublishedAt = articleStatus == "published" ? DateTime.UtcNow : null,
            LastReviewedAt = articleStatus == "published" ? DateTime.UtcNow : null,
            ReadTimeMinutes = ContentExtractor.CalculateReadTime(req.ContentMarkdown),
        };

        db.Articles.Add(article);

        await articleService.AddVersionAsync(article.Id, article.Title, article.Content, userId, "Initial version");

        if (req.Tags?.Length > 0)
            await articleService.AttachTagsAsync(article.Id, req.Tags,
                User.GetSource() == "api-key" || RbacService.HasPermission(User, Permissions.TagsManage));

        await db.SaveChangesAsync();

        // If published: queue for embedding + sync FTS index
        await articleService.QueueReindexAsync(article);

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

        return Ok(await articleService.BuildDetailAsync(article));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateArticleRequest req)
    {
        var article = await db.Articles.FindAsync(id);
        if (article == null) return NotFound(new { error = "Article not found" });

        var userId = User.GetUserId();

        if (!RbacService.CanEditArticle(User, article.OwnerId == userId))
            return StatusCode(403, new { error = "You do not have permission to edit this article" });

        var validContentTypes = await GetValidContentTypesAsync();
        if (req.ContentType != null && !validContentTypes.Contains(req.ContentType))
            return BadRequest(new { error = $"Invalid contentType. Allowed: {string.Join(", ", validContentTypes)}" });

        if (req.Status != null && !ValidStatuses.Contains(req.Status))
            return BadRequest(new { error = $"Invalid status. Allowed: {string.Join(", ", ValidStatuses)}" });

        var contentChanged = false;
        if (req.Title != null) { article.Title = req.Title.Trim(); }
        if (req.ContentMarkdown != null)
        {
            article.Content = req.ContentMarkdown.Trim();
            article.ReadTimeMinutes = ContentExtractor.CalculateReadTime(article.Content);
            contentChanged = true;
        }
        if (req.Excerpt != null) article.Excerpt = req.Excerpt.Trim();
        if (req.ContentType != null) article.ContentType = req.ContentType;
        if (req.Status != null)
        {
            // Publishing requires articles:publish permission
            if (req.Status == "published" && article.Status != "published"
                && !RbacService.HasPermission(User, Permissions.ArticlesPublish))
                return StatusCode(403, new { error = "You do not have permission to publish articles" });

            // Archiving requires articles:archive permission
            if (req.Status == "archived" && article.Status != "archived"
                && !RbacService.HasPermission(User, Permissions.ArticlesArchive))
                return StatusCode(403, new { error = "You do not have permission to archive articles" });

            if (req.Status == "published" && article.Status != "published")
            {
                article.PublishedAt = DateTime.UtcNow;
                article.IndexedAt = null; // Dirty flag: newly published → queue for embedding
            }
            if (req.Status == "published")
                article.LastReviewedAt = DateTime.UtcNow;

            // Unpublishing: remove embeddings
            if (req.Status != "published" && article.Status == "published")
            {
                article.IndexedAt = null;
                var embeddings = await db.ArticleEmbeddings.Where(e => e.ArticleId == id).ToListAsync();
                if (embeddings.Count > 0)
                    db.ArticleEmbeddings.RemoveRange(embeddings);
            }

            article.Status = req.Status;
        }

        article.UpdatedAt = DateTime.UtcNow;

        // Dirty flag: content changed on published article → re-embed
        if (contentChanged && article.Status == "published")
            article.IndexedAt = null;

        // Create version if content changed
        if (contentChanged)
            await articleService.AddVersionAsync(id, article.Title, article.Content, userId, req.ChangeSummary?.Trim());

        if (req.Tags != null)
        {
            var existingTags = await db.ArticleTags.Where(at => at.ArticleId == id).ToListAsync();
            db.ArticleTags.RemoveRange(existingTags);
            await articleService.AttachTagsAsync(id, req.Tags,
                User.GetSource() == "api-key" || RbacService.HasPermission(User, Permissions.TagsManage));
        }

        await db.SaveChangesAsync();

        // Update slug if title changed
        if (req.Title != null)
        {
            var newSlug = SlugHelper.GenerateArticleSlug(req.Title);
            if (newSlug != article.Slug && !await db.Articles.AnyAsync(a => a.Slug == newSlug && a.Id != id))
            {
                article.Slug = newSlug;
                await db.SaveChangesAsync();
            }
        }

        // Coalesced durable job handles FTS + semantic indexing (including title/excerpt/status).
        await articleService.QueueReindexAsync(article);

        return Ok(new { article.Id, article.Slug, article.Title });
    }

    [HttpDelete("{id}")]
    [RequireSessionAuth] // destructive deletes are session-only — API keys cannot delete
    public async Task<IActionResult> Delete(string id)
    {
        var article = await db.Articles.FindAsync(id);
        if (article == null) return NotFound(new { error = "Article not found" });

        var userId = User.GetUserId();

        if (!RbacService.CanDeleteArticle(User, article.OwnerId == userId))
            return StatusCode(403, new { error = "You do not have permission to delete this article" });

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

        if (article.Status != "pending")
            return BadRequest(new { error = "Only pending articles can be approved" });

        article.Status = "published";
        article.PublishedAt = DateTime.UtcNow;
        article.LastReviewedAt = DateTime.UtcNow;
        article.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Approved → queue for embedding + sync FTS index
        await articleService.QueueReindexAsync(article);

        return Ok(new { message = "Article approved and published", article.Id, article.Slug });
    }

    [HttpPost("{id}/reject")]
    [RequirePermission(Permissions.ArticlesApprove)]
    public async Task<IActionResult> Reject(string id)
    {
        var article = await db.Articles.FindAsync(id);
        if (article == null) return NotFound(new { error = "Article not found" });

        if (article.Status != "pending")
            return BadRequest(new { error = "Only pending articles can be rejected" });

        article.Status = "draft";
        article.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { message = "Article rejected and returned to draft", article.Id, article.Slug });
    }

    [HttpGet("{id}/related")]
    public async Task<IActionResult> Related(string id, [FromQuery] int limit = 5)
    {
        limit = Math.Clamp(limit, 1, 20);

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
            .OrderByDescending(g => g.SharedTags)
            .Take(limit)
            .Join(db.Articles.Include(a => a.ArticleTags).ThenInclude(at => at.Tag),
                g => g.ArticleId,
                a => a.Id,
                (g, a) => new { Article = a, g.SharedTags })
            .Where(x => x.Article.Status == "published")
            .OrderByDescending(x => x.SharedTags)
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

