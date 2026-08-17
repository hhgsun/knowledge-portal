using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/analytics")]
[Authorize]
[RequirePermission(Permissions.AnalyticsView)]
[RequireSessionAuth]
public class AnalyticsController(AppDbContext db, StatsService statsService, UsageAnalyticsService usageAnalyticsService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 30)
    {
        days = Math.Clamp(days, 1, 365);
        var overview = await statsService.GetOverviewAsync();

        var articlesByStatus = await db.Articles
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count);

        var topSearches = await statsService.GetTopSearchesAsync(10);
        var failedSearches = await statsService.GetFailedSearchesAsync(10);

        var weekAgo = DateTime.UtcNow.AddDays(-7);
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

        var usage = await usageAnalyticsService.GetAsync(days);

        return Ok(new
        {
            overview = new
            {
                totalArticles = overview.TotalArticles,
                articlesByStatus,
                viewsThisWeek = overview.ViewsThisWeek,
                searchesToday = overview.SearchesToday,
                staleArticles = overview.StaleArticles
            },
            topSearches = topSearches.Select(s => new { query = s.Query, count = s.Count }),
            failedSearches = failedSearches.Select(s => new { query = s.Query, count = s.Count }),
            topArticles = enrichedTopArticles.Select(a => new { articleId = a.ArticleId, title = a.Title, slug = a.Slug, views = a.Views }),
            usage
        });
    }
}
