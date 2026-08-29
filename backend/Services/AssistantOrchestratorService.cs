using System.Diagnostics;
using System.Security.Claims;
using KnowledgePortal.Api.Models;

namespace KnowledgePortal.Api.Services;

/// <summary>
/// Bounded, read-only grounded-answer orchestration. Assistant has one purpose:
/// synthesize an answer from authorized portal evidence through the RAG pipeline.
/// </summary>
public sealed class AssistantOrchestratorService(
    AssistantAnswerCacheService answerCache,
    KnowledgeAnswerService knowledgeAnswers,
    IConfiguration config,
    PortalMetrics metrics,
    ILogger<AssistantOrchestratorService> logger)
{
    public async Task<(AssistantResponseDto? Response, ServiceError? Error)> ExecuteAsync(
        AssistantRequest request, ClaimsPrincipal principal, CancellationToken cancellationToken = default,
        string? hypotheticalDocument = null, string contextualizationStrategy = "none")
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return (null, new ServiceError(400, "Message is required."));

        var maxLength = Math.Clamp(config.GetValue("Assistant:MaxMessageCharacters", 4000), 100, 20_000);
        if (request.Message.Length > maxLength)
            return (null, new ServiceError(400, $"Message cannot exceed {maxLength} characters."));

        var watch = Stopwatch.StartNew();
        var traceId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        var totalTimeout = Math.Clamp(config.GetValue("Assistant:TotalTimeoutSeconds", 120), 5, 300);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(totalTimeout));

        try
        {
            var normalizedQuestion = request.Message.Trim();
            var cacheQuestion = BuildCacheQuestion(request);
            CachedAssistantAnswer? cached = null;
            try
            {
                cached = await answerCache.TryGetAsync(cacheQuestion, principal, budget.Token);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Assistant answer cache lookup failed open");
                metrics.AssistantAnswerCache.Add(1,
                    new KeyValuePair<string, object?>("outcome", "failure"));
            }

            if (cached != null)
            {
                watch.Stop();
                metrics.AssistantDuration.Record(watch.Elapsed.TotalMilliseconds,
                    new("route", "knowledge_answer"), new("outcome", "success"));
                return (Base(normalizedQuestion, traceId, watch.ElapsedMilliseconds) with
                {
                    Answer = cached.Answer,
                    Rag = cached.Rag,
                    ToolCalls = ToolCalls("semantic_answer_cache", contextualizationStrategy),
                    CacheHit = true
                }, null);
            }

            var execution = await knowledgeAnswers.ExecuteAsync(new KnowledgeAnswerRequest(
                normalizedQuestion,
                request.OnlyOwnContent,
                request.Tags,
                request.Authors,
                request.ContentTypes,
                request.Facets,
                hypotheticalDocument), principal, budget.Token);
            if (execution.Error != null) return (null, execution.Error);

            var result = execution.Result!;
            if (result.Failure != KnowledgeAnswerFailureKind.None || result.Rag == null)
            {
                watch.Stop();
                metrics.AssistantToolCalls.Add(1, new("tool", "knowledge_rag"), new("outcome", "failure"));
                metrics.AssistantDuration.Record(watch.Elapsed.TotalMilliseconds,
                    new("route", "knowledge_answer"), new("outcome", "failure"));
                return (null, Failure(result.Failure));
            }

            var ragDto = ToDto(result.Rag);
            try
            {
                await answerCache.StoreAsync(cacheQuestion, principal,
                    new CachedAssistantAnswer(result.Rag.Answer, ragDto), budget.Token);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Assistant answer cache store failed open");
                metrics.AssistantAnswerCache.Add(1,
                    new KeyValuePair<string, object?>("outcome", "failure"));
            }

            watch.Stop();
            metrics.AssistantToolCalls.Add(1, new("tool", "knowledge_rag"), new("outcome", "success"));
            metrics.AssistantDuration.Record(watch.Elapsed.TotalMilliseconds,
                new("route", "knowledge_answer"), new("outcome", "success"));
            var warnings = result.Rag.Warnings.ToList();
            if (result.IndexingPending)
                warnings.Add("Some authorized sources are still being indexed and may not be represented yet.");
            return (Base(normalizedQuestion, traceId, watch.ElapsedMilliseconds) with
            {
                Answer = result.Rag.Answer,
                Rag = ragDto,
                ToolCalls = ToolCalls("knowledge_rag", contextualizationStrategy),
                Warnings = warnings.Distinct().ToArray()
            }, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            watch.Stop();
            metrics.AssistantDuration.Record(watch.Elapsed.TotalMilliseconds,
                new("route", "knowledge_answer"), new("outcome", "timeout"));
            return (null, new ServiceError(504, "Assistant request exceeded its processing deadline."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            watch.Stop();
            logger.LogError(exception, "Grounded Assistant request failed for trace {TraceId}", traceId);
            metrics.AssistantDuration.Record(watch.Elapsed.TotalMilliseconds,
                new("route", "knowledge_answer"), new("outcome", "failure"));
            return (null, new ServiceError(500, "Assistant request failed."));
        }
    }

    private static ServiceError Failure(KnowledgeAnswerFailureKind failure) => failure switch
    {
        KnowledgeAnswerFailureKind.Unavailable => new(503, "Knowledge Assistant is unavailable."),
        KnowledgeAnswerFailureKind.Busy => new(429, "Knowledge Assistant capacity is full. Please retry shortly."),
        KnowledgeAnswerFailureKind.CircuitOpen => new(503, "Knowledge Assistant is temporarily unavailable."),
        KnowledgeAnswerFailureKind.Timeout => new(504, "Grounded answer generation timed out."),
        _ => new(500, "Grounded answer generation failed.")
    };

    private static AssistantResponseDto Base(string question, string traceId, long responseTimeMs) => new(
        NormalizedQuery: question, Answer: null, Rag: null, ToolCalls: [], Warnings: [],
        InteractionId: null, ResponseTimeMs: responseTimeMs, TraceId: traceId,
        ConversationId: null, CacheHit: false);

    private static string BuildCacheQuestion(AssistantRequest request)
    {
        static string Join(IEnumerable<string>? values) => string.Join(',',
            (values ?? []).Select(value => value.Trim().ToLowerInvariant())
                .Where(value => value.Length > 0).OrderBy(value => value, StringComparer.Ordinal));
        var facets = string.Join(';', (request.Facets ?? [])
            .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => $"{entry.Key.Trim().ToLowerInvariant()}={Join(entry.Value)}"));
        return $"{request.Message.Trim()}\n[scope:own={request.OnlyOwnContent};tags={Join(request.Tags)};" +
               $"authors={Join(request.Authors)};types={Join(request.ContentTypes)};facets={facets}]";
    }

    private static string[] ToolCalls(string terminalTool, string contextualizationStrategy) =>
        contextualizationStrategy == "none"
            ? [terminalTool]
            : [$"query_contextualization:{contextualizationStrategy}", terminalTool];

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
}
