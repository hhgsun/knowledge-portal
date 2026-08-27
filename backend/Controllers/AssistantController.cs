using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Middleware;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/assistant")]
[Authorize]
[EnableRateLimiting("search")]
public sealed class AssistantController(
    IConfiguration config,
    AssistantOrchestratorService orchestrator,
    AssistantRouterService router,
    AssistantInteractionService interactions) : ControllerBase
{
    private static readonly HashSet<string> PreferredRoutes = new(StringComparer.OrdinalIgnoreCase)
        { "auto", "search", "knowledge_search", "answer", "rag", "knowledge_answer", "analytics", "chat", "general_chat" };

    [HttpPost]
    public async Task<IActionResult> Execute(AssistantRequest request)
    {
        if (!config.GetValue("Assistant:Enabled", true))
            return NotFound(new { error = "Assistant is disabled." });
        if (!string.IsNullOrWhiteSpace(request.PreferredRoute)
            && !PreferredRoutes.Contains(request.PreferredRoute))
            return BadRequest(new { error = "preferredRoute must be auto, search, answer, analytics, or chat." });

        var execution = await orchestrator.ExecuteAsync(request, User, HttpContext.RequestAborted);
        if (execution.Error != null)
        {
            HttpContext.Items[UsageTrackingMiddleware.OutcomeItem] = "assistant_error";
            return execution.Error.ToActionResult();
        }

        var response = execution.Response!;
        var interactionId = await interactions.RecordAsync(request.Message, response, User,
            HttpContext.RequestAborted);
        response = response with { InteractionId = interactionId };
        HttpContext.Items[UsageTrackingMiddleware.OperationItem] = $"assistant.{response.Route}";
        return Ok(response);
    }

    [HttpPost("feedback")]
    public async Task<IActionResult> Feedback(AssistantFeedbackRequest request)
    {
        if (!config.GetValue("Assistant:Enabled", true))
            return NotFound(new { error = "Assistant is disabled." });
        if (!config.GetValue("Assistant:AuditEnabled", true))
            return NotFound(new { error = "Assistant feedback is disabled." });
        var error = await interactions.RecordFeedbackAsync(request, User,
            HttpContext.RequestAborted);
        if (error != null) return error.ToActionResult();
        HttpContext.Items[UsageTrackingMiddleware.OperationItem] = "assistant.feedback";
        return Ok(new { message = "Feedback recorded." });
    }

    /// <summary>
    /// Admin-only classifier quality probe. It returns the routing decision without authorizing or
    /// executing a tool, so live golden gates are independent of document/RAG availability.
    /// </summary>
    [HttpPost("route-preview")]
    [RequirePermission(Permissions.UsersManage)]
    [RequireSessionAuth]
    public async Task<IActionResult> RoutePreview(AssistantRequest request)
    {
        if (!config.GetValue("Assistant:Enabled", true))
            return NotFound(new { error = "Assistant is disabled." });
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { error = "Message is required." });
        var maxLength = Math.Clamp(config.GetValue("Assistant:MaxMessageCharacters", 4000), 100, 20_000);
        if (request.Message.Length > maxLength)
            return BadRequest(new { error = $"Message cannot exceed {maxLength} characters." });
        if (!string.IsNullOrWhiteSpace(request.PreferredRoute)
            && !PreferredRoutes.Contains(request.PreferredRoute))
            return BadRequest(new { error = "preferredRoute must be auto, search, answer, analytics, or chat." });

        var decision = await router.RouteAsync(request.Message, request.PreferredRoute,
            HttpContext.RequestAborted);
        HttpContext.Items[UsageTrackingMiddleware.OperationItem] = "assistant.route_preview";
        return Ok(new
        {
            route = AssistantRouterService.RouteName(decision.Route),
            decision.Confidence,
            routeSource = decision.Source,
            decision.ReasonCode,
            decision.NormalizedQuery,
            decision.IncludeSearchResults
        });
    }
}
