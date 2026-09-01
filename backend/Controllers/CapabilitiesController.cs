using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/capabilities")]
[Authorize]
public sealed class CapabilitiesController(IConfiguration config) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new AssistantCapabilitiesDto(
        config.GetValue("Assistant:Enabled", true),
        config.GetValue("Assistant:Enabled", true) && config.GetValue("Ollama:Enabled", false),
        config.GetValue("Assistant:AuditEnabled", true),
        Math.Clamp(config.GetValue("Assistant:MaxMessageCharacters", 4000), 100, 20_000),
        true,
        config.GetValue("Assistant:ConversationHistoryEnabled", true),
        config.GetValue("Assistant:SemanticCache:Enabled", true),
        config.GetSection("FileStorage:AllowedExtensions").Get<string[]>() ?? [],
        Math.Max(1, config.GetValue("FileStorage:MaxFileSizeMB", 20)),
        Math.Max(1, config.GetValue("FileStorage:MaxAttachmentsPerArticle", 20)),
        RagAnswerProfiles.Allowed,
        RagAnswerProfiles.TryParse(config["Assistant:DefaultAnswerProfile"], out var defaultProfile)
            ? defaultProfile.ToWireValue()
            : "balanced"));
}
