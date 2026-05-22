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
        var query = db.Articles.Include(a => a.Owner).AsQueryable();

        // Viewers only see published
        if (role == "viewer")
            query = query.Where(a => a.Status == "published");
        else if (!string.IsNullOrWhiteSpace(status))
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
                    : null
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
        var article = new Article
        {
            Title = req.Title.Trim(),
            Slug = slug,
            Content = req.Content != null ? JsonSerializer.Serialize(req.Content) : null,
            Excerpt = req.Excerpt?.Trim(),
            Status = req.Status ?? "draft",
            OwnerId = userId,
            ContentType = req.ContentType ?? "reference",
            Difficulty = req.Difficulty ?? "beginner",
            CreatedViaApiKeyId = User.GetApiKeyId(),
            PublishedAt = req.Status == "published" ? DateTime.UtcNow : null,
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
            .FirstOrDefaultAsync(a => a.Id == idOrSlug || a.Slug == idOrSlug);

        if (article == null)
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
            ApiKeyName = apiKeyName
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

        if (!isOwner && !RbacService.HasPermission(role, "articles:edit_any"))
        {
            if (!RbacService.HasPermission(role, "articles:edit_own") || !isOwner)
                return StatusCode(403, new { error = "Forbidden" });
        }

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

        if (!isOwner && !RbacService.HasPermission(role, "articles:delete_any"))
        {
            if (!RbacService.HasPermission(role, "articles:delete_own") || !isOwner)
                return StatusCode(403, new { error = "Forbidden" });
        }

        db.Articles.Remove(article);
        await db.SaveChangesAsync();

        return Ok(new { message = "Article deleted" });
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
    string? ChangeSummary = null);
