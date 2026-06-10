using System.Text.Json;
using System.Text.RegularExpressions;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/articles")]
[Authorize]
public partial class ArticlesController(AppDbContext db, IConfiguration config) : ControllerBase
{
    private static readonly HashSet<string> ValidContentTypes = ["reference", "how-to", "adr", "runbook", "faq", "policy", "onboarding"];
    private static readonly HashSet<string> ValidDifficulties = ["beginner", "intermediate", "advanced"];
    private static readonly HashSet<string> ValidStatuses = ["draft", "pending", "published", "archived"];

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? contentType = null,
        [FromQuery] string? difficulty = null,
        [FromQuery] bool mine = false,
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

        if (mine)
            query = query.Where(a => a.OwnerId == userId);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(a => a.Status == status);

        if (!string.IsNullOrWhiteSpace(contentType) && ValidContentTypes.Contains(contentType))
            query = query.Where(a => a.ContentType == contentType);

        if (!string.IsNullOrWhiteSpace(difficulty) && ValidDifficulties.Contains(difficulty))
            query = query.Where(a => a.Difficulty == difficulty);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var escaped = q.Replace("%", "\\%").Replace("_", "\\_");
            query = query.Where(a => EF.Functions.Like(a.Title, $"%{escaped}%", "\\"));
        }

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
                Tags = a.ArticleTags.Select(at => new { at.Tag.Id, at.Tag.Name, at.Tag.Slug }).ToList(),
                ViewCount = db.ArticleViews.Count(v => v.ArticleId == a.Id),
                HelpfulCount = db.ArticleVotes.Count(v => v.ArticleId == a.Id && v.IsHelpful),
                NotHelpfulCount = db.ArticleVotes.Count(v => v.ArticleId == a.Id && !v.IsHelpful)
            })
            .ToListAsync();

        var articlesWithScore = articles.Select(a => new
        {
            a.Id, a.Title, a.Slug, a.Excerpt, a.Status,
            a.ContentType, a.Difficulty, a.UpdatedAt,
            a.OwnerName, a.ApiKeyName, a.Tags, a.ViewCount,
            WilsonScore = CalculateWilsonScore(a.HelpfulCount, a.NotHelpfulCount)
        });

        return Ok(new
        {
            articles = articlesWithScore,
            total
        });
    }

    [HttpPost]
    [RequirePermission(Permissions.ArticlesCreate)]
    public async Task<IActionResult> Create([FromBody] CreateArticleRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title) || req.Title.Length > 300)
            return BadRequest(new { error = "Title is required (1-300 chars)" });

        if (req.ContentType != null && !ValidContentTypes.Contains(req.ContentType))
            return BadRequest(new { error = $"Invalid contentType. Allowed: {string.Join(", ", ValidContentTypes)}" });

        if (req.Difficulty != null && !ValidDifficulties.Contains(req.Difficulty))
            return BadRequest(new { error = $"Invalid difficulty. Allowed: {string.Join(", ", ValidDifficulties)}" });

        if (req.Status != null && !ValidStatuses.Contains(req.Status))
            return BadRequest(new { error = $"Invalid status. Allowed: {string.Join(", ", ValidStatuses)}" });

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
            Audience = req.Audience?.Trim(),
            CreatedViaApiKeyId = User.GetApiKeyId(),
            PublishedAt = articleStatus == "published" ? DateTime.UtcNow : null,
            LastReviewedAt = articleStatus == "published" ? DateTime.UtcNow : null,
            ReadTimeMinutes = CalculateReadTime(req.Content != null ? JsonSerializer.Serialize(req.Content) : null),
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

        // Tags (supports ID, name, or slug; auto-creates when via API key)
        if (req.Tags?.Length > 0)
        {
            var isApiKey = User.GetSource() == "api-key";
            foreach (var tagInput in req.Tags)
            {
                var tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == tagInput)
                       ?? await db.Tags.FirstOrDefaultAsync(t => t.Name == tagInput || t.Slug == tagInput);
                if (tag == null && isApiKey && !string.IsNullOrWhiteSpace(tagInput))
                {
                    var tagSlug = GenerateTagSlug(tagInput);
                    tag = new Tag { Name = tagInput.Trim(), Slug = tagSlug };
                    db.Tags.Add(tag);
                    await db.SaveChangesAsync();
                }
                if (tag != null)
                    db.ArticleTags.Add(new ArticleTag { ArticleId = article.Id, TagId = tag.Id });
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

        var apiKeyName = article.CreatedViaApiKeyId != null
            ? await db.ApiKeys.Where(k => k.Id == article.CreatedViaApiKeyId).Select(k => k.Name).FirstOrDefaultAsync()
            : null;

        var viewCount = await db.ArticleViews.CountAsync(v => v.ArticleId == article.Id);

        return Ok(new
        {
            article.Id, article.Title, article.Slug, article.Excerpt,
            Content = article.Content != null ? JsonSerializer.Deserialize<object>(article.Content) : null,
            article.Status, article.ContentType, article.Difficulty,
            article.OwnerId, article.Audience, article.ReadTimeMinutes,
            UpdatedAt = article.UpdatedAt.ToString("o"),
            PublishedAt = article.PublishedAt?.ToString("o"),
            LastReviewedAt = article.LastReviewedAt?.ToString("o"),
            OwnerName = article.Owner.Name,
            ApiKeyName = apiKeyName,
            Tags = article.ArticleTags.Select(at => new { at.Tag.Id, at.Tag.Name, at.Tag.Slug }).ToList(),
            ViewCount = viewCount
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

        var canEditAny = RbacService.HasPermission(role, Permissions.ArticlesEditAny);
        var canEditOwn = RbacService.HasPermission(role, Permissions.ArticlesEditOwn) && isOwner;
        if (!canEditAny && !canEditOwn)
            return StatusCode(403, new { error = "You do not have permission to edit this article" });

        if (req.ContentType != null && !ValidContentTypes.Contains(req.ContentType))
            return BadRequest(new { error = $"Invalid contentType. Allowed: {string.Join(", ", ValidContentTypes)}" });

        if (req.Difficulty != null && !ValidDifficulties.Contains(req.Difficulty))
            return BadRequest(new { error = $"Invalid difficulty. Allowed: {string.Join(", ", ValidDifficulties)}" });

        if (req.Status != null && !ValidStatuses.Contains(req.Status))
            return BadRequest(new { error = $"Invalid status. Allowed: {string.Join(", ", ValidStatuses)}" });

        var contentChanged = false;
        if (req.Title != null) { article.Title = req.Title.Trim(); }
        if (req.Content != null)
        {
            article.Content = JsonSerializer.Serialize(req.Content);
            article.ReadTimeMinutes = CalculateReadTime(article.Content);
            contentChanged = true;
        }
        if (req.Excerpt != null) article.Excerpt = req.Excerpt.Trim();
        if (req.ContentType != null) article.ContentType = req.ContentType;
        if (req.Difficulty != null) article.Difficulty = req.Difficulty;
        if (req.Audience != null) article.Audience = req.Audience.Trim();
        if (req.Status != null)
        {
            // Publishing requires articles:publish permission
            if (req.Status == "published" && article.Status != "published"
                && !RbacService.HasPermission(role, Permissions.ArticlesPublish))
                return StatusCode(403, new { error = "You do not have permission to publish articles" });

            // Archiving requires articles:archive permission
            if (req.Status == "archived" && article.Status != "archived"
                && !RbacService.HasPermission(role, Permissions.ArticlesArchive))
                return StatusCode(403, new { error = "You do not have permission to archive articles" });

            if (req.Status == "published" && article.Status != "published")
                article.PublishedAt = DateTime.UtcNow;
            if (req.Status == "published")
                article.LastReviewedAt = DateTime.UtcNow;
            article.Status = req.Status;
        }

        article.UpdatedAt = DateTime.UtcNow;

        // Create version if content changed
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

        // Update tags (supports ID, name, or slug; auto-creates when via API key)
        if (req.Tags != null)
        {
            var isApiKey = User.GetSource() == "api-key";
            var existingTags = await db.ArticleTags.Where(at => at.ArticleId == id).ToListAsync();
            db.ArticleTags.RemoveRange(existingTags);
            foreach (var tagInput in req.Tags)
            {
                var tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == tagInput)
                       ?? await db.Tags.FirstOrDefaultAsync(t => t.Name == tagInput || t.Slug == tagInput);
                if (tag == null && isApiKey && !string.IsNullOrWhiteSpace(tagInput))
                {
                    var tagSlug = GenerateTagSlug(tagInput);
                    tag = new Tag { Name = tagInput.Trim(), Slug = tagSlug };
                    db.Tags.Add(tag);
                    await db.SaveChangesAsync();
                }
                if (tag != null)
                    db.ArticleTags.Add(new ArticleTag { ArticleId = id, TagId = tag.Id });
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

        var canDeleteAny = RbacService.HasPermission(role, Permissions.ArticlesDeleteAny);
        var canDeleteOwn = RbacService.HasPermission(role, Permissions.ArticlesDeleteOwn) && isOwner;
        if (!canDeleteAny && !canDeleteOwn)
            return StatusCode(403, new { error = "You do not have permission to delete this article" });

        // Clean up attachment files from disk
        var basePath = config["FileStorage:BasePath"] ?? "../data/uploads";
        var articleDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), basePath, id));
        if (Directory.Exists(articleDir))
            Directory.Delete(articleDir, true);

        db.Articles.Remove(article);
        await db.SaveChangesAsync();

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
                x.Article.Difficulty,
                UpdatedAt = x.Article.UpdatedAt.ToString("o"),
                Tags = x.Article.ArticleTags.Select(at => new { at.Tag.Id, at.Tag.Name, at.Tag.Slug }).ToList()
            })
            .ToListAsync();

        return Ok(new { articles = related });
    }

    private static string GenerateSlug(string title)
    {
        var slug = title.ToLowerInvariant().Trim();
        slug = SlugRegex().Replace(slug, "");
        slug = WhitespaceRegex().Replace(slug, "-");
        slug = slug.Trim('-');
        return slug.Length > 100 ? slug[..100] : slug;
    }

    private static string GenerateTagSlug(string name)
    {
        var slug = TagSlugRegex().Replace(name.ToLowerInvariant().Trim(), "-").Trim('-');
        return slug.Length > 50 ? slug[..50] : slug;
    }

    [GeneratedRegex(@"[^a-z0-9\s-]")]
    private static partial Regex SlugRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex TagSlugRegex();

    /// <summary>
    /// Estimates read time based on ~200 words per minute.
    /// Extracts text from TipTap JSON content.
    /// </summary>
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
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Name != "text" && prop.Name != "content")
                        sb.Append(ExtractTextFromJson(prop.Value));
                }
                return sb.ToString();
            case JsonValueKind.Array:
                var arrSb = new System.Text.StringBuilder();
                foreach (var item in element.EnumerateArray())
                    arrSb.Append(ExtractTextFromJson(item)).Append(' ');
                return arrSb.ToString();
            default:
                return "";
        }
    }

    private static double CalculateWilsonScore(int positive, int negative)
    {
        var n = positive + negative;
        if (n == 0) return 0;

        const double z = 1.96;
        var phat = (double)positive / n;
        var denominator = 1 + z * z / n;
        var centre = phat + z * z / (2 * n);
        var spread = z * Math.Sqrt((phat * (1 - phat) + z * z / (4 * n)) / n);

        return Math.Round((centre - spread) / denominator, 4);
    }
}

