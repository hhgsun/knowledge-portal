using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

/// <summary>Shared tag resolution and creation used by articles and the tags endpoint.</summary>
public class TagService(AppDbContext db)
{
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
