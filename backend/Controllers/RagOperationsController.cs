using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/admin/rag")]
[Authorize]
[RequirePermission(Permissions.UsersManage)]
[RequireSessionAuth]
public sealed class RagOperationsController(IConfiguration config) : ControllerBase
{
    [HttpGet("observability")]
    public IActionResult Observability([FromServices] RagResilienceService resilience) => Ok(new
    {
        runtime = resilience.Snapshot(), metricsEndpoint = "/metrics",
        activitySource = PortalMetrics.ActivitySourceName, metricPrefix = "kp_rag_",
        retrieval = new
        {
            queryRewriteEnabled = config.GetValue("Ollama:QueryUnderstanding:RewriteEnabled", true),
            decompositionEnabled = config.GetValue("Ollama:QueryUnderstanding:DecompositionEnabled", true),
            contextExpansionEnabled = config.GetValue("Ollama:ContextExpansion:Enabled", true),
            externalRerankerEnabled = config.GetValue("Reranking:External:Enabled", false)
        },
        privacy = "Raw questions are not logged or used as metric labels; traces contain a short SHA-256 fingerprint and query length."
    });

    [HttpGet("debug")]
    public async Task<IActionResult> Debug([FromQuery] string? q, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "Query parameter 'q' is required" });
        if (!config.GetValue("Ollama:Enabled", false)
            || HttpContext.RequestServices.GetService<RagService>() is not { } rag)
            return StatusCode(503, new { error = "RAG service is not available" });
        return Ok(await rag.DebugAsync(q.Trim(), null, cancellationToken));
    }
}
