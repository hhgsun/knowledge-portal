using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/lookups")]
[Authorize]
public class LookupsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? category = null)
    {
        var query = db.LookupValues.Where(l => true);

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(l => l.Category == category);

        var results = await query
            .OrderBy(l => l.Category)
            .ThenBy(l => l.SortOrder)
            .Select(l => new { l.Id, l.Category, l.Value, l.Label, l.Color, l.Icon, l.SortOrder, l.IsActive })
            .ToListAsync();

        return Ok(results);
    }

    [HttpPost]
    [RequirePermission(Permissions.TagsManage)]
    public async Task<IActionResult> Create([FromBody] CreateLookupRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Category) || string.IsNullOrWhiteSpace(req.Value) || string.IsNullOrWhiteSpace(req.Label))
            return BadRequest(new { error = "Category, value, and label are required" });

        var allowedCategories = new[] { "content_type" };
        if (!allowedCategories.Contains(req.Category))
            return BadRequest(new { error = $"Invalid category. Allowed: {string.Join(", ", allowedCategories)}" });

        var exists = await db.LookupValues.AnyAsync(l => l.Category == req.Category && l.Value == req.Value);
        if (exists)
            return Conflict(new { error = "A lookup with this category and value already exists" });

        var maxOrder = await db.LookupValues
            .Where(l => l.Category == req.Category)
            .MaxAsync(l => (int?)l.SortOrder) ?? 0;

        var lookup = new LookupValue
        {
            Category = req.Category,
            Value = req.Value.Trim().ToLowerInvariant(),
            Label = req.Label.Trim(),
            Color = req.Color?.Trim(),
            Icon = req.Icon?.Trim(),
            SortOrder = req.SortOrder ?? (maxOrder + 1),
            IsActive = true
        };

        db.LookupValues.Add(lookup);
        await db.SaveChangesAsync();

        return Created($"/api/lookups/{lookup.Id}", new { lookup.Id, lookup.Category, lookup.Value, lookup.Label, lookup.Color, lookup.Icon, lookup.SortOrder });
    }

    [HttpPut]
    [RequirePermission(Permissions.TagsManage)]
    public async Task<IActionResult> Update([FromBody] UpdateLookupRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Id))
            return BadRequest(new { error = "Id is required" });

        var lookup = await db.LookupValues.FindAsync(req.Id);
        if (lookup == null)
            return NotFound(new { error = "Lookup not found" });

        if (req.Label != null) lookup.Label = req.Label.Trim();
        if (req.Color != null) lookup.Color = req.Color.Trim();
        if (req.Icon != null) lookup.Icon = req.Icon.Trim();
        if (req.SortOrder.HasValue) lookup.SortOrder = req.SortOrder.Value;
        if (req.IsActive.HasValue) lookup.IsActive = req.IsActive.Value;

        await db.SaveChangesAsync();

        return Ok(new { lookup.Id, lookup.Category, lookup.Value, lookup.Label, lookup.Color, lookup.Icon, lookup.SortOrder, lookup.IsActive });
    }

    [HttpDelete]
    [RequirePermission(Permissions.TagsManage)]
    public async Task<IActionResult> Delete([FromQuery] string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { error = "Id is required" });

        var lookup = await db.LookupValues.FindAsync(id);
        if (lookup == null)
            return NotFound(new { error = "Lookup not found" });

        // Check if any articles use this value
        bool inUse = false;
        if (lookup.Category == "content_type")
            inUse = await db.Articles.AnyAsync(a => a.ContentType == lookup.Value);

        if (inUse)
            return Conflict(new { error = "Cannot delete: this value is in use by existing articles. Deactivate it instead." });

        db.LookupValues.Remove(lookup);
        await db.SaveChangesAsync();

        return Ok(new { message = "Lookup deleted" });
    }
}

public record CreateLookupRequest(string Category, string Value, string Label, string? Color = null, string? Icon = null, int? SortOrder = null);
public record UpdateLookupRequest(string Id, string? Label = null, string? Color = null, string? Icon = null, int? SortOrder = null, bool? IsActive = null);
