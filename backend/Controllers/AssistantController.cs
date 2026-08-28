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
[EnableRateLimiting("assistant")]
public sealed class AssistantController(
    IConfiguration config,
    AssistantRequestService requests,
    AssistantInteractionService interactions) : ControllerBase
{
    private static readonly JsonSerializerOptions SseJson = new(JsonSerializerDefaults.Web);

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
        HttpContext.Items[UsageTrackingMiddleware.OperationItem] = "assistant.answer";
        return Ok(response);
    }

    [HttpPost("stream")]
    public async Task Stream(AssistantRequest request)
    {
        Response.StatusCode = 200;
        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers["X-Accel-Buffering"] = "no";
        if (!config.GetValue("Assistant:Enabled", true))
        {
            await WriteEventAsync("error", new { error = "Assistant is disabled.", status = 404 }); return;
        }
        await WriteEventAsync("status", new { stage = "retrieval", message = "Yetkili kaynaklar getiriliyor." });
        await WriteEventAsync("status", new { stage = "grounding", message = "Yanıt kanıtlarla doğrulanıyor." });
        var execution = await requests.ExecuteAsync(request, User, HttpContext.RequestAborted);
        if (execution.Error != null)
        {
            await WriteEventAsync("error", new { error = execution.Error.Message,
                status = execution.Error.StatusCode }); return;
        }
        var response = execution.Response!;
        await WriteEventAsync("metadata", new { response.CacheHit });
        var answer = response.Answer ?? "";
        foreach (var chunk in TokenChunks(answer, 4))
            await WriteEventAsync("token", new { text = chunk });
        await WriteEventAsync("complete", response);
        HttpContext.Items[UsageTrackingMiddleware.OperationItem] = "assistant.stream.answer";
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

    [HttpPost("source-click")]
    public async Task<IActionResult> SourceClick(AssistantSourceClickRequest request)
    {
        var error = await interactions.RecordSourceClickAsync(request, User, HttpContext.RequestAborted);
        if (error != null) return error.ToActionResult();
        HttpContext.Items[UsageTrackingMiddleware.OperationItem] = "assistant.source_click";
        return Ok(new { message = "Source click recorded." });
    }
}
