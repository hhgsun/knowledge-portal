using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public sealed record UsageAnalytics(
    int PeriodDays,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    int TotalRequests,
    int SuccessfulRequests,
    int Errors,
    double ErrorRate,
    double AverageDurationMs,
    int ActiveUsers,
    int ActiveIntegrations,
    int SessionRequests,
    int IntegrationRequests,
    int RestRequests,
    int McpCalls,
    IReadOnlyList<DailyUsageMetric> Daily,
    IReadOnlyList<UserUsageMetric> Users,
    IReadOnlyList<IntegrationUsageMetric> Integrations,
    IReadOnlyList<OperationUsageMetric> Operations);

public sealed record DailyUsageMetric(
    string Date,
    int Requests,
    int Errors,
    double AverageDurationMs,
    int ActiveUsers,
    int ActiveIntegrations,
    int SessionRequests,
    int IntegrationRequests,
    int RestRequests,
    int McpCalls);

public sealed record UserUsageMetric(
    string UserId,
    string Name,
    string Email,
    string Role,
    int Requests,
    int SessionRequests,
    int IntegrationRequests,
    int RestRequests,
    int McpCalls,
    int ReadRequests,
    int WriteRequests,
    int Errors,
    double ErrorRate,
    double AverageDurationMs,
    DateTime LastUsedAt,
    int ActiveDays,
    int IntegrationsUsed,
    string? TopOperation,
    int TopOperationRequests);

public sealed record IntegrationUsageMetric(
    string ApiKeyId,
    string Name,
    string OwnerId,
    string OwnerName,
    string OwnerEmail,
    int Requests,
    int RestRequests,
    int McpCalls,
    int ReadRequests,
    int WriteRequests,
    int Errors,
    double ErrorRate,
    double AverageDurationMs,
    DateTime LastUsedAt,
    int ActiveDays,
    string? TopOperation,
    int TopOperationRequests);

public sealed record OperationUsageMetric(
    string Operation,
    string Channel,
    int Requests,
    int Errors,
    double ErrorRate,
    double AverageDurationMs,
    DateTime LastUsedAt,
    int UniqueUsers,
    int UniqueIntegrations);

