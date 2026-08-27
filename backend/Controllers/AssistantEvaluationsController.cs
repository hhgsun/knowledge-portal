using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/admin/assistant-evaluations")]
[Authorize]
[RequirePermission(Permissions.UsersManage)]
[RequireSessionAuth]
public sealed class AssistantEvaluationsController(AppDbContext db) : ControllerBase
{
    [HttpGet("candidates")]
    public async Task<IActionResult> Candidates([FromQuery] string? status = "pending")
    {
        var query = db.AssistantEvaluationCandidates.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        var items = await query.OrderByDescending(x => x.CreatedAt).Take(200)
            .Select(x => new { x.Id, x.InteractionId, x.Question, x.ActualRoute, x.ExpectedRoute,
                x.Reason, x.Status, x.ReviewedById, x.CreatedAt, x.ReviewedAt }).ToListAsync();
        return Ok(new { candidates = items });
    }

    [HttpPut("candidates/{id}")]
    public async Task<IActionResult> Review(string id, ReviewAssistantCandidateRequest request)
    {
        var status = request.Status?.Trim().ToLowerInvariant();
        if (status is not ("approved" or "rejected"))
            return BadRequest(new { error = "Status must be approved or rejected." });
        var expected = string.IsNullOrWhiteSpace(request.ExpectedRoute) ? null
            : AssistantRouterService.ParseRoute(request.ExpectedRoute) is { } route
                ? AssistantRouterService.RouteName(route) : "invalid";
        if (expected == "invalid" || status == "approved" && expected == null)
            return BadRequest(new { error = "An approved candidate requires a valid expectedRoute." });
        var candidate = await db.AssistantEvaluationCandidates.FindAsync(id);
        if (candidate == null) return NotFound(new { error = "Candidate not found." });
        candidate.Status = status; candidate.ExpectedRoute = expected ?? candidate.ExpectedRoute;
        candidate.ReviewedById = User.GetUserId(); candidate.ReviewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { candidate.Id, candidate.Status, candidate.ExpectedRoute });
    }

    [HttpGet("routing-summary")]
    public async Task<IActionResult> RoutingSummary([FromQuery] int days = 30)
    {
        var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 365));
        var samples = await db.AssistantRoutingShadowSamples.AsNoTracking()
            .Where(x => x.CreatedAt >= since).ToListAsync();
        return Ok(new { days, total = samples.Count,
            agreementRate = samples.Count == 0 ? 0 : samples.Count(x => x.Agreed) / (double)samples.Count,
            disagreements = samples.Where(x => !x.Agreed).GroupBy(x => new { x.PrimaryRoute, x.ShadowRoute })
                .Select(x => new { primaryRoute = x.Key.PrimaryRoute, shadowRoute = x.Key.ShadowRoute,
                    count = x.Count() }).OrderByDescending(x => x.count) });
    }
}
