using KnowledgePortal.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var now = DateTime.UtcNow;
        var weekAgo = now.AddDays(-7);
        var dayAgo = now.AddDays(-1);
        var staleThreshold = now.AddDays(-90);

        var totalArticles = await db.Articles.CountAsync();
        var viewsThisWeek = await db.ArticleViews.CountAsync(v => v.CreatedAt >= weekAgo);
        var searchesToday = await db.SearchQueries.CountAsync(s => s.CreatedAt >= dayAgo);

        var staleCount = await db.Articles.CountAsync(a =>
            a.Status == "published" && a.LastReviewedAt != null && a.LastReviewedAt < staleThreshold);

        var recentArticles = await db.Articles
            .Where(a => a.Status == "published")
            .OrderByDescending(a => a.UpdatedAt)
            .Take(5)
            .Select(a => new { a.Id, a.Title, a.Slug, a.ContentType })
            .ToListAsync();

        var topSearches = await db.SearchQueries
            .Where(s => s.CreatedAt >= weekAgo)
            .GroupBy(s => s.Query)
            .Select(g => new { Query = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToListAsync();

        return Ok(new
        {
            totalArticles,
            viewsThisWeek,
            searchesToday,
            staleCount,
            recentArticles,
            topSearches = topSearches.Select(s => new { query = s.Query, count = s.Count })
        });
    }
}
