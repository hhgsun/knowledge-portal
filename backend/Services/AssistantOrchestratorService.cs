using System.Diagnostics;
using System.Security.Claims;
using KnowledgePortal.Api.Models;

namespace KnowledgePortal.Api.Services;

/// <summary>
/// Bounded read-only orchestration. It composes existing search/RAG/analytics services and owns no
/// domain behavior, so deleting the assistant leaves those services and their public APIs intact.
/// </summary>
public sealed class AssistantOrchestratorService(
    AssistantRouterService router,
    AssistantPolicyService policy,
    SearchExecutionService search,
    AnalyticsReportService analytics,
    IConfiguration config,
    PortalMetrics metrics,
    ILogger<AssistantOrchestratorService> logger)
{
    public async Task<(AssistantResponseDto? Response, ServiceError? Error)> ExecuteAsync(
        AssistantRequest request, ClaimsPrincipal principal, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return (null, new(400, "Message is required."));
        var maxLength = Math.Clamp(config.GetValue("Assistant:MaxMessageCharacters", 4000), 100, 20_000);
        if (request.Message.Length > maxLength)
            return (null, new(400, $"Message cannot exceed {maxLength} characters."));

        var watch = Stopwatch.StartNew();
        var traceId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        var totalTimeout = Math.Clamp(config.GetValue("AgenticRouting:TotalTimeoutSeconds", 30), 5, 120);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(totalTimeout));
        AssistantRouteDecision? decision = null;
        try
        {
            decision = await router.RouteAsync(request.Message, request.PreferredRoute, budget.Token);
            var authorization = policy.Authorize(decision.Route, principal);
            if (!authorization.Allowed)
            {
                watch.Stop();
                metrics.AssistantDuration.Record(watch.Elapsed.TotalMilliseconds,
                    new("assistant.route", AssistantRouterService.RouteName(decision.Route)),
                    new("assistant.outcome", "denied"));
                return (null, new(403, authorization.Error!));
            }

            var response = decision.Route switch
            {
                AssistantRoute.KnowledgeSearch => await SearchAsync(decision, principal, budget.Token),
                AssistantRoute.KnowledgeAnswer => await AnswerAsync(decision, principal, budget.Token),
                AssistantRoute.Analytics => await AnalyticsAsync(decision, budget.Token),
                AssistantRoute.GeneralChat => GeneralChat(decision),
                _ => Clarify(decision)
            };
            watch.Stop();
            response = response with { ResponseTimeMs = watch.ElapsedMilliseconds, TraceId = traceId };
            metrics.AssistantDuration.Record(watch.Elapsed.TotalMilliseconds,
                new("assistant.route", response.Route), new("assistant.outcome", "success"));
            return (response, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            watch.Stop();
            metrics.AssistantDuration.Record(watch.Elapsed.TotalMilliseconds,
                new("assistant.route", decision == null ? "unknown" : AssistantRouterService.RouteName(decision.Route)),
                new("assistant.outcome", "timeout"));
            return (null, new(504, "Assistant request exceeded its processing deadline."));
        }
        catch (AssistantExecutionException ex)
        {
            watch.Stop();
            logger.LogWarning("Assistant tool failed for trace {TraceId} with status {StatusCode}",
                traceId, ex.Error.StatusCode);
            metrics.AssistantDuration.Record(watch.Elapsed.TotalMilliseconds,
                new("assistant.route", decision == null ? "unknown" : AssistantRouterService.RouteName(decision.Route)),
                new("assistant.outcome", "tool_failure"));
            return (null, ex.Error);
        }
        catch (Exception ex)
        {
            watch.Stop();
            logger.LogError(ex, "Assistant orchestration failed for trace {TraceId}", traceId);
            metrics.AssistantDuration.Record(watch.Elapsed.TotalMilliseconds,
                new("assistant.route", decision == null ? "unknown" : AssistantRouterService.RouteName(decision.Route)),
                new("assistant.outcome", "failure"));
            return (null, new(500, "Assistant request failed."));
        }
    }

    private async Task<AssistantResponseDto> SearchAsync(AssistantRouteDecision decision,
        ClaimsPrincipal principal, CancellationToken ct)
    {
        var result = await ExecuteSearchAsync(decision.NormalizedQuery, "hybrid", principal, ct);
        if (result.Error != null) throw new AssistantExecutionException(result.Error);
        var value = result.Result!;
        var answer = value.Results.Count == 0
            ? "Bu sorguyla eşleşen bir portal kaynağı bulamadım."
            : $"{value.Results.Count} ilgili portal kaynağı buldum.";
        return Base(decision) with
        {
            Answer = answer,
            Results = value.Results,
            ToolCalls = ["knowledge_search"],
            Warnings = CompactWarnings(value.Warning)
        };
    }

    private async Task<AssistantResponseDto> AnswerAsync(AssistantRouteDecision decision,
        ClaimsPrincipal principal, CancellationToken ct)
    {
        var execution = await ExecuteSearchAsync(decision.NormalizedQuery, "rag", principal, ct);
        if (execution.Error != null) throw new AssistantExecutionException(execution.Error);
        var result = execution.Result!;
        if (result.Failure != SearchFailureKind.None || result.Rag == null)
        {
            var fallback = await ExecuteSearchAsync(decision.NormalizedQuery, "hybrid", principal, ct);
            if (fallback.Error != null) throw new AssistantExecutionException(fallback.Error);
            var warning = result.Warning ?? "Kaynaklı AI yanıtı üretilemedi; güvenli doküman aramasına dönüldü.";
            return Base(decision, AssistantRoute.KnowledgeSearch, "rag_failure_safe_fallback") with
            {
                Answer = fallback.Result!.Results.Count == 0
                    ? "Kaynaklı yanıt üretilemedi ve eşleşen bir portal kaynağı bulunamadı."
                    : $"Kaynaklı yanıt üretilemedi; bunun yerine {fallback.Result.Results.Count} ilgili kaynak gösteriyorum.",
                Results = fallback.Result.Results,
                ToolCalls = ["knowledge_rag", "knowledge_search"],
                Warnings = CompactWarnings(warning, fallback.Result.Warning)
            };
        }

        var results = new List<ArticleSummaryDto>();
        var tools = new List<string> { "knowledge_rag" };
        var warnings = result.Rag.Warnings.ToList();
        if (decision.IncludeSearchResults
            && Math.Clamp(config.GetValue("AgenticRouting:MaxToolCalls", 4), 1, 8) >= 2)
        {
            var related = await ExecuteSearchAsync(decision.NormalizedQuery, "hybrid", principal, ct);
            if (related.Result != null)
            {
                results = related.Result.Results;
                tools.Add("knowledge_search");
                if (!string.IsNullOrWhiteSpace(related.Result.Warning)) warnings.Add(related.Result.Warning);
            }
        }

        return Base(decision) with
        {
            Answer = result.Rag.Answer,
            Results = results,
            Rag = ToDto(result.Rag),
            ToolCalls = tools.ToArray(),
            Warnings = warnings.Distinct().ToArray()
        };
    }

    private async Task<AssistantResponseDto> AnalyticsAsync(AssistantRouteDecision decision,
        CancellationToken ct)
    {
        var days = Math.Clamp(config.GetValue("AgenticRouting:AnalyticsDays", 30), 1, 365);
        AnalyticsReport report;
        try
        {
            report = await analytics.GetAsync(days, ct);
            RecordTool("portal_analytics", "success");
        }
        catch
        {
            RecordTool("portal_analytics", "failure");
            throw;
        }
        var dto = new AssistantAnalyticsDto(
            new(report.Overview.TotalArticles, report.Overview.ViewsThisWeek,
                report.Overview.SearchesToday, report.Overview.StaleArticles),
            report.TopSearches.Select(item => new AssistantQueryCountDto(item.Query, item.Count)).ToArray(),
            report.FailedSearches.Select(item => new AssistantQueryCountDto(item.Query, item.Count)).ToArray(),
            report.TopArticles.Select(item => new AssistantTopArticleDto(item.ArticleId,
                item.Title, item.Slug, item.Views)).ToArray(), days);
        return Base(decision) with
        {
            Answer = AnalyticsAnswer(decision.NormalizedQuery, dto),
            Analytics = dto,
            ToolCalls = ["portal_analytics"]
        };
    }

    private static AssistantResponseDto GeneralChat(AssistantRouteDecision decision)
    {
        var folded = Helpers.SlugHelper.Transliterate(decision.NormalizedQuery).ToLowerInvariant();
        var english = folded is "hello" or "hi" or "hey" or "thanks" or "thank you";
        return Base(decision) with
        {
            Answer = english
                ? "Hello! I can search the internal portal, answer with cited sources, or show authorized portal analytics."
                : "Merhaba! Şirket içi portalda kaynak arayabilir, kanıtlı yanıt üretebilir veya yetkiniz varsa portal analitiklerini gösterebilirim."
        };
    }

    private static AssistantResponseDto Clarify(AssistantRouteDecision decision) => Base(decision) with
    {
        RequiresClarification = true,
        Clarification = "Doküman aramamı mı, portal kaynaklarına dayanarak yanıt vermemi mi, yoksa analitikleri göstermemi mi istiyorsunuz?"
    };

    private async Task<(PortalSearchResult? Result, ServiceError? Error)> ExecuteSearchAsync(string query,
        string type, ClaimsPrincipal principal, CancellationToken ct)
    {
        var tool = type == "rag" ? "knowledge_rag" : "knowledge_search";
        try
        {
            var execution = await search.ExecuteAsync(
                new(query, type, Limit: Math.Clamp(config.GetValue("AgenticRouting:SearchLimit", 8), 1, 20)),
                principal, ct);
            var outcome = execution.Error != null
                ? "failure"
                : execution.Result?.Failure == SearchFailureKind.None ? "success" : "degraded";
            RecordTool(tool, outcome);
            return execution;
        }
        catch
        {
            RecordTool(tool, "failure");
            throw;
        }
    }

    private void RecordTool(string tool, string outcome) => metrics.AssistantToolCalls.Add(1,
        new("assistant.tool", tool), new("assistant.outcome", outcome));

    private static AssistantResponseDto Base(AssistantRouteDecision decision,
        AssistantRoute? actualRoute = null, string? reason = null) => new(
        AssistantRouterService.RouteName(actualRoute ?? decision.Route), decision.Confidence,
        decision.Source, reason ?? decision.ReasonCode, decision.NormalizedQuery, null, [], null, null,
        false, null, [], [], 0, "");

    private static AssistantRagDto ToDto(RagService.RagResult rag) => new(
        rag.Sources.Select(ToSource).ToArray(), rag.ConsultedSources.Select(ToSource).ToArray(),
        rag.Claims.Select(claim => new AssistantClaimDto(claim.Text, claim.Role,
            claim.SourceIds.ToArray())).ToArray(),
        rag.Evidence.Select(item => new AssistantEvidenceDto(item.SourceId, item.ArticleId,
            item.Title, item.Slug, item.SourceType, item.AttachmentId, item.SourceName,
            item.SourceLocation, item.Passage, item.Score, item.ChunkId, item.CanonicalUrl,
            item.PageNumber)).ToArray(), rag.CitationCoverage, rag.ClaimSupportCoverage,
        rag.GroundingStatus, rag.InsufficientContext, rag.PartialResult);

    private static AssistantSourceDto ToSource(RagService.RagSource source) => new(source.ArticleId,
        source.Title, source.Slug, source.Score, source.AuthorityWeight, source.Approved,
        source.ReviewState, source.ReliabilityScore, source.UpdatedAt);

    private static string AnalyticsAnswer(string query, AssistantAnalyticsDto analytics)
    {
        var folded = Helpers.SlugHelper.Transliterate(query).ToLowerInvariant();
        if (folded.Contains("okunan") || folded.Contains("goruntulen"))
            return analytics.TopArticles.Length == 0
                ? "Son yedi günde görüntülenmiş bir makale bulunmuyor."
                : "Son yedi günde en çok okunan makaleler: " + string.Join(", ",
                    analytics.TopArticles.Take(5).Select(item => $"{item.Title} ({item.Views})")) + ".";
        if (folded.Contains("aranan") || folded.Contains("arama"))
            return analytics.TopSearches.Length == 0
                ? "Son yedi güne ait arama verisi bulunmuyor."
                : "Son yedi günün en sık aranan sorguları: " + string.Join(", ",
                    analytics.TopSearches.Take(5).Select(item => $"{item.Query} ({item.Count})")) + ".";
        return $"Portalda {analytics.Overview.TotalArticles} makale var; son yedi günde " +
               $"{analytics.Overview.ViewsThisWeek} görüntülenme ve bugün " +
               $"{analytics.Overview.SearchesToday} arama kaydedildi.";
    }

    private static string[] CompactWarnings(params string?[] warnings) => warnings
        .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).Distinct().ToArray();

    private sealed class AssistantExecutionException(ServiceError error) : Exception(error.Message)
    {
        public ServiceError Error { get; } = error;
    }
}
