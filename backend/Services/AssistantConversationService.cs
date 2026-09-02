using System.Security.Claims;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace KnowledgePortal.Api.Services;

public sealed record AssistantConversationContext(
    string? ConversationId,
    string EffectiveMessage,
    string? HypotheticalDocument,
    string ContextualizationStrategy,
    AssistantTurnPlan TurnPlan);

public sealed class AssistantConversationService(
    AppDbContext db,
    IServiceProvider services,
    IConfiguration config)
{
    public async Task<(AssistantConversationContext? Context, ServiceError? Error)> ResolveAsync(
        AssistantRequest request, ClaimsPrincipal principal, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ConversationId))
        {
            var standalone = request.Message.Trim();
            var initialPlan = services.GetService<AssistantTurnPlanningService>() is { } noHistoryPlanner
                ? await noHistoryPlanner.PlanAsync(standalone, [], ct)
                : new AssistantTurnPlan(standalone, standalone, AssistantTurnActions.Retrieve,
                    "answer", AssistantPresentationModes.Auto, "none");
            return (new(null, initialPlan.StandaloneQuery, null, initialPlan.Strategy, initialPlan), null);
        }
        if (!config.GetValue("Assistant:ConversationHistoryEnabled", true))
            return (null, new(400, "Assistant conversation history is disabled."));
        if (principal.GetSource() == "api-key")
            return (null, new(403, "Conversation history requires session authentication."));
        var userId = principal.GetUserId();
        var conversation = await db.AssistantConversations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.ConversationId && x.UserId == userId, ct);
        if (conversation == null) return (null, new(404, "Assistant conversation not found."));

        var maxMessages = Math.Clamp(config.GetValue(
            "Assistant:QueryContextualization:MaxHistoryMessages", 6), 2, 12);
        var maxCharacters = Math.Clamp(config.GetValue(
            "Assistant:QueryContextualization:MaxHistoryCharacters", 6000), 500, 20_000);
        var maxCharactersPerMessage = Math.Clamp(config.GetValue(
            "Assistant:QueryContextualization:MaxCharactersPerMessage", 1500), 200, 4000);
        var recent = await db.AssistantMessages.AsNoTracking()
            .Where(x => x.ConversationId == conversation.Id)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Take(maxMessages).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => new AssistantConversationTurn(x.Role, x.Content, x.TurnStateJson)).ToListAsync(ct);
        var bounded = BoundHistory(recent, maxCharacters, maxCharactersPerMessage);
        if (services.GetService<AssistantTurnPlanningService>() is not { } planner)
        {
            var fallback = request.Message.Trim();
            var fallbackPlan = new AssistantTurnPlan(fallback, fallback, AssistantTurnActions.Retrieve,
                "answer", AssistantPresentationModes.Auto, "none");
            return (new(conversation.Id, fallback, null, "none", fallbackPlan), null);
        }
        var plan = await planner.PlanAsync(request.Message, bounded, ct);
        return (new(conversation.Id, plan.StandaloneQuery,
            plan.HypotheticalDocument, plan.Strategy, plan), null);
    }

    public async Task<AssistantConversation> CreateAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var existing = db.AssistantConversations.Where(x => x.UserId == userId);
        if (db.Database.IsRelational())
            await existing.ExecuteDeleteAsync(ct);
        else
        {
            db.AssistantConversations.RemoveRange(await existing.ToListAsync(ct));
            await db.SaveChangesAsync(ct);
        }

        var conversation = new AssistantConversation { UserId = userId };
        db.AssistantConversations.Add(conversation); await db.SaveChangesAsync(ct);
        return conversation;
    }

    public async Task AppendAsync(string conversationId, string userText, AssistantResponseDto response,
        string? interactionId, ClaimsPrincipal principal, CancellationToken ct)
    {
        var conversation = await db.AssistantConversations
            .SingleOrDefaultAsync(x => x.Id == conversationId && x.UserId == principal.GetUserId(), ct);
        if (conversation == null) return;
        if (conversation.Title == "Yeni konuşma")
            conversation.Title = userText.Trim()[..Math.Min(userText.Trim().Length, 120)];
        conversation.UpdatedAt = DateTime.UtcNow;
        db.AssistantMessages.AddRange(
            new AssistantMessage { ConversationId = conversationId, Role = "user", Content = userText.Trim() },
            new AssistantMessage { ConversationId = conversationId, Role = "assistant",
                Content = response.Answer ?? "", InteractionId = interactionId,
                TurnStateJson = SerializeState(userText, response) });
        await db.SaveChangesAsync(ct);
    }

    public async Task PruneExpiredAsync(string userId, CancellationToken ct)
    {
        var days = Math.Clamp(config.GetValue("Assistant:ConversationRetentionDays", 90), 1, 3650);
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var expired = db.AssistantConversations.Where(x => x.UserId == userId && x.UpdatedAt < cutoff);
        if (db.Database.IsRelational()) await expired.ExecuteDeleteAsync(ct);
        else { db.AssistantConversations.RemoveRange(await expired.ToListAsync(ct)); await db.SaveChangesAsync(ct); }
    }

    private static IReadOnlyList<AssistantConversationTurn> BoundHistory(
        IReadOnlyList<AssistantConversationTurn> history, int maxCharacters,
        int maxCharactersPerMessage)
    {
        var selected = new List<AssistantConversationTurn>();
        var remaining = maxCharacters;
        foreach (var turn in history.Reverse())
        {
            if (remaining <= 0) break;
            var compact = string.Join(' ', turn.Content.Split((char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
            var take = Math.Min(Math.Min(compact.Length, maxCharactersPerMessage), remaining);
            if (take > 0) selected.Add(turn with { Content = compact[..take] });
            remaining -= take;
        }
        selected.Reverse();
        return selected;
    }

    internal static string SerializeState(string originalRequest, AssistantResponseDto response) =>
        JsonSerializer.Serialize(new AssistantStoredTurnState(
            originalRequest.Trim(), response.NormalizedQuery, response.Intent, response.Presentation,
            response.Answer ?? "", CompactTurnStateRag(response.Rag), response.AnswerProfile),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static AssistantRagDto? CompactTurnStateRag(AssistantRagDto? rag) => rag == null ? null : rag with
    {
        // Persist citation/provenance identity, not duplicated source passages. The current corpus
        // remains the authority and presentation-only turns never re-run grounding from this JSON.
        Evidence = rag.Evidence.Select(item => item with { Passage = "" }).ToArray()
    };
}
