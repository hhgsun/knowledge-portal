using System.Text.RegularExpressions;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
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

        var slug = TagSlugRegex().Replace(req.Name.ToLowerInvariant().Trim(), "-").Trim('-');

        var existing = await db.Tags.FirstOrDefaultAsync(t => t.Slug == slug);
        if (existing != null)
            return Ok(new { existing.Id, existing.Name, existing.Slug });

        var tag = new Tag { Name = req.Name.Trim(), Slug = slug };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();

        return StatusCode(201, new { tag.Id, tag.Name, tag.Slug });
    }

    [HttpDelete]
    [RequirePermission(Permissions.TagsManage)]
    public async Task<IActionResult> Delete([FromQuery] string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { error = "Tag id is required" });

        var tag = await db.Tags.FindAsync(id);
        if (tag == null) return NotFound(new { error = "Tag not found" });

        // Remove article-tag mappings
        var mappings = await db.ArticleTags.Where(at => at.TagId == id).ToListAsync();
        db.ArticleTags.RemoveRange(mappings);

        db.Tags.Remove(tag);
        await db.SaveChangesAsync();

        return Ok(new { message = "Tag deleted" });
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex TagSlugRegex();
}

public record CreateTagRequest(string Name);
