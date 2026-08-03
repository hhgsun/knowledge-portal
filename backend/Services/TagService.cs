using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

/// <summary>Shared tag resolution, creation, and listing used by articles, the tags endpoint, and MCP.</summary>
public class TagService(AppDbContext db)
{
    /// <summary>Lists all tags with article counts, optionally counting only published articles.</summary>
    public async Task<List<TagWithCountDto>> ListWithCountsAsync(bool publishedOnly = false)
    {
        var rows = await db.Tags
            .OrderBy(t => t.Name)
            .Select(t => new
            {
                t.Id, t.Name, t.Slug,
                Count = publishedOnly
                    ? t.ArticleTags.Count(at => at.Article.Status == "published")
                    : t.ArticleTags.Count
            })
            .ToListAsync();

        return rows.Select(r => new TagWithCountDto(r.Id, r.Name, r.Slug, r.Count)).ToList();
    }

    /// <summary>Searches and pages tags for asynchronous selectors.</summary>
    public async Task<TagListResponse> SearchWithCountsAsync(
        int page,
        int limit,
        string? query = null,
        IReadOnlyCollection<string>? ids = null,
        bool publishedOnly = false)
    {
        var tags = db.Tags.AsNoTracking().AsQueryable();

        if (ids is { Count: > 0 })
            tags = tags.Where(t => ids.Contains(t.Id));

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim().ToLower();
            var slugTerm = SlugHelper.GenerateTagSlug(query.Trim());
            tags = tags.Where(t => t.Name.ToLower().Contains(term) || t.Slug.Contains(slugTerm));
        }

        var total = await tags.CountAsync();
        var rows = await tags
            .OrderBy(t => t.Name)
            .ThenBy(t => t.Id)
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(t => new
            {
                t.Id, t.Name, t.Slug,
                Count = publishedOnly
                    ? t.ArticleTags.Count(at => at.Article.Status == "published")
                    : t.ArticleTags.Count
            })
            .ToListAsync();

        var result = rows.Select(r => new TagWithCountDto(r.Id, r.Name, r.Slug, r.Count)).ToList();
        return new TagListResponse(result, total, page, (int)Math.Ceiling(total / (double)limit));
    }

    /// <summary>Resolves the input as a tag ID, name, or slug.</summary>
    public async Task<Tag?> ResolveAsync(string input)
        => await db.Tags.FirstOrDefaultAsync(t => t.Id == input)
           ?? await db.Tags.FirstOrDefaultAsync(t => t.Name == input || t.Slug == input);

    /// <summary>Returns the existing tag with the same slug, or creates and persists a new one.</summary>
    public async Task<(Tag Tag, bool Created)> FindOrCreateAsync(string name)
    {
        var slug = SlugHelper.GenerateTagSlug(name);
        var existing = await db.Tags.FirstOrDefaultAsync(t => t.Slug == slug);
        if (existing != null) return (existing, false);

        var tag = new Tag { Name = name.Trim(), Slug = slug };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        return (tag, true);
    }
}
