using System.Security.Claims;
using KnowledgePortal.Api.Models;

namespace KnowledgePortal.Api.Services;

public sealed class AssistantRequestService(
    AssistantOrchestratorService orchestrator,
    AssistantInteractionService interactions,
    AssistantConversationService conversations,
    IConfiguration config)
{
    private static readonly HashSet<string> PreferredRoutes = new(StringComparer.OrdinalIgnoreCase)
        { "auto", "search", "knowledge_search", "answer", "rag", "knowledge_answer", "analytics", "chat", "general_chat" };

    public async Task<(AssistantResponseDto? Response, ServiceError? Error)> ExecuteAsync(
        AssistantRequest request, ClaimsPrincipal principal, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message)) return (null, new(400, "Message is required."));
        var max = Math.Clamp(config.GetValue("Assistant:MaxMessageCharacters", 4000), 100, 20_000);
        if (request.Message.Length > max) return (null, new(400, $"Message cannot exceed {max} characters."));
        if (!string.IsNullOrWhiteSpace(request.PreferredRoute) && !PreferredRoutes.Contains(request.PreferredRoute))
            return (null, new(400, "preferredRoute must be auto, search, answer, analytics, or chat."));
        var conversation = await conversations.ResolveAsync(request, principal, ct);
        if (conversation.Error != null) return (null, conversation.Error);
        var effective = request with { Message = conversation.Context!.EffectiveMessage };
        var execution = await orchestrator.ExecuteAsync(effective, principal, ct);
        if (execution.Error != null) return execution;
        var response = execution.Response! with { ConversationId = conversation.Context.ConversationId };
        var interactionId = await interactions.RecordAsync(effective.Message, response, principal, ct);
        response = response with { InteractionId = interactionId };
        if (conversation.Context.ConversationId != null)
            await conversations.AppendAsync(conversation.Context.ConversationId, request.Message,
                response, interactionId, principal, ct);
        return (response, null);
    }
}
