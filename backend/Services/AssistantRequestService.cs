using System.Security.Claims;
using KnowledgePortal.Api.Models;

namespace KnowledgePortal.Api.Services;

public sealed record AssistantProgress(string Stage, string Message);

public sealed class AssistantRequestService(
    AssistantOrchestratorService orchestrator,
    AssistantInteractionService interactions,
    AssistantConversationService conversations,
    KnowledgeInputValidationService inputValidation,
    LlmModelSelectionService modelSelection,
    ChatModelContext modelContext,
    IConfiguration config)
{
    public async Task<(AssistantResponseDto? Response, ServiceError? Error)> ExecuteAsync(
        AssistantRequest request, ClaimsPrincipal principal, CancellationToken ct,
        Action<AssistantProgress>? progress = null)
    {
        progress?.Invoke(new("validation", "Soru ve erişim kapsamı denetleniyor."));
        var validationError = inputValidation.ValidateQuestion(request.Message, "Message")
                              ?? inputValidation.ValidateAnswerProfile(request.AnswerProfile)
                              ?? inputValidation.ValidateScope(request.Tags, request.Authors,
                                  request.ContentTypes, request.Facets);
        if (validationError != null) return (null, validationError);
                var retrievalStrategy = string.IsNullOrWhiteSpace(request.RetrievalStrategy)
                    ? AssistantRetrievalStrategies.Baseline
                    : request.RetrievalStrategy.Trim().ToLowerInvariant();
                if (!AssistantRetrievalStrategies.Allowed.Contains(retrievalStrategy, StringComparer.Ordinal))
                    return (null, new ServiceError(400, "Retrieval strategy is invalid."));
                if (retrievalStrategy == AssistantRetrievalStrategies.Agentic &&
                    !config.GetValue("Assistant:AgenticRetrieval:Enabled", false))
                    return (null, new ServiceError(400, "Agentic retrieval is not enabled."));
        progress?.Invoke(new("model", "Yanıt modeli hazırlanıyor."));
        var effectiveModel = string.IsNullOrWhiteSpace(request.Model)
            ? await modelSelection.GetDefaultModelAsync(ct)
            : await modelSelection.ResolveAsync(request.Model, ct);
        if (effectiveModel == null)
            return (null, new ServiceError(400, "Model is not available from the Ollama server."));
        modelContext.Model = effectiveModel;
        progress?.Invoke(new("conversation", "Konuşma bağlamı hazırlanıyor."));
        var conversation = await conversations.ResolveAsync(request, principal, ct);
        if (conversation.Error != null) return (null, conversation.Error);
        var effective = request with
        {
            Message = conversation.Context!.EffectiveMessage,
            Model = effectiveModel,
            RetrievalStrategy = retrievalStrategy
        };
        var execution = await orchestrator.ExecuteAsync(effective, principal, ct,
            conversation.Context.HypotheticalDocument,
            conversation.Context.ContextualizationStrategy,
            conversation.Context.TurnPlan, progress);
        if (execution.Error != null) return execution;
        var response = execution.Response! with { ConversationId = conversation.Context.ConversationId };
        progress?.Invoke(new("finalizing", "Yanıt kaydediliyor ve hazırlanıyor."));
        var interactionId = await interactions.RecordAsync(effective.Message, response, principal, ct);
        response = response with { InteractionId = interactionId };
        if (conversation.Context.ConversationId != null)
            await conversations.AppendAsync(conversation.Context.ConversationId, request.Message,
                response, interactionId, principal, ct);
        return (response, null);
    }
}
