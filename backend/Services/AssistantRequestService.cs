using System.Security.Claims;
using KnowledgePortal.Api.Models;

namespace KnowledgePortal.Api.Services;

public sealed class AssistantRequestService(
    AssistantOrchestratorService orchestrator,
    AssistantInteractionService interactions,
    AssistantConversationService conversations,
    KnowledgeInputValidationService inputValidation)
{
    public async Task<(AssistantResponseDto? Response, ServiceError? Error)> ExecuteAsync(
        AssistantRequest request, ClaimsPrincipal principal, CancellationToken ct)
    {
        var validationError = inputValidation.ValidateQuestion(request.Message, "Message")
                              ?? inputValidation.ValidateScope(request.Tags, request.Authors,
                                  request.ContentTypes, request.Facets);
        if (validationError != null) return (null, validationError);
        var conversation = await conversations.ResolveAsync(request, principal, ct);
        if (conversation.Error != null) return (null, conversation.Error);
        var effective = request with { Message = conversation.Context!.EffectiveMessage };
        var execution = await orchestrator.ExecuteAsync(effective, principal, ct,
            conversation.Context.HypotheticalDocument,
            conversation.Context.ContextualizationStrategy);
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
