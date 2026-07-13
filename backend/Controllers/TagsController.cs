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
    public async Task<IActionResult> List()
        => Ok(await tagService.ListWithCountsAsync());

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

