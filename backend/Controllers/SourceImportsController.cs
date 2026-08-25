using System.Text.Json;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/source-imports")]
[Authorize]
[RequirePermission(Permissions.ArticlesCreate)]
public class SourceImportsController(SourceImportService service) : ControllerBase
{
    [HttpPost("analyze")]
    [RequestSizeLimit(104_857_600)]
    public async Task<IActionResult> Analyze([FromForm] List<IFormFile> files, CancellationToken ct)
    {
        if (files.Count == 0) return BadRequest(new { error = "At least one source file is required" });
        var previews = new List<SourceImportPreview>();
        for (var i = 0; i < files.Count; i++) previews.Add(await service.AnalyzeAsync(files[i], i, ct));
        return Ok(new { drafts = previews });
    }

    [HttpPost("commit")]
    [RequestSizeLimit(104_857_600)]
    public async Task<IActionResult> Commit([FromForm] string manifest, [FromForm] List<IFormFile> files,
        [FromForm] List<IFormFile> attachments, CancellationToken ct)
    {
        SourceImportCommitRequest? request;
        try { request = JsonSerializer.Deserialize<SourceImportCommitRequest>(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
        catch (JsonException) { return BadRequest(new { error = "Invalid import manifest" }); }
        if (request?.Drafts is not { Length: > 0 }) return BadRequest(new { error = "At least one draft is required" });
        return Ok(await service.CommitAsync(request, files, attachments, User, ct));
    }
}
