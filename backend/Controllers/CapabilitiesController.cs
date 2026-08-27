using KnowledgePortal.Api.Models;
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
        config.GetValue("AgenticRouting:Enabled", true),
        config.GetValue("AgenticRouting:ClassifierEnabled", true),
        config.GetValue("Assistant:AuditEnabled", true),
        Math.Clamp(config.GetValue("Assistant:MaxMessageCharacters", 4000), 100, 20_000),
        ["auto", "search", "answer", "analytics", "chat"],
        true,
        config.GetValue("Assistant:ConversationHistoryEnabled", true),
        config.GetValue("Assistant:SemanticCache:Enabled", true),
        config["AgenticRouting:Model"] ?? config["Ollama:ChatModel"] ?? "unknown"));
}
