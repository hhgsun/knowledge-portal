using System.Text.Json;
using System.Text.RegularExpressions;
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
public partial class ArticlesController(AppDbContext db, IConfiguration config, FullTextSearchService ftsService) : ControllerBase
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
        var query = db.Articles.Include(a => a.Owner).Include(a => a.ArticleTags).ThenInclude(at => at.Tag).AsQueryable();

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
                query = query.Where(a => ctValues.Contains(a.ContentType));
        }

        if (tag is { Length: > 0 })
        {
            var tagSlugs = tag.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (tagSlugs.Count > 0)
            {
                // AND logic: article must have ALL specified tags
                foreach (var tagSlug in tagSlugs)
                {
                    query = query.Where(a => a.ArticleTags.Any(at => at.Tag.Slug == tagSlug));
                }
            }
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

        var total = await query.CountAsync();
        var articles = await query
            .OrderByDescending(a => a.UpdatedAt)
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(a => new
            {
                a.Id, a.Title, a.Slug, a.Excerpt, a.Status,
                a.ContentType,
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

        // Attachment map if requested
        Dictionary<string, List<object>>? attachmentMap = null;
        if (includeAttachments)
        {
            var articleIds = articles.Select(a => a.Id).ToList();
            var attachments = await db.ArticleAttachments
                .Where(att => articleIds.Contains(att.ArticleId))
                .Select(att => new { att.Id, att.ArticleId, att.FileName, att.ContentType, att.SizeBytes })
                .ToListAsync();
            attachmentMap = attachments.GroupBy(att => att.ArticleId).ToDictionary(
                g => g.Key,
                g => g.Select(att => (object)new { att.Id, att.FileName, att.ContentType, att.SizeBytes, DownloadUrl = $"/api/attachments/{att.Id}/download" }).ToList());
        }

        // Content map if requested
        Dictionary<string, string?>? contentMap = null;
        if (includeContent)
        {
            var articleIds = articles.Select(a => a.Id).ToList();
            var contents = await db.Articles
                .Where(a => articleIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Content })
                .ToListAsync();
            contentMap = contents.ToDictionary(c => c.Id, c => ExtractPlainText(c.Content));
        }

        var articlesWithScore = articles.Select(a => new
        {
            a.Id, a.Title, a.Slug, a.Excerpt, a.Status,
            a.ContentType, a.UpdatedAt,
            a.OwnerName, a.ApiKeyName, a.Tags, a.ViewCount,
            WilsonScore = SlugHelper.WilsonScore(a.HelpfulCount, a.NotHelpfulCount),
            Content = includeContent ? contentMap?.GetValueOrDefault(a.Id) : null,
            Attachments = includeAttachments ? attachmentMap?.GetValueOrDefault(a.Id) : null
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

        var validContentTypes = await GetValidContentTypesAsync();
        if (req.ContentType != null && !validContentTypes.Contains(req.ContentType))
            return BadRequest(new { error = $"Invalid contentType. Allowed: {string.Join(", ", validContentTypes)}" });

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
            CreatedViaApiKeyId = User.GetApiKeyId(),
            PublishedAt = articleStatus == "published" ? DateTime.UtcNow : null,
            LastReviewedAt = articleStatus == "published" ? DateTime.UtcNow : null,
            ReadTimeMinutes = ContentExtractor.CalculateReadTime(req.Content != null ? JsonSerializer.Serialize(req.Content) : null),
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

        // Dirty flag: if published, queue for embedding
        if (article.Status == "published")
            article.IndexedAt = null;

        await db.SaveChangesAsync();

        // Sync FTS index
        await ftsService.SyncArticleAsync(article);

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

        var attachments = await db.ArticleAttachments
            .Where(a => a.ArticleId == article.Id)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new { a.Id, a.FileName, a.ContentType, a.SizeBytes, DownloadUrl = $"/api/attachments/{a.Id}/download" })
            .ToListAsync();

        return Ok(new
        {
            article.Id, article.Title, article.Slug, article.Excerpt,
            Content = article.Content != null ? JsonSerializer.Deserialize<object>(article.Content) : null,
            ContentText = ExtractPlainText(article.Content),
            article.Status, article.ContentType,
            article.OwnerId, article.ReadTimeMinutes,
            UpdatedAt = article.UpdatedAt.ToString("o"),
            PublishedAt = article.PublishedAt?.ToString("o"),
            LastReviewedAt = article.LastReviewedAt?.ToString("o"),
            OwnerName = article.Owner.Name,
            ApiKeyName = apiKeyName,
            Tags = article.ArticleTags.Select(at => new { at.Tag.Id, at.Tag.Name, at.Tag.Slug }).ToList(),
            ViewCount = viewCount,
            Attachments = attachments
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

        var validContentTypes = await GetValidContentTypesAsync();
        if (req.ContentType != null && !validContentTypes.Contains(req.ContentType))
            return BadRequest(new { error = $"Invalid contentType. Allowed: {string.Join(", ", validContentTypes)}" });

        if (req.Status != null && !ValidStatuses.Contains(req.Status))
            return BadRequest(new { error = $"Invalid status. Allowed: {string.Join(", ", ValidStatuses)}" });

        var contentChanged = false;
        if (req.Title != null) { article.Title = req.Title.Trim(); }
        if (req.Content != null)
        {
            article.Content = JsonSerializer.Serialize(req.Content);
            article.ReadTimeMinutes = ContentExtractor.CalculateReadTime(article.Content);
            contentChanged = true;
        }
        if (req.Excerpt != null) article.Excerpt = req.Excerpt.Trim();
        if (req.ContentType != null) article.ContentType = req.ContentType;
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

        // Sync FTS index (handles published/unpublished state)
        await ftsService.SyncArticleAsync(article);

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

        // Remove from FTS index
        await ftsService.RemoveArticleAsync(id);

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
        article.IndexedAt = null; // Dirty flag: approved → queue for embedding
        await db.SaveChangesAsync();

        // Sync FTS index
        await ftsService.SyncArticleAsync(article);

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

    private static string GenerateSlug(string title)
    {
        var slug = SlugHelper.Transliterate(title.ToLowerInvariant().Trim());
        slug = SlugRegex().Replace(slug, "");
        slug = WhitespaceRegex().Replace(slug, "-");
        slug = slug.Trim('-');
        return slug.Length > 100 ? slug[..100] : slug;
    }

    private static string GenerateTagSlug(string name)
    {
        var slug = SlugHelper.Transliterate(name.ToLowerInvariant().Trim());
        slug = TagSlugRegex().Replace(slug, "-").Trim('-');
        return slug.Length > 50 ? slug[..50] : slug;
    }



    [GeneratedRegex(@"[^a-z0-9\s-]")]
    private static partial Regex SlugRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex TagSlugRegex();

    private static string? ExtractPlainText(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson)) return null;
        try
        {
            var text = ContentExtractor.ExtractTextFromJson(System.Text.Json.JsonDocument.Parse(contentJson).RootElement);
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch { return null; }
    }
}

