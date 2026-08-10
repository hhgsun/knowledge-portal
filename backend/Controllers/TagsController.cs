using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/tags")]
[Authorize]
public class TagsController(AppDbContext db, TagService tagService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int? page = null,
        [FromQuery] int? limit = null,
        [FromQuery(Name = "q")] string? query = null,
        [FromQuery] string[]? ids = null,
        [FromQuery] string[]? slugs = null)
    {
        // Preserve the original array response for existing consumers.
        if (page is null && limit is null && query is null && (ids is null || ids.Length == 0) && (slugs is null || slugs.Length == 0))
            return Ok(await tagService.ListWithCountsAsync());

        var resolvedPage = page ?? 1;
        var resolvedLimit = limit ?? 30;
        if (resolvedPage < 1)
            return BadRequest(new { error = "Page must be at least 1" });
        if (resolvedLimit is < 1 or > 100)
            return BadRequest(new { error = "Limit must be between 1 and 100" });

        return Ok(await tagService.SearchWithCountsAsync(
            resolvedPage,
            resolvedLimit,
            query,
            ids?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray(),
            slugs?.Where(slug => !string.IsNullOrWhiteSpace(slug)).Distinct().ToArray()));
    }

    [HttpPost]
    [RequirePermission(Permissions.TagsManage)]
    public async Task<IActionResult> Create([FromBody] CreateTagRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 50)
            return BadRequest(new { error = "Name is required (1-50 chars)" });

        var (tag, created) = await tagService.FindOrCreateAsync(req.Name);
        return StatusCode(created ? 201 : 200, new { tag.Id, tag.Name, tag.Slug });
    }

    [HttpPut]
    [RequirePermission(Permissions.TagsManage)]
    public async Task<IActionResult> Update([FromBody] UpdateTagRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Id))
            return BadRequest(new { error = "Tag id is required" });

        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 50)
            return BadRequest(new { error = "Name is required (1-50 chars)" });

        var tag = await db.Tags.FindAsync(req.Id);
        if (tag == null) return NotFound(new { error = "Tag not found" });

        var newSlug = SlugHelper.GenerateTagSlug(req.Name);

        var existing = await db.Tags.FirstOrDefaultAsync(t => t.Slug == newSlug && t.Id != req.Id);
        if (existing != null)
            return Conflict(new { error = "A tag with this name already exists" });

        tag.Name = req.Name.Trim();
        tag.Slug = newSlug;
        await db.SaveChangesAsync();

        return Ok(new { tag.Id, tag.Name, tag.Slug });
    }

    [HttpDelete]
    [RequirePermission(Permissions.TagsManage)]
    [RequireSessionAuth] // destructive deletes are session-only — API keys cannot delete
    public async Task<IActionResult> Delete([FromQuery] string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { error = "Tag id is required" });

        var tag = await db.Tags.FindAsync(id);
        if (tag == null) return NotFound(new { error = "Tag not found" });

        var articleCount = await db.ArticleTags.CountAsync(at => at.TagId == id);
        if (articleCount > 0)
            return Conflict(new { error = $"Tag cannot be deleted because it is used by {articleCount} article(s)" });

        db.Tags.Remove(tag);
        await db.SaveChangesAsync();

        return Ok(new { message = "Tag deleted" });
    }
}

