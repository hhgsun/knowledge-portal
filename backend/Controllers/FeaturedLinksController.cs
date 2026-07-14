using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/featured-links")]
[Authorize]
public class FeaturedLinksController(AppDbContext db) : ControllerBase
{
    private static readonly string[] AllowedLinkTypes = ["content_type", "tag", "custom"];

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeInactive = false)
    {
        var query = db.FeaturedLinks.AsQueryable();

        if (!includeInactive)
            query = query.Where(f => f.IsActive);

        var results = await query
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.CreatedAt)
            .Select(f => new FeaturedLinkDto(f.Id, f.Label, f.LinkType, f.Target, f.Icon, f.Color, f.SortOrder, f.IsActive))
            .ToListAsync();

        return Ok(results);
    }

    [HttpPost]
    [RequirePermission(Permissions.FeaturedLinksManage)]
    public async Task<IActionResult> Create([FromBody] CreateFeaturedLinkRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Label) || string.IsNullOrWhiteSpace(req.LinkType) || string.IsNullOrWhiteSpace(req.Target))
            return BadRequest(new { error = "Label, linkType, and target are required" });

        if (!AllowedLinkTypes.Contains(req.LinkType))
            return BadRequest(new { error = $"Invalid linkType. Allowed: {string.Join(", ", AllowedLinkTypes)}" });

        var target = req.Target.Trim();
        var validationError = await ValidateTargetAsync(req.LinkType, target);
        if (validationError != null)
            return BadRequest(new { error = validationError });

        var maxOrder = await db.FeaturedLinks.MaxAsync(f => (int?)f.SortOrder) ?? 0;

        var link = new FeaturedLink
        {
            Label = req.Label.Trim(),
            LinkType = req.LinkType,
            Target = target,
            Icon = req.Icon?.Trim(),
            Color = string.IsNullOrWhiteSpace(req.Color) ? null : req.Color.Trim(),
            SortOrder = req.SortOrder ?? (maxOrder + 1),
            IsActive = true
        };

        db.FeaturedLinks.Add(link);
        await db.SaveChangesAsync();

        return Created($"/api/featured-links/{link.Id}",
            new FeaturedLinkDto(link.Id, link.Label, link.LinkType, link.Target, link.Icon, link.Color, link.SortOrder, link.IsActive));
    }

    [HttpPut]
    [RequirePermission(Permissions.FeaturedLinksManage)]
    public async Task<IActionResult> Update([FromBody] UpdateFeaturedLinkRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Id))
            return BadRequest(new { error = "Id is required" });

        var link = await db.FeaturedLinks.FindAsync(req.Id);
        if (link == null)
            return NotFound(new { error = "Featured link not found" });

        if (!string.IsNullOrWhiteSpace(req.Target))
        {
            var target = req.Target.Trim();
            var validationError = await ValidateTargetAsync(link.LinkType, target);
            if (validationError != null)
                return BadRequest(new { error = validationError });
            link.Target = target;
        }

        if (!string.IsNullOrWhiteSpace(req.Label)) link.Label = req.Label.Trim();
        if (req.Icon != null) link.Icon = req.Icon.Trim();
        if (req.Color != null) link.Color = string.IsNullOrWhiteSpace(req.Color) ? null : req.Color.Trim();
        if (req.SortOrder.HasValue) link.SortOrder = req.SortOrder.Value;
        if (req.IsActive.HasValue) link.IsActive = req.IsActive.Value;

        await db.SaveChangesAsync();

        return Ok(new FeaturedLinkDto(link.Id, link.Label, link.LinkType, link.Target, link.Icon, link.Color, link.SortOrder, link.IsActive));
    }

    [HttpDelete]
    [RequirePermission(Permissions.FeaturedLinksManage)]
    public async Task<IActionResult> Delete([FromQuery] string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { error = "Id is required" });

        var link = await db.FeaturedLinks.FindAsync(id);
        if (link == null)
            return NotFound(new { error = "Featured link not found" });

        db.FeaturedLinks.Remove(link);
        await db.SaveChangesAsync();

        return Ok(new { message = "Featured link deleted" });
    }

    private async Task<string?> ValidateTargetAsync(string linkType, string target)
    {
        switch (linkType)
        {
            case "content_type":
                var typeExists = await db.LookupValues.AnyAsync(l => l.Category == "content_type" && l.Value == target);
                if (!typeExists) return "Target content type does not exist";
                break;
            case "tag":
                var tagExists = await db.Tags.AnyAsync(t => t.Slug == target);
                if (!tagExists) return "Target tag does not exist";
                break;
            case "custom":
                var isValid = target.StartsWith('/')
                    || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
                if (!isValid) return "Custom target must be an internal path (/...) or an http(s) URL";
                break;
        }
        return null;
    }
}
