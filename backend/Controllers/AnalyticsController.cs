using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/analytics")]
[Authorize]
[RequirePermission(Permissions.AnalyticsView)]
[RequireSessionAuth]
public class AnalyticsController(AnalyticsReportService reports) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int days = 30, CancellationToken ct = default)
    {
        return Ok(await reports.GetAsync(days, ct));
    }
}
