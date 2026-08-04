using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/bulk")]
[Authorize]
public class BulkTransferController(AppDbContext db, BulkTransferService service) : ControllerBase
{
    [HttpGet("templates/{format}")]
    public IActionResult DownloadTemplate(string format)
    {
        if (format.Equals("jsonl", StringComparison.OrdinalIgnoreCase))
            return File(BulkTransferService.CreateJsonLinesTemplate(), "application/x-ndjson", "article-import-template.jsonl");
        if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
            return File(BulkTransferService.CreateCsvTemplate(), "text/csv; charset=utf-8", "article-import-template.csv");
        return BadRequest(new { error = "format must be jsonl or csv" });
    }

    [HttpGet("import-schema")]
    public async Task<IActionResult> GetImportSchema(CancellationToken ct)
    {
        var contentTypes = await db.LookupValues
            .Where(x => x.Category == "content_type" && x.IsActive)
            .OrderBy(x => x.SortOrder)
            .Select(x => new { x.Value, x.Label })
            .ToListAsync(ct);
        return Ok(new
        {
            maxRecords = BulkTransferService.MaxRecords,
            maxFileSizeMb = BulkTransferService.MaxFileSizeMb,
            statuses = new[] { "draft", "pending", "published", "archived" },
            contentTypes,
            conflictPolicies = new[] { "skip", "update", "duplicate" },
            attachmentsIncluded = false,
            fields = new object[]
            {
                new { name = "title", required = true, description = "Article title, 1–300 characters." },
                new { name = "externalId", required = false, description = "Optional source-system identifier." },
                new { name = "excerpt", required = false, description = "Short article summary." },
                new { name = "status", required = false, description = "Article lifecycle status; defaults to draft." },
                new { name = "contentType", required = false, description = "An active content type value." },
                new { name = "tags", required = false, description = "JSON array in JSONL; pipe-separated values in CSV." },
                new { name = "content", required = false, description = "TipTap JSON; CSV also accepts plain text." }
            }
        });
    }

    [HttpPost("import")]
    [RequirePermission(Permissions.ArticlesCreate)]
    [RequestSizeLimit(104_857_600)]
    public async Task<IActionResult> Import(IFormFile file, [FromForm] bool dryRun = false,
        [FromForm] string conflictPolicy = "skip", CancellationToken ct = default)
    {
        if (file.Length == 0) return BadRequest(new { error = "File is empty" });
        try
        {
            await using var stream = file.OpenReadStream();
            var items = await service.ReadAsync(stream, file.FileName, ct);
            var result = await service.ImportAsync(items, User, dryRun, conflictPolicy.ToLowerInvariant(), ct);
            return Ok(result);
        }
        catch (InvalidDataException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string format = "jsonl", [FromQuery] string? status = null,
        [FromQuery] string? contentType = null, [FromQuery] string? authorId = null,
        [FromQuery] string? tag = null, [FromQuery] string? dateFrom = null,
        [FromQuery] string? dateTo = null, [FromQuery] bool mine = false, CancellationToken ct = default)
    {
        var query = db.Articles.AsQueryable();
        var userId = User.GetUserId();
        if (User.GetRole() == "viewer") query = query.Where(a => a.Status == "published" || a.OwnerId == userId);
        if (mine) query = query.Where(a => a.OwnerId == userId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(a => a.Status == status);
        if (!string.IsNullOrWhiteSpace(contentType)) query = query.Where(a => a.ContentType == contentType);
        if (!string.IsNullOrWhiteSpace(authorId)) query = query.Where(a => a.OwnerId == authorId);
        if (!string.IsNullOrWhiteSpace(tag)) query = query.Where(a => a.ArticleTags.Any(at => at.Tag.Slug == tag));
        if (!string.IsNullOrWhiteSpace(dateFrom))
        {
            if (!DateTime.TryParse(dateFrom, out var from)) return BadRequest(new { error = "dateFrom must be a valid date" });
            query = query.Where(a => a.UpdatedAt >= from.ToUniversalTime());
        }
        if (!string.IsNullOrWhiteSpace(dateTo))
        {
            if (!DateTime.TryParse(dateTo, out var to)) return BadRequest(new { error = "dateTo must be a valid date" });
            query = query.Where(a => a.UpdatedAt < to.ToUniversalTime().AddDays(1));
        }

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
            return File(await service.ExportCsvAsync(query, ct), "text/csv; charset=utf-8", $"knowledge-portal-{stamp}.csv");
        if (!format.Equals("jsonl", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "format must be jsonl or csv" });
        return File(await service.ExportJsonLinesAsync(query, ct), "application/x-ndjson", $"knowledge-portal-{stamp}.jsonl");
    }
}
