using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/llm-models")]
[Authorize]
public sealed class LlmModelsController(LlmModelSelectionService selection) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get() => Ok(await selection.GetSettingsAsync(HttpContext.RequestAborted));
}

[ApiController]
[Route("api/admin/llm-settings")]
[Authorize]
[RequirePermission(Permissions.UsersManage)]
[RequireSessionAuth]
public sealed class AdminLlmSettingsController(LlmModelSelectionService selection) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get() => Ok(await selection.GetSettingsAsync(HttpContext.RequestAborted));

    [HttpPut]
    public async Task<IActionResult> Update(UpdateDefaultLlmModelRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Model)
            || !await selection.IsAvailableAsync(request.Model, HttpContext.RequestAborted))
            return BadRequest(new { error = "Model is not available from the Ollama server." });
        await selection.SetDefaultModelAsync(request.Model, User.GetUserId(), HttpContext.RequestAborted);
        return Ok(await selection.GetSettingsAsync(HttpContext.RequestAborted));
    }
}
