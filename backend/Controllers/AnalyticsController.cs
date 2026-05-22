using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/analytics")]
[Authorize]
[RequirePermission("analytics:view")]
public class AnalyticsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        if (User.GetSource() == "api-key")
            return StatusCode(403, new { error = "Analytics requires session auth" });

        var now = DateTime.UtcNow;
        var weekAgo = now.AddDays(-7);
        var dayAgo = now.AddDays(-1);
        var staleThreshold = now.AddDays(-90);

        var totalArticles = await db.Articles.CountAsync();

        var articlesByStatus = await db.Articles
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count);

        var viewsThisWeek = await db.ArticleViews.CountAsync(v => v.CreatedAt >= weekAgo);
        var searchesToday = await db.SearchQueries.CountAsync(s => s.CreatedAt >= dayAgo);

        var staleArticles = await db.Articles.CountAsync(a =>
            a.Status == "published" && a.LastReviewedAt != null && a.LastReviewedAt < staleThreshold);

        var topSearches = await db.SearchQueries
            .Where(s => s.CreatedAt >= weekAgo)
            .GroupBy(s => s.Query)
            .Select(g => new { Query = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync();

        var failedSearches = await db.SearchQueries
            .Where(s => s.ResultsCount == 0)
            .GroupBy(s => s.Query)
            .Select(g => new { Query = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync();

        var topArticles = await db.ArticleViews
            .Where(v => v.CreatedAt >= weekAgo)
            .GroupBy(v => v.ArticleId)
            .Select(g => new
            {
                ArticleId = g.Key,
                Views = g.Count()
            })
            .OrderByDescending(x => x.Views)
            .Take(10)
            .ToListAsync();

        // Enrich top articles with title/slug
        var articleIds = topArticles.Select(a => a.ArticleId).ToList();
        var articleInfos = await db.Articles
            .Where(a => articleIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Title, a.Slug })
            .ToDictionaryAsync(a => a.Id);

        var enrichedTopArticles = topArticles.Select(a => new
        {
            a.ArticleId,
            Title = articleInfos.TryGetValue(a.ArticleId, out var info) ? info.Title : "Unknown",
            Slug = articleInfos.TryGetValue(a.ArticleId, out var info2) ? info2.Slug : "",
            a.Views
        });

        return Ok(new
        {
            overview = new
            {
                totalArticles,
                articlesByStatus,
                viewsThisWeek,
                searchesToday,
                staleArticles
            },
            topSearches = topSearches.Select(s => new { query = s.Query, count = s.Count }),
            failedSearches = failedSearches.Select(s => new { query = s.Query, count = s.Count }),
            topArticles = enrichedTopArticles.Select(a => new { articleId = a.ArticleId, title = a.Title, slug = a.Slug, views = a.Views })
        });
    }
}