/// <summary>
/// Produces privacy-safe product usage aggregates from persisted authenticated request events.
/// No request bodies, credentials, client IPs or query-string values are collected or returned.
/// </summary>
public sealed class UsageAnalyticsService(AppDbContext db)
{
    public async Task<UsageAnalytics> GetAsync(int days)
    {
        days = Math.Clamp(days, 1, 365);
        var periodEnd = DateTime.UtcNow;
        var periodStart = periodEnd.Date.AddDays(1 - days);
        var usage = db.UsageEvents.AsNoTracking().Where(e => e.OccurredAt >= periodStart && e.OccurredAt <= periodEnd);

        var summary = await usage
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Successful = g.Count(e => e.Outcome == "success"),
                Errors = g.Count(e => e.Outcome != "success"),
                AverageDurationMs = g.Average(e => (double)e.DurationMs),
                ActiveUsers = g.Where(e => e.UserId != null).Select(e => e.UserId).Distinct().Count(),
                ActiveIntegrations = g.Where(e => e.ApiKeyId != null).Select(e => e.ApiKeyId).Distinct().Count(),
                SessionRequests = g.Count(e => e.ApiKeyId == null),
                IntegrationRequests = g.Count(e => e.ApiKeyId != null),
                RestRequests = g.Count(e => e.Channel == "rest"),
                McpCalls = g.Count(e => e.Channel == "mcp")
            })
            .SingleOrDefaultAsync();

        var dailyRows = await usage
            .GroupBy(e => e.OccurredAt.Date)
            .Select(g => new
            {
                Date = g.Key,
                Requests = g.Count(),
                Errors = g.Count(e => e.Outcome != "success"),
                AverageDurationMs = g.Average(e => (double)e.DurationMs),
                ActiveUsers = g.Where(e => e.UserId != null).Select(e => e.UserId).Distinct().Count(),
                ActiveIntegrations = g.Where(e => e.ApiKeyId != null).Select(e => e.ApiKeyId).Distinct().Count(),
                SessionRequests = g.Count(e => e.ApiKeyId == null),
                IntegrationRequests = g.Count(e => e.ApiKeyId != null),
                RestRequests = g.Count(e => e.Channel == "rest"),
                McpCalls = g.Count(e => e.Channel == "mcp")
            })
            .OrderBy(x => x.Date)
            .ToListAsync();

        var userRows = await usage
            .Where(e => e.UserId != null)
            .GroupBy(e => e.UserId!)
            .Select(g => new
            {
                UserId = g.Key,
                Requests = g.Count(),
                SessionRequests = g.Count(e => e.ApiKeyId == null),
                IntegrationRequests = g.Count(e => e.ApiKeyId != null),
                RestRequests = g.Count(e => e.Channel == "rest"),
                McpCalls = g.Count(e => e.Channel == "mcp"),
                WriteRequests = g.Count(e => e.Channel == "rest" && e.HttpMethod != "GET" && e.HttpMethod != "HEAD" && e.HttpMethod != "OPTIONS"),
                Errors = g.Count(e => e.Outcome != "success"),
                AverageDurationMs = g.Average(e => (double)e.DurationMs),
                LastUsedAt = g.Max(e => e.OccurredAt),
                ActiveDays = g.Select(e => e.OccurredAt.Date).Distinct().Count(),
                IntegrationsUsed = g.Where(e => e.ApiKeyId != null).Select(e => e.ApiKeyId).Distinct().Count()
            })
            .OrderByDescending(x => x.Requests)
            .ToListAsync();

        var integrationRows = await usage
            .Where(e => e.ApiKeyId != null)
            .GroupBy(e => e.ApiKeyId!)
            .Select(g => new
            {
                ApiKeyId = g.Key,
                Requests = g.Count(),
                RestRequests = g.Count(e => e.Channel == "rest"),
                McpCalls = g.Count(e => e.Channel == "mcp"),
                WriteRequests = g.Count(e => e.Channel == "rest" && e.HttpMethod != "GET" && e.HttpMethod != "HEAD" && e.HttpMethod != "OPTIONS"),
                Errors = g.Count(e => e.Outcome != "success"),
                AverageDurationMs = g.Average(e => (double)e.DurationMs),
                LastUsedAt = g.Max(e => e.OccurredAt),
                ActiveDays = g.Select(e => e.OccurredAt.Date).Distinct().Count()
            })
            .OrderByDescending(x => x.Requests)
            .ToListAsync();

        var operationRows = await usage
            .GroupBy(e => new { e.Operation, e.Channel })
            .Select(g => new
            {
                g.Key.Operation,
                g.Key.Channel,
                Requests = g.Count(),
                Errors = g.Count(e => e.Outcome != "success"),
                AverageDurationMs = g.Average(e => (double)e.DurationMs),
                LastUsedAt = g.Max(e => e.OccurredAt),
                UniqueUsers = g.Where(e => e.UserId != null).Select(e => e.UserId).Distinct().Count(),
                UniqueIntegrations = g.Where(e => e.ApiKeyId != null).Select(e => e.ApiKeyId).Distinct().Count()
            })
            .OrderByDescending(x => x.Requests)
            .Take(50)
            .ToListAsync();

        var userOperationRows = await usage
            .Where(e => e.UserId != null)
            .GroupBy(e => new { UserId = e.UserId!, e.Operation })
            .Select(g => new { g.Key.UserId, g.Key.Operation, Requests = g.Count() })
            .ToListAsync();
        var integrationOperationRows = await usage
            .Where(e => e.ApiKeyId != null)
            .GroupBy(e => new { ApiKeyId = e.ApiKeyId!, e.Operation })
            .Select(g => new { g.Key.ApiKeyId, g.Key.Operation, Requests = g.Count() })
            .ToListAsync();

        var topUserOperations = userOperationRows.GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Requests).ThenBy(x => x.Operation).First());
        var topIntegrationOperations = integrationOperationRows.GroupBy(x => x.ApiKeyId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Requests).ThenBy(x => x.Operation).First());

        var userIds = userRows.Select(x => x.UserId).ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Name, u.Email, u.Role })
            .ToDictionaryAsync(u => u.Id);
        var apiKeyIds = integrationRows.Select(x => x.ApiKeyId).ToList();
        var apiKeys = await db.ApiKeys.AsNoTracking().Where(k => apiKeyIds.Contains(k.Id))
            .Select(k => new { k.Id, k.Name, OwnerId = k.UserId, OwnerName = k.User.Name, OwnerEmail = k.User.Email })
            .ToDictionaryAsync(k => k.Id);

        var dailyByDate = dailyRows.ToDictionary(x => x.Date.Date);
        var daily = Enumerable.Range(0, days).Select(offset =>
        {
            var date = periodStart.Date.AddDays(offset);
            if (!dailyByDate.TryGetValue(date, out var row))
                return new DailyUsageMetric(date.ToString("yyyy-MM-dd"), 0, 0, 0, 0, 0, 0, 0, 0, 0);
            return new DailyUsageMetric(date.ToString("yyyy-MM-dd"), row.Requests, row.Errors,
                row.AverageDurationMs, row.ActiveUsers, row.ActiveIntegrations, row.SessionRequests,
                row.IntegrationRequests, row.RestRequests, row.McpCalls);
        }).ToList();

        var total = summary?.Total ?? 0;
        return new UsageAnalytics(
            days,
            periodStart,
            periodEnd,
            total,
            summary?.Successful ?? 0,
            summary?.Errors ?? 0,
            Rate(summary?.Errors ?? 0, total),
            summary?.AverageDurationMs ?? 0,
            summary?.ActiveUsers ?? 0,
            summary?.ActiveIntegrations ?? 0,
            summary?.SessionRequests ?? 0,
            summary?.IntegrationRequests ?? 0,
            summary?.RestRequests ?? 0,
            summary?.McpCalls ?? 0,
            daily,
            userRows.Select(x =>
            {
                users.TryGetValue(x.UserId, out var user);
                topUserOperations.TryGetValue(x.UserId, out var top);
                return new UserUsageMetric(x.UserId, user?.Name ?? "Silinmiş kullanıcı", user?.Email ?? "",
                    user?.Role ?? "unknown", x.Requests, x.SessionRequests, x.IntegrationRequests,
                    x.RestRequests, x.McpCalls, x.Requests - x.WriteRequests, x.WriteRequests, x.Errors,
                    Rate(x.Errors, x.Requests), x.AverageDurationMs, x.LastUsedAt, x.ActiveDays,
                    x.IntegrationsUsed, top?.Operation, top?.Requests ?? 0);
            }).ToList(),
            integrationRows.Select(x =>
            {
                apiKeys.TryGetValue(x.ApiKeyId, out var key);
                topIntegrationOperations.TryGetValue(x.ApiKeyId, out var top);
                return new IntegrationUsageMetric(x.ApiKeyId, key?.Name ?? "Silinmiş entegrasyon",
                    key?.OwnerId ?? "", key?.OwnerName ?? "", key?.OwnerEmail ?? "", x.Requests,
                    x.RestRequests, x.McpCalls, x.Requests - x.WriteRequests, x.WriteRequests, x.Errors,
                    Rate(x.Errors, x.Requests), x.AverageDurationMs, x.LastUsedAt, x.ActiveDays,
                    top?.Operation, top?.Requests ?? 0);
            }).ToList(),
            operationRows.Select(x => new OperationUsageMetric(x.Operation, x.Channel, x.Requests, x.Errors,
                Rate(x.Errors, x.Requests), x.AverageDurationMs, x.LastUsedAt,
                x.UniqueUsers, x.UniqueIntegrations)).ToList());
    }

    private static double Rate(int value, int total) => total == 0 ? 0 : (double)value / total;
}
