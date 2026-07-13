using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController(AppDbContext db, StatsService statsService, ArticleService articleService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var overview = await statsService.GetOverviewAsync();

        var (recentArticles, _) = await articleService.ListAsync(db.Articles.WherePublished(), 1, 5);

        var topSearches = await statsService.GetTopSearchesAsync(5);

        return Ok(new
        {
            totalArticles = overview.TotalArticles,
            viewsThisWeek = overview.ViewsThisWeek,
            searchesToday = overview.SearchesToday,
            staleCount = overview.StaleArticles,
            recentArticles,
            topSearches = topSearches.Select(s => new { query = s.Query, count = s.Count })
        });
    }
}
