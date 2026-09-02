using System.Security.Claims;
using KnowledgePortal.Api.Models;

namespace KnowledgePortal.Api.Services;

public sealed class AssistantRequestService(
    AssistantOrchestratorService orchestrator,
    AssistantInteractionService interactions,
    AssistantConversationService conversations,
    KnowledgeInputValidationService inputValidation,
    LlmModelSelectionService modelSelection,
    ChatModelContext modelContext)
{
    public async Task<(AssistantResponseDto? Response, ServiceError? Error)> ExecuteAsync(
        AssistantRequest request, ClaimsPrincipal principal, CancellationToken ct)
    {
        var validationError = inputValidation.ValidateQuestion(request.Message, "Message")
                              ?? inputValidation.ValidateAnswerProfile(request.AnswerProfile)
                              ?? inputValidation.ValidateScope(request.Tags, request.Authors,
                                  request.ContentTypes, request.Facets);
        if (validationError != null) return (null, validationError);
        var effectiveModel = string.IsNullOrWhiteSpace(request.Model)
            ? await modelSelection.GetDefaultModelAsync(ct)
            : await modelSelection.ResolveAsync(request.Model, ct);
        if (effectiveModel == null)
            return (null, new ServiceError(400, "Model is not available from the Ollama server."));
        modelContext.Model = effectiveModel;
        var conversation = await conversations.ResolveAsync(request, principal, ct);
        if (conversation.Error != null) return (null, conversation.Error);
        var effective = request with
        {
            Message = conversation.Context!.EffectiveMessage,
            Model = effectiveModel
        };
        var execution = await orchestrator.ExecuteAsync(effective, principal, ct,
            conversation.Context.HypotheticalDocument,
            conversation.Context.ContextualizationStrategy,
            conversation.Context.TurnPlan);
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
