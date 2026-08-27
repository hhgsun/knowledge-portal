using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Middleware;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/assistant")]
[Authorize]
[EnableRateLimiting("search")]
public sealed class AssistantController(
    IConfiguration config,
    AssistantRequestService requests,
    AssistantRouterService router,
    AssistantInteractionService interactions) : ControllerBase
{
    private static readonly JsonSerializerOptions SseJson = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> PreferredRoutes = new(StringComparer.OrdinalIgnoreCase)
        { "auto", "search", "knowledge_search", "answer", "rag", "knowledge_answer", "analytics", "chat", "general_chat" };

    [HttpPost]
    public async Task<IActionResult> Execute(AssistantRequest request)
    {
        if (!config.GetValue("Assistant:Enabled", true))
            return NotFound(new { error = "Assistant is disabled." });
        var execution = await requests.ExecuteAsync(request, User, HttpContext.RequestAborted);
        if (execution.Error != null)
        {
            HttpContext.Items[UsageTrackingMiddleware.OutcomeItem] = "assistant_error";
            return execution.Error.ToActionResult();
        }

        var response = execution.Response!;
        HttpContext.Items[UsageTrackingMiddleware.OperationItem] = $"assistant.{response.Route}";
        return Ok(response);
    }

    [HttpPost("stream")]
    public async Task Stream(AssistantRequest request)
    {
        Response.StatusCode = 200;
        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers["X-Accel-Buffering"] = "no";
        await WriteEventAsync("status", new { stage = "routing", message = "İstek yönlendiriliyor." });
        if (!config.GetValue("Assistant:Enabled", true))
        {
            await WriteEventAsync("error", new { error = "Assistant is disabled.", status = 404 }); return;
        }
        await WriteEventAsync("status", new { stage = "processing", message = "Kaynaklar aranıyor ve yanıt doğrulanıyor." });
        var execution = await requests.ExecuteAsync(request, User, HttpContext.RequestAborted);
        if (execution.Error != null)
        {
            await WriteEventAsync("error", new { error = execution.Error.Message,
                status = execution.Error.StatusCode }); return;
        }
        var response = execution.Response!;
        await WriteEventAsync("metadata", new { response.Route, response.RouteSource,
            response.Confidence, response.RawConfidence, response.CacheHit });
        var answer = response.Answer ?? response.Clarification ?? "";
        foreach (var chunk in TokenChunks(answer, 4))
            await WriteEventAsync("token", new { text = chunk });
        await WriteEventAsync("complete", response);
        HttpContext.Items[UsageTrackingMiddleware.OperationItem] = $"assistant.stream.{response.Route}";
    }

    private async Task WriteEventAsync(string eventName, object value)
    {
        await Response.WriteAsync($"event: {eventName}\n", HttpContext.RequestAborted);
        await Response.WriteAsync($"data: {JsonSerializer.Serialize(value, SseJson)}\n\n", HttpContext.RequestAborted);
        await Response.Body.FlushAsync(HttpContext.RequestAborted);
    }

    private static IEnumerable<string> TokenChunks(string text, int wordsPerChunk)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i += wordsPerChunk)
            yield return (i == 0 ? "" : " ") + string.Join(' ', words.Skip(i).Take(wordsPerChunk));
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
