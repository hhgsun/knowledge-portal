using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/lookups")]
[Authorize]
public class LookupsController(AppDbContext db) : ControllerBase
{
    private static readonly HashSet<string> Cardinalities = ["single", "multiple"];
    private static readonly HashSet<string> RagBehaviors = ["none", "filter"];

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? category = null)
    {
        var query = db.LookupValues.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(value => value.Category == category);
        var results = await query.OrderBy(value => value.Category).ThenBy(value => value.SortOrder).ToListAsync();
        return Ok(results.Select(value => new
        {
            value.Id, value.Category, value.Value, value.Label, value.Color, value.Icon,
            value.SortOrder, value.AuthorityWeight, value.IsActive
        }));
    }

    [HttpGet("categories")]
    public async Task<IActionResult> ListCategories()
        => Ok(await db.LookupCategories.AsNoTracking()
            .OrderBy(category => category.SortOrder).ThenBy(category => category.Label)
            .Select(category => new
            {
                category.Id, category.Key, category.Label, category.Cardinality,
                category.IsRequired, category.DefaultValueId, category.RagBehavior,
                category.SortOrder, category.IsActive
            }).ToListAsync());

    [HttpPost("categories")]
    [RequirePermission(Permissions.TagsManage)]
    public async Task<IActionResult> CreateCategory(CreateLookupCategoryRequest request)
    {
        var key = ClassificationService.NormalizeKey(request.Key);
        var label = request.Label.Trim();
        var cardinality = request.Cardinality.Trim().ToLowerInvariant();
        var ragBehavior = string.IsNullOrWhiteSpace(request.RagBehavior)
            ? "filter"
            : request.RagBehavior.Trim().ToLowerInvariant();
        if (key.Length is < 1 or > 50 || label.Length is < 1 or > 100)
            return BadRequest(new { error = "Category key and label are required (max 50/100 chars)" });
        if (!Cardinalities.Contains(cardinality))
            return BadRequest(new { error = "cardinality must be single or multiple" });
        if (!RagBehaviors.Contains(ragBehavior))
            return BadRequest(new { error = "ragBehavior must be none or filter" });
        if (request.IsRequired)
            return BadRequest(new { error = "Create the category and a default value before making it required" });
        if (await db.LookupCategories.AnyAsync(category => category.Key == key))
            return Conflict(new { error = "A category with this key already exists" });

        var maxOrder = await db.LookupCategories.MaxAsync(category => (int?)category.SortOrder) ?? 0;
        var category = new LookupCategory
        {
            Key = key, Label = label, Cardinality = cardinality, RagBehavior = ragBehavior,
            SortOrder = request.SortOrder ?? maxOrder + 1
        };
        db.LookupCategories.Add(category);
        await db.SaveChangesAsync();
        return Created("/api/lookups/categories", Shape(category));
    }

    [HttpPut("categories")]
    [RequirePermission(Permissions.TagsManage)]
    public async Task<IActionResult> UpdateCategory(UpdateLookupCategoryRequest request)
    {
        var category = await db.LookupCategories.FindAsync(request.Id);
        if (category == null) return NotFound(new { error = "Lookup category not found" });
        if (request.Label != null)
        {
            var label = request.Label.Trim();
            if (label.Length is < 1 or > 100) return BadRequest(new { error = "label is required (max 100 chars)" });
            category.Label = label;
        }
        if (request.Cardinality != null)
        {
            var cardinality = request.Cardinality.Trim().ToLowerInvariant();
            if (!Cardinalities.Contains(cardinality))
                return BadRequest(new { error = "cardinality must be single or multiple" });
            if (category.Key == "content_type" && cardinality != "single")
                return BadRequest(new { error = "content_type must remain single-select" });
            if (cardinality == "single")
            {
                var hasMultiple = await db.ArticleLookupValues
                    .Where(value => value.LookupValue.Category == category.Key)
                    .GroupBy(value => value.ArticleId).AnyAsync(group => group.Count() > 1);
                if (hasMultiple)
                    return Conflict(new { error = "Some articles have multiple values in this category" });
            }
            category.Cardinality = cardinality;
        }
        if (request.RagBehavior != null)
        {
            var behavior = request.RagBehavior.Trim().ToLowerInvariant();
            if (!RagBehaviors.Contains(behavior))
                return BadRequest(new { error = "ragBehavior must be none or filter" });
            if (category.Key == "content_type" && behavior != "filter")
                return BadRequest(new { error = "content_type must remain an AI filter" });
            category.RagBehavior = behavior;
        }
        if (request.DefaultValueId != null)
        {
            var defaultValue = await db.LookupValues.FirstOrDefaultAsync(value =>
                value.Id == request.DefaultValueId && value.Category == category.Key && value.IsActive);
            if (defaultValue == null)
                return BadRequest(new { error = "Default value must be active and belong to the category" });
            if (category.Key == "content_type" && defaultValue.Value != ContentTypeService.DefaultValue)
                return BadRequest(new { error = "content_type default must remain reference" });
            category.DefaultValueId = defaultValue.Id;
        }
        if (request.IsRequired == true && category.DefaultValueId == null)
            return BadRequest(new { error = "A required category must have a default value" });
        if (category.Key == "content_type" && request.IsRequired == false)
            return BadRequest(new { error = "content_type must remain required" });
        if (request.IsRequired.HasValue) category.IsRequired = request.IsRequired.Value;
        if (request.SortOrder.HasValue) category.SortOrder = request.SortOrder.Value;
        if (request.IsActive.HasValue)
        {
            if (category.Key == "content_type" && !request.IsActive.Value)
                return BadRequest(new { error = "content_type cannot be deactivated" });
            category.IsActive = request.IsActive.Value;
        }

        if (category.IsRequired && category.DefaultValueId != null)
        {
            var articleIds = await db.Articles
                .Where(article => !article.ArticleLookupValues.Any(value =>
                    value.LookupValue.Category == category.Key))
                .Select(article => article.Id).ToListAsync();
            db.ArticleLookupValues.AddRange(articleIds.Select(articleId => new ArticleLookupValue
                { ArticleId = articleId, LookupValueId = category.DefaultValueId }));
        }
        await db.SaveChangesAsync();
        return Ok(Shape(category));
    }

    [HttpDelete("categories")]
    [RequirePermission(Permissions.TagsManage)]
    [RequireSessionAuth]
    public async Task<IActionResult> DeleteCategory([FromQuery] string id)
    {
        var category = await db.LookupCategories.FindAsync(id);
        if (category == null) return NotFound(new { error = "Lookup category not found" });
        if (category.Key == "content_type")
            return BadRequest(new { error = "content_type is required for backwards compatibility" });
        if (await db.LookupValues.AnyAsync(value => value.Category == category.Key))
            return Conflict(new { error = "Delete all category values first" });
        db.LookupCategories.Remove(category);
        await db.SaveChangesAsync();
        return Ok(new { message = "Lookup category deleted" });
    }

    [HttpPost]
    [RequirePermission(Permissions.TagsManage)]
    public async Task<IActionResult> Create(CreateLookupRequest request)
    {
        var categoryKey = ClassificationService.NormalizeKey(request.Category);
        var valueKey = request.Value.Trim().ToLowerInvariant();
        var label = request.Label.Trim();
        var category = await db.LookupCategories.FirstOrDefaultAsync(item => item.Key == categoryKey && item.IsActive);
        if (category == null) return BadRequest(new { error = "Unknown or inactive lookup category" });
        if (valueKey.Length is < 1 or > 100 || label.Length is < 1 or > 100)
            return BadRequest(new { error = "Value and label are required (max 100 chars)" });
        if (await db.LookupValues.AnyAsync(value => value.Category == categoryKey && value.Value == valueKey))
            return Conflict(new { error = "A lookup with this category and value already exists" });
        if (request.AuthorityWeight is < 0 or > 100)
            return BadRequest(new { error = "authorityWeight must be between 0 and 100" });

        var maxOrder = await db.LookupValues.Where(value => value.Category == categoryKey)
            .MaxAsync(value => (int?)value.SortOrder) ?? 0;
        var lookup = new LookupValue
        {
            Category = categoryKey, Value = valueKey, Label = label,
            Color = request.Color?.Trim(), Icon = request.Icon?.Trim(),
            SortOrder = request.SortOrder ?? maxOrder + 1,
            AuthorityWeight = categoryKey == "content_type" ? request.AuthorityWeight ?? 50 : 50
        };
        db.LookupValues.Add(lookup);
        await db.SaveChangesAsync();
        return Created($"/api/lookups/{lookup.Id}", Shape(lookup));
    }

    [HttpPut]
    [RequirePermission(Permissions.TagsManage)]
    public async Task<IActionResult> Update(UpdateLookupRequest request)
    {
        var lookup = await db.LookupValues.FindAsync(request.Id);
        if (lookup == null) return NotFound(new { error = "Lookup not found" });
        if (request.Label != null) lookup.Label = request.Label.Trim();
        if (request.Color != null) lookup.Color = request.Color.Trim();
        if (request.Icon != null) lookup.Icon = request.Icon.Trim();
        if (request.SortOrder.HasValue) lookup.SortOrder = request.SortOrder.Value;
        if (request.AuthorityWeight is < 0 or > 100)
            return BadRequest(new { error = "authorityWeight must be between 0 and 100" });
        if (request.AuthorityWeight.HasValue && lookup.Category == "content_type")
            lookup.AuthorityWeight = request.AuthorityWeight.Value;
        if (request.IsActive.HasValue)
        {
            var category = await db.LookupCategories.FirstAsync(item => item.Key == lookup.Category);
            if (!request.IsActive.Value && category.DefaultValueId == lookup.Id)
                return Conflict(new { error = "The category default value cannot be deactivated" });
            lookup.IsActive = request.IsActive.Value;
        }
        await db.SaveChangesAsync();
        return Ok(Shape(lookup));
    }

    [HttpDelete]
    [RequirePermission(Permissions.TagsManage)]
    [RequireSessionAuth]
    public async Task<IActionResult> Delete([FromQuery] string id)
    {
        var lookup = await db.LookupValues.FindAsync(id);
        if (lookup == null) return NotFound(new { error = "Lookup not found" });
        if (await db.ArticleLookupValues.AnyAsync(value => value.LookupValueId == id)
            || (lookup.Category == "content_type" && await db.Articles.AnyAsync(article => article.ContentType == lookup.Value)))
            return Conflict(new { error = "Cannot delete: this value is in use. Deactivate it instead." });
        if (await db.LookupCategories.AnyAsync(category => category.DefaultValueId == id))
            return Conflict(new { error = "Cannot delete a category default value" });
        db.LookupValues.Remove(lookup);
        await db.SaveChangesAsync();
        return Ok(new { message = "Lookup deleted" });
    }

    private static object Shape(LookupCategory category) => new
    {
        category.Id, category.Key, category.Label, category.Cardinality,
        category.IsRequired, category.DefaultValueId, category.RagBehavior,
        category.SortOrder, category.IsActive
    };

    private static object Shape(LookupValue value) => new
    {
        value.Id, value.Category, value.Value, value.Label, value.Color, value.Icon,
        value.SortOrder, value.AuthorityWeight, value.IsActive
    };
}
