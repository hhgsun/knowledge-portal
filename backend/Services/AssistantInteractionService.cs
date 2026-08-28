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
        "incorrect", "incomplete", "wrong_source", "outdated", "no_answer", "other"
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
            ApplicationVersion = typeof(AssistantInteractionService).Assembly.GetName().Version?.ToString() ?? "unknown",
            ConversationId = response.ConversationId,
            RagTraceId = response.TraceId,
            RagPromptVersion = RagService.PromptVersion,
            RagRetrievalVersion = RagService.RetrievalVersion,
            RagReranker = config.GetValue("Reranking:External:Enabled", false)
                ? $"external:{config["Reranking:External:Model"] ?? "unspecified"}"
                : "local-deterministic-v1",
            RagIndexProfile = EmbeddingService.ComputeIndexProfile(config),
            RagGroundingStatus = response.Rag?.GroundingStatus,
            RagAnswerHash = string.IsNullOrWhiteSpace(response.Answer) ? null : Fingerprint(response.Answer),
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
        interaction.Helpful = request.Helpful;
        interaction.FeedbackReason = reason;
        interaction.FeedbackAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        metrics.AssistantFeedback.Add(1,
            new("outcome", request.Helpful ? "helpful" : "not_helpful"),
            new("reason", reason ?? "none"));
        return null;
    }

    public async Task<ServiceError?> RecordSourceClickAsync(AssistantSourceClickRequest request,
        ClaimsPrincipal principal, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.InteractionId) || string.IsNullOrWhiteSpace(request.ArticleId))
            return new(400, "interactionId and articleId are required.");
        var interaction = await db.AssistantInteractions.FindAsync([request.InteractionId], ct);
        if (interaction == null) return new(404, "Assistant interaction not found.");
        if (!string.Equals(interaction.UserId, principal.GetUserId(), StringComparison.Ordinal))
            return new(403, "Cannot update another user's assistant interaction.");
        if (!await db.Articles.AnyAsync(article => article.Id == request.ArticleId, ct))
            return new(404, "Source article not found.");
        interaction.ClickedArticleId = request.ArticleId;
        await db.SaveChangesAsync(ct);
        return null;
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static string Fingerprint(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()))).ToLowerInvariant();

}
