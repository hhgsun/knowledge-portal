using System.Text.Json;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

public record SaveRagDatasetRequest(string Name, string? Description, string? Version,
    List<RagEvaluationCase> Cases, RagEvaluationThresholds Thresholds);

[ApiController]
[Route("api/admin/rag-evaluations")]
[Authorize]
[RequirePermission(Permissions.UsersManage)]
[RequireSessionAuth]
public class RagEvaluationsController(AppDbContext db, IServiceProvider services, IConfiguration config) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [HttpGet("datasets")]
    public async Task<IActionResult> Datasets()
    {
        var rows = await db.RagEvaluationDatasets.OrderBy(x => x.Name).ToListAsync();
        return Ok(new { datasets = rows.Select(x => new { x.Id, x.Name, x.Description, x.Version,
            caseCount = RagEvaluationService.ParseCases(x.CasesJson).Count, x.CreatedAt, x.UpdatedAt }) });
    }

    [HttpGet("datasets/{id}")]
    public async Task<IActionResult> Dataset(string id)
    {
        var x = await db.RagEvaluationDatasets.FindAsync(id);
        return x == null ? NotFound(new { error = "Evaluation dataset not found" }) : Ok(ToDataset(x));
    }

    [HttpPost("datasets")]
    public async Task<IActionResult> Create(SaveRagDatasetRequest request)
    {
        var error = Validate(request); if (error != null) return BadRequest(new { error });
        var x = new RagEvaluationDataset(); Apply(x, request); db.Add(x); await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Dataset), new { id = x.Id }, ToDataset(x));
    }

    [HttpPut("datasets/{id}")]
    public async Task<IActionResult> Update(string id, SaveRagDatasetRequest request)
    {
        var error = Validate(request); if (error != null) return BadRequest(new { error });
        var x = await db.RagEvaluationDatasets.FindAsync(id); if (x == null) return NotFound(new { error = "Evaluation dataset not found" });
        Apply(x, request); await db.SaveChangesAsync(); return Ok(ToDataset(x));
    }

    [HttpDelete("datasets/{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var x = await db.RagEvaluationDatasets.FindAsync(id); if (x == null) return NotFound(new { error = "Evaluation dataset not found" });
        db.Remove(x); await db.SaveChangesAsync(); return Ok(new { message = "Evaluation dataset deleted" });
    }

    [HttpPost("datasets/{id}/runs")]
    public async Task<IActionResult> Run(string id)
    {
        if (!config.GetValue("Ollama:Enabled", false) || services.GetService<RagService>() == null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "RAG service is not available" });
        var dataset = await db.RagEvaluationDatasets.FindAsync(id); if (dataset == null) return NotFound(new { error = "Evaluation dataset not found" });
        var now = DateTime.UtcNow;
        if (await db.RagEvaluationRuns.AnyAsync(x => x.Status == "pending" ||
                (x.Status == "running" && x.LeaseExpiresAt != null && x.LeaseExpiresAt > now)))
            return Conflict(new { error = "Another evaluation run is already active" });
        var evaluator = services.GetRequiredService<RagEvaluationService>();
        var run = new RagEvaluationRun
        {
            DatasetId = id,
            RequestedById = User.GetUserId(),
            TotalCases = RagEvaluationService.ParseCases(dataset.CasesJson).Count,
            DatasetVersion = dataset.Version,
            CasesSnapshotJson = dataset.CasesJson,
            ThresholdsSnapshotJson = dataset.ThresholdsJson,
            RuntimeSnapshotJson = await evaluator.BuildRuntimeSnapshotAsync(HttpContext.RequestAborted)
        };
        db.Add(run); await db.SaveChangesAsync(); return AcceptedAtAction(nameof(GetRun), new { runId = run.Id }, RunDto(run));
    }

    [HttpGet("runs")]
    public async Task<IActionResult> Runs() => Ok(new { runs = await db.RagEvaluationRuns.Include(x => x.Dataset)
        .OrderByDescending(x => x.CreatedAt).Take(50).Select(x => new { x.Id, x.DatasetId, datasetName = x.Dataset.Name, x.Status,
            x.TotalCases, x.CompletedCases, x.MetricsJson, x.Error, x.CreatedAt, x.StartedAt, x.CompletedAt }).ToListAsync() });

    [HttpGet("runs/{runId}")]
    public async Task<IActionResult> GetRun(string runId)
    {
        var run = await db.RagEvaluationRuns.Include(x => x.Dataset).SingleOrDefaultAsync(x => x.Id == runId);
        return run == null ? NotFound(new { error = "Evaluation run not found" }) : Ok(RunDto(run));
    }

    [HttpGet("feedback-summary")]
    public async Task<IActionResult> FeedbackSummary([FromQuery] int days = 30)
    {
        days = Math.Clamp(days, 1, 365);
        var since = DateTime.UtcNow.AddDays(-days);
        var rows = await db.SearchQueries.AsNoTracking()
            .Where(x => x.SearchType == "rag" && x.RagFeedbackAt >= since && x.RagFeedback != null)
            .Select(x => new { x.RagFeedback, x.RagFeedbackReason, x.RagGroundingStatus,
                x.RagPromptVersion, x.RagRetrievalVersion, x.RagReranker, x.RagIndexProfile, x.ResponseTimeMs })
            .ToListAsync();
        var helpful = rows.Count(x => x.RagFeedback == "helpful");
        var assistantRows = await db.AssistantInteractions.AsNoTracking()
            .Where(x => x.FeedbackAt >= since && x.Helpful != null)
            .Select(x => new { x.Route, x.RouteSource, x.Helpful, x.FeedbackReason, x.CorrectedRoute,
                x.DurationMs })
            .ToListAsync();
        var assistantHelpful = assistantRows.Count(x => x.Helpful == true);
        return Ok(new
        {
            days,
            total = rows.Count,
            helpful,
            notHelpful = rows.Count - helpful,
            helpfulRate = rows.Count == 0 ? 0 : helpful / (double)rows.Count,
            averageResponseTimeMs = rows.Count == 0 ? 0 : rows.Average(x => x.ResponseTimeMs ?? 0),
            reasons = rows.Where(x => x.RagFeedback == "not_helpful")
                .GroupBy(x => x.RagFeedbackReason ?? "unspecified")
                .Select(x => new { reason = x.Key, count = x.Count() }).OrderByDescending(x => x.count),
            grounding = rows.GroupBy(x => x.RagGroundingStatus ?? "unknown")
                .Select(x => new { status = x.Key, count = x.Count(),
                    helpfulRate = x.Count(y => y.RagFeedback == "helpful") / (double)x.Count() })
                .OrderByDescending(x => x.count),
            configurations = rows.GroupBy(x => new { x.RagPromptVersion, x.RagRetrievalVersion, x.RagReranker, x.RagIndexProfile })
                .Select(x => new { promptVersion = x.Key.RagPromptVersion, indexProfile = x.Key.RagIndexProfile,
                    retrievalVersion = x.Key.RagRetrievalVersion, reranker = x.Key.RagReranker,
                    count = x.Count(), helpfulRate = x.Count(y => y.RagFeedback == "helpful") / (double)x.Count() })
                .OrderByDescending(x => x.count).Take(10),
            assistant = new
            {
                total = assistantRows.Count,
                helpful = assistantHelpful,
                notHelpful = assistantRows.Count - assistantHelpful,
                helpfulRate = assistantRows.Count == 0 ? 0 : assistantHelpful / (double)assistantRows.Count,
                averageResponseTimeMs = assistantRows.Count == 0 ? 0 : assistantRows.Average(x => x.DurationMs),
                reasons = assistantRows.Where(x => x.Helpful == false)
                    .GroupBy(x => x.FeedbackReason ?? "unspecified")
                    .Select(x => new { reason = x.Key, count = x.Count() }).OrderByDescending(x => x.count),
                routes = assistantRows.GroupBy(x => new { x.Route, x.RouteSource })
                    .Select(x => new { route = x.Key.Route, source = x.Key.RouteSource, count = x.Count(),
                        helpfulRate = x.Count(y => y.Helpful == true) / (double)x.Count() })
                    .OrderByDescending(x => x.count),
                corrections = assistantRows.Where(x => x.CorrectedRoute != null)
                    .GroupBy(x => x.CorrectedRoute!)
                    .Select(x => new { route = x.Key, count = x.Count() }).OrderByDescending(x => x.count)
            }
        });
    }

    private static object ToDataset(RagEvaluationDataset x) => new { x.Id, x.Name, x.Description, x.Version,
        cases = RagEvaluationService.ParseCases(x.CasesJson), thresholds = RagEvaluationService.ParseThresholds(x.ThresholdsJson), x.CreatedAt, x.UpdatedAt };
    private static object RunDto(RagEvaluationRun x) => new { x.Id, x.DatasetId, datasetName = x.Dataset?.Name, x.Status, x.TotalCases,
            x.CompletedCases, x.AttemptCount, x.DatasetVersion, runtimeSnapshot = Deserialize(x.RuntimeSnapshotJson),
            metrics = Deserialize(x.MetricsJson), results = Deserialize(x.ResultsJson), x.Error, x.CreatedAt, x.StartedAt, x.CompletedAt };
    private static object? Deserialize(string? value) => value == null ? null : JsonSerializer.Deserialize<JsonElement>(value, Json);
    private static string? Validate(SaveRagDatasetRequest x) => string.IsNullOrWhiteSpace(x.Name) ? "Name is required" : x.Cases.Count is < 1 or > 500 ? "Dataset must contain 1-500 cases" : x.Cases.Any(c => string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Question)) ? "Every case requires id and question" : x.Cases.Select(c => c.Id).Distinct().Count() != x.Cases.Count ? "Case ids must be unique" : null;
    private static void Apply(RagEvaluationDataset x, SaveRagDatasetRequest r) { x.Name = r.Name.Trim(); x.Description = r.Description?.Trim() ?? ""; x.Version = r.Version?.Trim() ?? "1.0.0"; x.CasesJson = JsonSerializer.Serialize(r.Cases, Json); x.ThresholdsJson = JsonSerializer.Serialize(r.Thresholds, Json); x.UpdatedAt = DateTime.UtcNow; }
}
