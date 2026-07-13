using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

/// <summary>Shared usage metrics consumed by the dashboard and analytics endpoints.</summary>
public class StatsService(AppDbContext db)
{
    public record Overview(int TotalArticles, int ViewsThisWeek, int SearchesToday, int StaleArticles);

    public record QueryCount(string Query, int Count);

    public async Task<Overview> GetOverviewAsync()
    {
        var now = DateTime.UtcNow;
        var weekAgo = now.AddDays(-7);
        var dayAgo = now.AddDays(-1);
        var staleThreshold = now.AddDays(-90);

        return new Overview(
            await db.Articles.CountAsync(),
            await db.ArticleViews.CountAsync(v => v.CreatedAt >= weekAgo),
            await db.SearchQueries.CountAsync(s => s.CreatedAt >= dayAgo),
            await db.Articles.CountAsync(a =>
                a.Status == "published" && a.LastReviewedAt != null && a.LastReviewedAt < staleThreshold));
    }

    /// <summary>Most frequent search queries of the last 7 days.</summary>
    public async Task<List<QueryCount>> GetTopSearchesAsync(int take)
    {
        var weekAgo = DateTime.UtcNow.AddDays(-7);
        var rows = await db.SearchQueries
            .Where(s => s.CreatedAt >= weekAgo)
            .GroupBy(s => s.Query)
            .Select(g => new { Query = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(take)
            .ToListAsync();
        return rows.Select(r => new QueryCount(r.Query, r.Count)).ToList();
    }

    /// <summary>Most frequent queries that returned zero results (all time).</summary>
    public async Task<List<QueryCount>> GetFailedSearchesAsync(int take)
    {
        var rows = await db.SearchQueries
            .Where(s => s.ResultsCount == 0)
            .GroupBy(s => s.Query)
            .Select(g => new { Query = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(take)
            .ToListAsync();
        return rows.Select(r => new QueryCount(r.Query, r.Count)).ToList();
    }
}
