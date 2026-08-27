using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public sealed class AssistantInteractionService(
    AppDbContext db,
    IConfiguration config,
    PortalMetrics metrics,
    ILogger<AssistantInteractionService> logger)
{
    private static readonly HashSet<string> FeedbackReasons =
    [
        "incorrect", "incomplete", "wrong_source", "wrong_route", "outdated", "no_answer", "other"
    ];

    public async Task<string?> RecordAsync(string query, AssistantResponseDto response,
        ClaimsPrincipal principal, CancellationToken ct)
    {
        if (!config.GetValue("Assistant:AuditEnabled", true)) return null;
        var interaction = new AssistantInteraction
        {
            UserId = EmptyToNull(principal.GetUserId()),
            ApiKeyId = principal.GetApiKeyId(),
            QueryFingerprint = Fingerprint(query),
            Route = response.Route,
            RouteSource = response.RouteSource,
            ReasonCode = response.ReasonCode,
            Confidence = response.Confidence,
            RawConfidence = response.RawConfidence,
            ConfidenceCalibrationSamples = response.ConfidenceCalibrationSamples,
            RoutingPromptVersion = AssistantRouterService.RoutingPromptVersion,
            ClassifierModel = response.RouteSource == "classifier_model_fallback"
                ? config["Ollama:ChatModel"] ?? "unknown"
                : config["AgenticRouting:Model"] ?? config["Ollama:ChatModel"] ?? "unknown",
            RoutingConfigSnapshotJson = BuildRoutingSnapshot(),
            ApplicationVersion = typeof(AssistantInteractionService).Assembly.GetName().Version?.ToString() ?? "unknown",
            ConversationId = response.ConversationId,
            SearchQueryId = response.SearchQueryId,
            ToolCallsJson = JsonSerializer.Serialize(response.ToolCalls),
            DurationMs = response.ResponseTimeMs
        };
        db.AssistantInteractions.Add(interaction);
        try
        {
            await db.SaveChangesAsync(ct);
            return interaction.Id;
        }
        catch (Exception ex)
        {
            db.Entry(interaction).State = EntityState.Detached;
            logger.LogWarning(ex, "Assistant interaction audit could not be persisted for trace {TraceId}",
                response.TraceId);
            metrics.AssistantAuditFailures.Add(1);
            return null;
        }
    }

    public async Task<ServiceError?> RecordFeedbackAsync(AssistantFeedbackRequest request,
        ClaimsPrincipal principal, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.InteractionId))
            return new(400, "interactionId is required.");
        var interaction = await db.AssistantInteractions.FindAsync([request.InteractionId], ct);
        if (interaction == null) return new(404, "Assistant interaction not found.");
        if (!string.Equals(interaction.UserId, principal.GetUserId(), StringComparison.Ordinal))
            return new(403, "Cannot update another user's assistant interaction.");

        var reason = string.IsNullOrWhiteSpace(request.Reason)
            ? null
            : request.Reason.Trim().ToLowerInvariant();
        if (reason != null && !FeedbackReasons.Contains(reason))
            return new(400, "Invalid feedback reason.");
        var corrected = string.IsNullOrWhiteSpace(request.CorrectedRoute)
            ? null
            : AssistantRouterService.ParseRoute(request.CorrectedRoute) is { } route
                ? AssistantRouterService.RouteName(route)
                : "invalid";
        if (corrected == "invalid") return new(400, "Invalid correctedRoute.");

        interaction.Helpful = request.Helpful;
        interaction.FeedbackReason = reason;
        interaction.CorrectedRoute = corrected;
        interaction.FeedbackAt = DateTime.UtcNow;

        if (!request.Helpful && !string.IsNullOrWhiteSpace(request.Question)
            && string.Equals(Fingerprint(request.Question), interaction.QueryFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            var exists = await db.AssistantEvaluationCandidates.AnyAsync(
                x => x.InteractionId == interaction.Id, ct);
            if (!exists)
                db.AssistantEvaluationCandidates.Add(new AssistantEvaluationCandidate
                {
                    InteractionId = interaction.Id,
                    Question = request.Question.Trim(),
                    ActualRoute = interaction.Route,
                    ExpectedRoute = corrected,
                    Reason = reason ?? "other"
                });
        }

        // Keep grounded-answer feedback in the existing RAG evaluation cohort as well.
        // Search-only interactions are intentionally excluded because they have no
        // generated answer to evaluate.
        if (interaction.SearchQueryId != null)
        {
            var searchQuery = await db.SearchQueries.FindAsync([interaction.SearchQueryId], ct);
            if (searchQuery?.SearchType == "rag"
                && string.Equals(searchQuery.UserId, principal.GetUserId(), StringComparison.Ordinal))
            {
                searchQuery.RagFeedback = request.Helpful ? "helpful" : "not_helpful";
                searchQuery.RagFeedbackReason = reason == "wrong_route" ? "other" : reason;
                searchQuery.RagFeedbackAt = interaction.FeedbackAt;
            }
        }

        await db.SaveChangesAsync(ct);
        metrics.AssistantFeedback.Add(1,
            new("outcome", request.Helpful ? "helpful" : "not_helpful"),
            new("reason", reason ?? "none"));
        return null;
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static string Fingerprint(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()))).ToLowerInvariant();

    private string BuildRoutingSnapshot() => JsonSerializer.Serialize(new SortedDictionary<string, object?>
    {
        ["promptVersion"] = AssistantRouterService.RoutingPromptVersion,
        ["routingModel"] = config["AgenticRouting:Model"] ?? config["Ollama:ChatModel"],
        ["chatModelFallback"] = config["Ollama:ChatModel"],
        ["shadowModel"] = config["AgenticRouting:Shadow:Model"],
        ["minConfidence"] = config.GetValue("AgenticRouting:MinConfidence", .78),
        ["confidenceThresholds"] = new SortedDictionary<string, double>
        {
            ["analytics"] = config.GetValue("AgenticRouting:ConfidenceThresholds:analytics", .86),
            ["general_chat"] = config.GetValue("AgenticRouting:ConfidenceThresholds:general_chat", .75),
            ["knowledge_answer"] = config.GetValue("AgenticRouting:ConfidenceThresholds:knowledge_answer", .80),
            ["knowledge_search"] = config.GetValue("AgenticRouting:ConfidenceThresholds:knowledge_search", .72)
        },
        ["classifierTimeoutSeconds"] = config.GetValue("AgenticRouting:ClassifierTimeoutSeconds", 8),
        ["defaultRoute"] = config["AgenticRouting:DefaultRoute"],
        ["calibrationEnabled"] = config.GetValue("AgenticRouting:Calibration:Enabled", true)
    });
}
