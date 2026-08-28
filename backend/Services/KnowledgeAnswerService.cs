using System.Diagnostics;
using System.Security.Claims;

namespace KnowledgePortal.Api.Services;

public sealed record KnowledgeAnswerRequest(
    string Question,
    bool OnlyOwnContent = false,
    IEnumerable<string>? Tags = null,
    IEnumerable<string>? Authors = null,
    IEnumerable<string>? ContentTypes = null,
    string? HypotheticalDocument = null);

public enum KnowledgeAnswerFailureKind
{
    None,
    Unavailable,
    Busy,
    CircuitOpen,
    Timeout,
    Failed
}

public sealed record KnowledgeAnswerResult(
    string Question,
    RagService.RagResult? Rag,
    SearchIndexCoverage? IndexCoverage,
    long ResponseTimeMs,
    string? TraceId,
    KnowledgeAnswerFailureKind Failure = KnowledgeAnswerFailureKind.None,
    string? Warning = null)
{
    public bool IndexingPending => IndexCoverage?.RelevantPending > 0;
}

/// <summary>
/// Canonical grounded-answer pipeline for Assistant and MCP. Unlike document search,
/// this service invokes the language model and never writes to search analytics.
/// </summary>
public sealed class KnowledgeAnswerService(
    IConfiguration config,
    ArticleService articles,
    KnowledgeQueryScopeService scopeResolver,
    IServiceProvider services,
    ILogger<KnowledgeAnswerService> logger)
{
    public async Task<(KnowledgeAnswerResult? Result, ServiceError? Error)> ExecuteAsync(
        KnowledgeAnswerRequest request,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return (null, new ServiceError(400, "Question is required"));

        var stopwatch = Stopwatch.StartNew();
        var scope = await scopeResolver.ResolveAsync(new KnowledgeQueryScopeRequest(
            request.Question,
            request.OnlyOwnContent,
            request.Tags,
            request.Authors,
            request.ContentTypes), principal, cancellationToken);

        if (scope.HasUnknownTags)
            return (null, new ServiceError(404, "No knowledge sources matched the requested scope"));
        if (string.IsNullOrWhiteSpace(scope.QueryText))
            return (null, new ServiceError(400, "A question is required in addition to scope filters"));

        var coverage = await articles.GetSearchIndexCoverageAsync("rag", scope.Filter, cancellationToken);
        var enabled = config.GetValue("Assistant:Enabled", false) && config.GetValue("Ollama:Enabled", false);
        if (!enabled || services.GetService<RagService>() is not { } ragService)
        {
            stopwatch.Stop();
            return (new KnowledgeAnswerResult(request.Question, null, coverage,
                stopwatch.ElapsedMilliseconds, Activity.Current?.TraceId.ToString(),
                KnowledgeAnswerFailureKind.Unavailable,
                "Knowledge Assistant is unavailable because its AI runtime is disabled."), null);
        }

        try
        {
            var rag = await ragService.AskAsync(scope.QueryText, scope.Filter, cancellationToken,
                request.HypotheticalDocument);
            stopwatch.Stop();
            return (new KnowledgeAnswerResult(request.Question, rag, coverage,
                stopwatch.ElapsedMilliseconds, Activity.Current?.TraceId.ToString()), null);
        }
        catch (RagBusyException)
        {
            stopwatch.Stop();
            return (new KnowledgeAnswerResult(request.Question, null, coverage,
                stopwatch.ElapsedMilliseconds, Activity.Current?.TraceId.ToString(),
                KnowledgeAnswerFailureKind.Busy), null);
        }
        catch (RagCircuitOpenException)
        {
            stopwatch.Stop();
            return (new KnowledgeAnswerResult(request.Question, null, coverage,
                stopwatch.ElapsedMilliseconds, Activity.Current?.TraceId.ToString(),
                KnowledgeAnswerFailureKind.CircuitOpen), null);
        }
        catch (RagStageTimeoutException)
        {
            stopwatch.Stop();
            return (new KnowledgeAnswerResult(request.Question, null, coverage,
                stopwatch.ElapsedMilliseconds, Activity.Current?.TraceId.ToString(),
                KnowledgeAnswerFailureKind.Timeout), null);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            logger.LogError(exception, "Grounded knowledge answer failed");
            return (new KnowledgeAnswerResult(request.Question, null, coverage,
                stopwatch.ElapsedMilliseconds, Activity.Current?.TraceId.ToString(),
                KnowledgeAnswerFailureKind.Failed, "Grounded answer generation failed."), null);
        }
    }
}
