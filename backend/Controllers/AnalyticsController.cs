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
public class AnalyticsController(AppDbContext db, StatsService statsService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 30)
    {
        days = Math.Clamp(days, 1, 365);
        var usageSince = DateTime.UtcNow.AddDays(-days);
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

        var usageBase = db.UsageEvents.AsNoTracking().Where(e => e.OccurredAt >= usageSince);
        var usageTotal = await usageBase.CountAsync();
        var usageErrors = await usageBase.CountAsync(e => e.Outcome != "success");
        var averageDurationMs = usageTotal == 0 ? 0 : await usageBase.AverageAsync(e => (double)e.DurationMs);

        var userUsageRows = await usageBase
            .Where(e => e.UserId != null)
            .GroupBy(e => e.UserId!)
            .Select(g => new { UserId = g.Key, Requests = g.Count(), Errors = g.Count(e => e.Outcome != "success"), AverageDurationMs = g.Average(e => (double)e.DurationMs), LastUsedAt = g.Max(e => e.OccurredAt) })
            .OrderByDescending(x => x.Requests).Take(20).ToListAsync();
        var userIds = userUsageRows.Select(x => x.UserId).ToList();
        var usageUsers = await db.Users.Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.Name, u.Email }).ToDictionaryAsync(u => u.Id);

        var integrationUsageRows = await usageBase
            .Where(e => e.ApiKeyId != null)
            .GroupBy(e => e.ApiKeyId!)
            .Select(g => new { ApiKeyId = g.Key, Requests = g.Count(), McpCalls = g.Count(e => e.Channel == "mcp"), Errors = g.Count(e => e.Outcome != "success"), AverageDurationMs = g.Average(e => (double)e.DurationMs), LastUsedAt = g.Max(e => e.OccurredAt) })
            .OrderByDescending(x => x.Requests).Take(20).ToListAsync();
        var apiKeyIds = integrationUsageRows.Select(x => x.ApiKeyId).ToList();
        var usageKeys = await db.ApiKeys.Where(k => apiKeyIds.Contains(k.Id)).Select(k => new { k.Id, k.Name, OwnerName = k.User.Name }).ToDictionaryAsync(k => k.Id);

        var operations = await usageBase.GroupBy(e => new { e.Operation, e.Channel })
            .Select(g => new { g.Key.Operation, g.Key.Channel, Requests = g.Count(), Errors = g.Count(e => e.Outcome != "success"), AverageDurationMs = g.Average(e => (double)e.DurationMs) })
            .OrderByDescending(x => x.Requests).Take(20).ToListAsync();

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
            topArticles = enrichedTopArticles.Select(a => new { articleId = a.ArticleId, title = a.Title, slug = a.Slug, views = a.Views })
            ,usage = new
            {
                periodDays = days,
                totalRequests = usageTotal,
                errors = usageErrors,
                errorRate = usageTotal == 0 ? 0 : (double)usageErrors / usageTotal,
                averageDurationMs,
                users = userUsageRows.Select(x => new { x.UserId, name = usageUsers.TryGetValue(x.UserId, out var u) ? u.Name : "Deleted user", email = usageUsers.TryGetValue(x.UserId, out var ue) ? ue.Email : "", x.Requests, x.Errors, x.AverageDurationMs, x.LastUsedAt }),
                integrations = integrationUsageRows.Select(x => new { x.ApiKeyId, name = usageKeys.TryGetValue(x.ApiKeyId, out var k) ? k.Name : "Deleted integration", ownerName = usageKeys.TryGetValue(x.ApiKeyId, out var ko) ? ko.OwnerName : "", x.Requests, x.McpCalls, x.Errors, x.AverageDurationMs, x.LastUsedAt }),
                operations
            }
        });
    }
}
