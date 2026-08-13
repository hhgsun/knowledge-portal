using System.Diagnostics;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Routing;

namespace KnowledgePortal.Api.Middleware;

/// <summary>Persists one privacy-safe usage event for every authenticated API/MCP request.</summary>
public sealed class UsageTrackingMiddleware(RequestDelegate next, PortalMetrics metrics, ILogger<UsageTrackingMiddleware> logger)
{
    public const string OperationItem = "Usage.Operation";
    public const string OutcomeItem = "Usage.Outcome";

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        var started = Stopwatch.GetTimestamp();
        try { await next(context); }
        finally
        {
            if (context.User.Identity?.IsAuthenticated == true &&
                (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/mcp")))
                await TrackAsync(context, db, started);
        }
    }

    private async Task TrackAsync(HttpContext context, AppDbContext db, long started)
    {
            var route = context.GetEndpoint()?.Metadata.GetMetadata<RouteEndpoint>()?.RoutePattern.RawText
                ?? context.Request.Path.Value ?? "unknown";
            var channel = context.Request.Path.StartsWithSegments("/mcp") ? "mcp" : "rest";
            var operation = context.Items[OperationItem] as string ?? $"{context.Request.Method} /{route.TrimStart('/')}";
            var status = context.Response.StatusCode;
            var outcome = context.Items[OutcomeItem] as string
                ?? (status >= 500 ? "server_error" : status >= 400 ? "client_error" : "success");
            var durationMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            db.UsageEvents.Add(new UsageEvent
            {
                UserId = EmptyToNull(context.User.GetUserId()), ApiKeyId = context.User.GetApiKeyId(),
                AuthSource = context.User.GetSource(), Channel = channel,
                Operation = operation[..Math.Min(operation.Length, 200)], HttpMethod = context.Request.Method,
                Outcome = outcome, StatusCode = status, DurationMs = durationMs
            });
            try
            {
                await db.SaveChangesAsync(CancellationToken.None);
                metrics.UsageRequests.Add(1, new("usage.channel", channel), new("usage.outcome", outcome));
                metrics.UsageDuration.Record(durationMs, new("usage.channel", channel), new("usage.outcome", outcome));
            }
            catch (Exception ex)
            {
                metrics.UsageTrackingFailures.Add(1);
                logger.LogWarning(ex, "Usage event could not be persisted for {Operation}", operation);
            }
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
