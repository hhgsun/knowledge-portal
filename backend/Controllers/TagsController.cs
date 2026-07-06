using System.Text.RegularExpressions;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
using KnowledgePortal.Api.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/tags")]
[Authorize]
public partial class TagsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var tags = await db.Tags
            .Select(t => new
            {
                t.Id, t.Name, t.Slug,
                articleCount = t.ArticleTags.Count
            })
            .OrderBy(t => t.Name)
            .ToListAsync();

        return Ok(tags);
    }

    [HttpPost]
    [RequirePermission(Permissions.TagsManage)]
    public async Task<IActionResult> Create([FromBody] CreateTagRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 50)
            return BadRequest(new { error = "Name is required (1-50 chars)" });

        var slug = TagSlugRegex().Replace(SlugHelper.Transliterate(req.Name.ToLowerInvariant().Trim()), "-").Trim('-');

        var existing = await db.Tags.FirstOrDefaultAsync(t => t.Slug == slug);
        if (existing != null)
            return Ok(new { existing.Id, existing.Name, existing.Slug });

        var tag = new Tag { Name = req.Name.Trim(), Slug = slug };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();

        return StatusCode(201, new { tag.Id, tag.Name, tag.Slug });
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

        var newSlug = TagSlugRegex().Replace(SlugHelper.Transliterate(req.Name.ToLowerInvariant().Trim()), "-").Trim('-');

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

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex TagSlugRegex();

}

