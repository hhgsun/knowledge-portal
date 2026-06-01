using System.Text.Json;
using System.Text.RegularExpressions;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/articles")]
[Authorize]
public partial class ArticlesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? q = null)
    {
        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, 100);

        var role = User.GetRole();
        var userId = User.GetUserId();
        var query = db.Articles.Include(a => a.Owner).Include(a => a.ArticleTags).ThenInclude(at => at.Tag).AsQueryable();

        // Viewers see published + their own articles
        if (role == "viewer")
            query = query.Where(a => a.Status == "published" || a.OwnerId == userId);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(a => a.Status == status);

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(a => a.Title.Contains(q));

        var total = await query.CountAsync();
        var articles = await query
            .OrderByDescending(a => a.UpdatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(a => new
            {
                a.Id, a.Title, a.Slug, a.Excerpt, a.Status,
                a.ContentType, a.Difficulty,
                UpdatedAt = a.UpdatedAt.ToString("o"),
                OwnerName = a.Owner.Name,
                ApiKeyName = a.CreatedViaApiKeyId != null
                    ? db.ApiKeys.Where(k => k.Id == a.CreatedViaApiKeyId).Select(k => k.Name).FirstOrDefault()
                    : null,
                Tags = a.ArticleTags.Select(at => new { at.Tag.Id, at.Tag.Name, at.Tag.Slug }).ToList()
            })
            .ToListAsync();

        return Ok(new
        {
            articles,
            total
        });
    }

    [HttpPost]
    [RequirePermission("articles:create")]
    public async Task<IActionResult> Create([FromBody] CreateArticleRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title) || req.Title.Length > 300)
            return BadRequest(new { error = "Title is required (1-300 chars)" });

        var slug = GenerateSlug(req.Title);
        // Ensure unique slug
        var baseSlug = slug;
        var counter = 1;
        while (await db.Articles.AnyAsync(a => a.Slug == slug))
        {
            slug = $"{baseSlug}-{counter++}";
        }

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
            Content = req.Content != null ? JsonSerializer.Serialize(req.Content) : null,
            Excerpt = req.Excerpt?.Trim(),
            Status = articleStatus,
            OwnerId = userId,
            ContentType = req.ContentType ?? "reference",
            Difficulty = req.Difficulty ?? "beginner",
            CreatedViaApiKeyId = User.GetApiKeyId(),
            PublishedAt = articleStatus == "published" ? DateTime.UtcNow : null,
        };

        db.Articles.Add(article);

        // Initial version
        db.ArticleVersions.Add(new ArticleVersion
        {
            ArticleId = article.Id,
            Title = article.Title,
            Content = article.Content,
            ChangedBy = userId,
            ChangeSummary = "Initial version",
            Version = 1
        });

        // Tags
        if (req.Tags?.Length > 0)
        {
            foreach (var tagId in req.Tags)
            {
                if (await db.Tags.AnyAsync(t => t.Id == tagId))
                    db.ArticleTags.Add(new ArticleTag { ArticleId = article.Id, TagId = tagId });
            }
        }

        await db.SaveChangesAsync();
        return StatusCode(201, new { article.Id, article.Slug, article.Title });
    }

    [HttpGet("{idOrSlug}")]
    public async Task<IActionResult> Get(string idOrSlug)
    {
        var article = await db.Articles
            .Include(a => a.Owner)
            .Include(a => a.ArticleTags).ThenInclude(at => at.Tag)
            .FirstOrDefaultAsync(a => a.Id == idOrSlug || a.Slug == idOrSlug);

        if (article == null)
            return NotFound(new { error = "Article not found" });

        // Viewers can only see published articles or their own
        var role = User.GetRole();
        var userId = User.GetUserId();
        if (role == "viewer" && article.Status != "published" && article.OwnerId != userId)
            return NotFound(new { error = "Article not found" });

        // Record view
        db.ArticleViews.Add(new ArticleView
        {
            ArticleId = article.Id,
            UserId = User.Identity?.IsAuthenticated == true ? User.GetUserId() : null
        });
        await db.SaveChangesAsync();

        var apiKeyName = article.CreatedViaApiKeyId != null
            ? await db.ApiKeys.Where(k => k.Id == article.CreatedViaApiKeyId).Select(k => k.Name).FirstOrDefaultAsync()
            : null;

        return Ok(new
        {
            article.Id, article.Title, article.Slug, article.Excerpt,
            Content = article.Content != null ? JsonSerializer.Deserialize<object>(article.Content) : null,
            article.Status, article.ContentType, article.Difficulty,
            article.OwnerId, article.Audience,
            UpdatedAt = article.UpdatedAt.ToString("o"),
            PublishedAt = article.PublishedAt?.ToString("o"),
            LastReviewedAt = article.LastReviewedAt?.ToString("o"),
            OwnerName = article.Owner.Name,
            ApiKeyName = apiKeyName,
            Tags = article.ArticleTags.Select(at => new { at.Tag.Id, at.Tag.Name, at.Tag.Slug }).ToList()
        });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateArticleRequest req)
    {
        var article = await db.Articles.FindAsync(id);
        if (article == null) return NotFound(new { error = "Article not found" });

        var userId = User.GetUserId();
        var role = User.GetRole();
        var isOwner = article.OwnerId == userId;

        var canEditAny = RbacService.HasPermission(role, "articles:edit_any");
        var canEditOwn = RbacService.HasPermission(role, "articles:edit_own") && isOwner;
        if (!canEditAny && !canEditOwn)
            return StatusCode(403, new { error = "You do not have permission to edit this article" });

        var contentChanged = false;
        if (req.Title != null) { article.Title = req.Title.Trim(); }
        if (req.Content != null)
        {
            article.Content = JsonSerializer.Serialize(req.Content);
            contentChanged = true;
        }
        if (req.Excerpt != null) article.Excerpt = req.Excerpt.Trim();
        if (req.ContentType != null) article.ContentType = req.ContentType;
        if (req.Difficulty != null) article.Difficulty = req.Difficulty;
        if (req.Status != null)
        {
            // Viewers can only set draft or pending
            if (role == "viewer" && req.Status != "draft" && req.Status != "pending")
                return StatusCode(403, new { error = "You can only save as draft or submit for review" });

            if (req.Status == "published" && article.Status != "published")
                article.PublishedAt = DateTime.UtcNow;
            article.Status = req.Status;
        }
        article.UpdatedAt = DateTime.UtcNow;

        // Create version if content/title changed
        if (contentChanged)
        {
            var maxVersion = await db.ArticleVersions
                .Where(v => v.ArticleId == id)
                .MaxAsync(v => (int?)v.Version) ?? 0;

            db.ArticleVersions.Add(new ArticleVersion
            {
                ArticleId = id,
                Title = article.Title,
                Content = article.Content,
                ChangedBy = userId,
                ChangeSummary = req.ChangeSummary?.Trim(),
                Version = maxVersion + 1
            });
        }

        // Update tags
        if (req.Tags != null)
        {
            var existingTags = await db.ArticleTags.Where(at => at.ArticleId == id).ToListAsync();
            db.ArticleTags.RemoveRange(existingTags);
            foreach (var tagId in req.Tags)
            {
                if (await db.Tags.AnyAsync(t => t.Id == tagId))
                    db.ArticleTags.Add(new ArticleTag { ArticleId = id, TagId = tagId });
            }
        }

        await db.SaveChangesAsync();

        // Update slug if title changed
        if (req.Title != null)
        {
            var newSlug = GenerateSlug(req.Title);
            if (newSlug != article.Slug && !await db.Articles.AnyAsync(a => a.Slug == newSlug && a.Id != id))
            {
                article.Slug = newSlug;
                await db.SaveChangesAsync();
            }
        }

        return Ok(new { article.Id, article.Slug, article.Title });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var article = await db.Articles.FindAsync(id);
        if (article == null) return NotFound(new { error = "Article not found" });

        var userId = User.GetUserId();
        var role = User.GetRole();
        var isOwner = article.OwnerId == userId;

        var canDeleteAny = RbacService.HasPermission(role, "articles:delete_any");
        var canDeleteOwn = RbacService.HasPermission(role, "articles:delete_own") && isOwner;
        if (!canDeleteAny && !canDeleteOwn)
            return StatusCode(403, new { error = "You do not have permission to delete this article" });

        db.Articles.Remove(article);
        await db.SaveChangesAsync();

        return Ok(new { message = "Article deleted" });
    }

    [HttpPost("{id}/approve")]
    [RequirePermission("articles:approve")]
    public async Task<IActionResult> Approve(string id)
    {
        var article = await db.Articles.FindAsync(id);
        if (article == null) return NotFound(new { error = "Article not found" });

        if (article.Status != "pending")
            return BadRequest(new { error = "Only pending articles can be approved" });

        article.Status = "published";
        article.PublishedAt = DateTime.UtcNow;
        article.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { message = "Article approved and published", article.Id, article.Slug });
    }

    [HttpPost("{id}/reject")]
    [RequirePermission("articles:approve")]
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

    private static string GenerateSlug(string title)
    {
        var slug = title.ToLowerInvariant().Trim();
        slug = SlugRegex().Replace(slug, "");
        slug = WhitespaceRegex().Replace(slug, "-");
        slug = slug.Trim('-');
        return slug.Length > 100 ? slug[..100] : slug;
    }

    [GeneratedRegex(@"[^a-z0-9\s-]")]
    private static partial Regex SlugRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

public record CreateArticleRequest(
    string Title,
    object? Content = null,
    string? Excerpt = null,
    string? Status = null,
    string? ContentType = null,
    string? Difficulty = null,
    string? Audience = null,
    string[]? Tags = null);

public record UpdateArticleRequest(
    string? Title = null,
    object? Content = null,
    string? Excerpt = null,
    string? Status = null,
    string? ContentType = null,
    string? Difficulty = null,
    string? ChangeSummary = null,
    string[]? Tags = null);
