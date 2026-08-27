using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public sealed record AnalyticsOverviewReport(int TotalArticles, Dictionary<string, int> ArticlesByStatus,
    int ViewsThisWeek, int SearchesToday, int StaleArticles);
public sealed record AnalyticsTopArticle(string ArticleId, string Title, string Slug, int Views);
public sealed record AnalyticsReport(AnalyticsOverviewReport Overview,
    List<StatsService.QueryCount> TopSearches,
    List<StatsService.QueryCount> FailedSearches,
    List<AnalyticsTopArticle> TopArticles,
    UsageAnalytics Usage);

/// <summary>
/// Shared analytics read model for the REST analytics page and the read-only assistant route.
/// Keeping it outside both controllers prevents assistant removal from affecting analytics logic.
/// </summary>
public sealed class AnalyticsReportService(
    AppDbContext db,
    StatsService statsService,
    UsageAnalyticsService usageAnalyticsService)
{
    public async Task<AnalyticsReport> GetAsync(int days, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 365);
        var overview = await statsService.GetOverviewAsync();
        var articlesByStatus = await db.Articles
            .GroupBy(article => article.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count, ct);

        var topSearches = await statsService.GetTopSearchesAsync(10);
        var failedSearches = await statsService.GetFailedSearchesAsync(10);
        var weekAgo = DateTime.UtcNow.AddDays(-7);
        var viewRows = await db.ArticleViews
            .Where(view => view.CreatedAt >= weekAgo)
            .GroupBy(view => view.ArticleId)
            .Select(group => new { ArticleId = group.Key, Views = group.Count() })
            .OrderByDescending(item => item.Views)
            .Take(10)
            .ToListAsync(ct);
        var articleIds = viewRows.Select(item => item.ArticleId).ToList();
        var articles = await db.Articles.Where(article => articleIds.Contains(article.Id))
            .Select(article => new { article.Id, article.Title, article.Slug })
            .ToDictionaryAsync(article => article.Id, ct);
        var topArticles = viewRows.Select(item => new AnalyticsTopArticle(item.ArticleId,
            articles.GetValueOrDefault(item.ArticleId)?.Title ?? "Unknown",
            articles.GetValueOrDefault(item.ArticleId)?.Slug ?? "", item.Views)).ToList();
        var usage = await usageAnalyticsService.GetAsync(days);

        return new(new(overview.TotalArticles, articlesByStatus, overview.ViewsThisWeek,
                overview.SearchesToday, overview.StaleArticles),
            topSearches, failedSearches, topArticles, usage);
    }
}
