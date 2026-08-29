using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/search")]
[Authorize]
[EnableRateLimiting("search")]
public class SearchController(AppDbContext db, IConfiguration config,
    SearchExecutionService searchExecution) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? q, [FromQuery] string type = "fulltext",
        [FromQuery] int limit = 20, [FromQuery] int page = 1,
        [FromQuery] bool onlyOwnContent = false, [FromQuery] bool includeContent = false,
        [FromQuery] bool includeAttachments = false, [FromQuery] List<string>? tag = null,
        [FromQuery] List<string>? author = null, [FromQuery] List<string>? contentType = null,
        [FromQuery] List<string>? facet = null)
    {
        var execution = await searchExecution.ExecuteAsync(
            new PortalSearchRequest(q ?? "", type, limit, page, onlyOwnContent,
                includeContent, includeAttachments, tag, author, contentType,
                ClassificationService.ParseFacetPairs(facet)),
            User, HttpContext.RequestAborted);
        if (execution.Error != null) return execution.Error.ToActionResult();

        var result = execution.Result!;
        if (result.Failure != SearchFailureKind.None) return FailureResult(result);

        return Ok(new
        {
            results = result.Results,
            result.Query, result.Type, result.Tags, result.ResponseTimeMs,
            result.Total, result.Page, result.TotalPages, result.IndexingPending,
            result.IndexCoverage, result.SearchQueryId, result.Warning
        });
    }

    private IActionResult FailureResult(PortalSearchResult result)
    {
        var payload = new
        {
            results = result.Results,
            result.Query, result.Type, result.ResponseTimeMs, result.Total,
            result.Page, result.TotalPages, result.IndexingPending,
            result.IndexCoverage, result.SearchQueryId, result.Warning
        };
        return Ok(payload);
    }

    [HttpPost("click")]
    public async Task<IActionResult> RecordClick([FromBody] RecordClickRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.SearchQueryId) || string.IsNullOrWhiteSpace(req.ArticleId))
            return BadRequest(new { error = "searchQueryId and articleId are required" });
        var searchQuery = await db.SearchQueries.FindAsync(req.SearchQueryId);
        if (searchQuery == null) return NotFound(new { error = "Search query not found" });
        if (searchQuery.UserId != User.GetUserId())
            return StatusCode(403, new { error = "Cannot update another user's search query" });
        searchQuery.ClickedArticleId = req.ArticleId;
        await db.SaveChangesAsync();
        return Ok(new { message = "Click recorded" });
    }

    [HttpPost("reindex")]
    [RequirePermission(Permissions.UsersManage)]
    [RequireSessionAuth]
    public async Task<IActionResult> Reindex()
    {
        if (!config.GetValue("Ollama:Enabled", false))
            return StatusCode(503, new { error = "Ollama is not enabled" });
        await db.ArticleEmbeddings.Where(embedding => embedding.ChunkIndex == 0)
            .ExecuteUpdateAsync(setters => setters.SetProperty(embedding => embedding.TextHash, ""));
        var count = await db.Articles.WherePublished()
            .ExecuteUpdateAsync(setters => setters.SetProperty(article => article.IndexedAt, (DateTime?)null));
        await HttpContext.RequestServices.GetRequiredService<IndexJobQueue>()
            .BackfillDirtyArticlesAsync(HttpContext.RequestAborted);
        return Ok(new { message = "Reindex queued", articlesQueued = count });
    }

    [HttpPost("repair-indexing")]
    [RequirePermission(Permissions.UsersManage)]
    [RequireSessionAuth]
    public async Task<IActionResult> RepairIndexing(CancellationToken ct)
    {
        var repaired = await HttpContext.RequestServices.GetRequiredService<IndexJobQueue>()
            .RepairDirtyArticlesAsync(ct);
        var pending = await db.IndexJobs.CountAsync(
            job => job.Status == "pending" || job.Status == "processing", ct);
        return Ok(new
        {
            message = repaired > 0 ? "Missing or stuck index jobs repaired" : "No repairable index jobs found",
            articlesRepaired = repaired, pendingCount = pending
        });
    }

    [HttpGet("diagnostics")]
    [RequirePermission(Permissions.UsersManage)]
    [RequireSessionAuth]
    public async Task<IActionResult> Diagnostics([FromServices] SearchDiagnosticsService diagnostics, CancellationToken ct)
        => Ok(await diagnostics.CollectAsync(ct));

    [HttpGet("storage-status")]
    [RequirePermission(Permissions.UsersManage)]
    [RequireSessionAuth]
    public async Task<IActionResult> StorageStatus([FromServices] AttachmentStorageService storage, CancellationToken ct)
        => Ok(await storage.CollectHealthAsync(ct));

    [HttpGet("embedding-status")]
    [RequirePermission(Permissions.UsersManage)]
    [RequireSessionAuth]
    public async Task<IActionResult> EmbeddingStatus()
    {
        var totalPublished = await db.Articles.CountAsync(article => article.Status == "published");
        var semanticEnabled = config.GetValue("Ollama:Enabled", false);
        var totalFtsIndexed = await db.Articles.CountAsync(article => article.Status == "published" && article.FtsIndexedAt != null);
        var totalSemanticIndexed = semanticEnabled
            ? await db.Articles.CountAsync(article => article.Status == "published" && article.IndexedAt != null) : 0;
        var failedJobs = await db.IndexJobs.Where(job => job.Status == "failed")
            .OrderByDescending(job => job.AttemptCount).Take(20).ToListAsync();
        return Ok(new
        {
            totalPublished, totalIndexed = semanticEnabled ? totalSemanticIndexed : totalFtsIndexed,
            totalFtsIndexed, totalSemanticIndexed,
            pendingCount = await db.IndexJobs.CountAsync(job => job.Status == "pending" || job.Status == "processing"),
            failedCount = await db.IndexJobs.CountAsync(job => job.Status == "failed"),
            ollamaEnabled = semanticEnabled,
            modelName = config["Ollama:EmbeddingModel"] ?? "bge-m3",
            configuredDimensions = config.GetValue("Ollama:EmbeddingDimensions", 1024),
            chunkingVersion = config["Ollama:ChunkingVersion"] ?? "hierarchical-parent-child-v2",
            semanticIndexProfile = EmbeddingService.ComputeIndexProfile(config),
            failedArticles = failedJobs.Select(job => new
            {
                articleId = job.ArticleId, failureCount = job.AttemptCount,
                nextRetryAt = job.AvailableAt.ToString("o"), error = job.LastError
            }).ToList()
        });
    }

    [HttpGet("authors")]
    public async Task<IActionResult> Authors()
        => Ok(await db.Users.Select(user => new { user.Id, user.Name, user.Slug })
            .OrderBy(user => user.Name).ToListAsync());
}
