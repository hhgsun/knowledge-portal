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
    AssistantOrchestratorService orchestrator) : ControllerBase
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
        HttpContext.Items[UsageTrackingMiddleware.OperationItem] = $"assistant.{response.Route}";
        return Ok(response);
    }
}
