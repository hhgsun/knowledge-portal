using KnowledgePortal.Api.Helpers;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Data;

/// <summary>
/// Generates unique slugs by appending "-1", "-2", … until no collision remains.
/// </summary>
public static class SlugQueries
{
    public static async Task<string> GenerateUniqueArticleSlugAsync(this AppDbContext db, string title)
        => await MakeUniqueAsync(SlugHelper.GenerateArticleSlug(title), slug => db.Articles.AnyAsync(a => a.Slug == slug));

    public static async Task<string> GenerateUniqueArticleSlugAsync(this AppDbContext db, string title, string excludeArticleId)
        => await MakeUniqueAsync(SlugHelper.GenerateArticleSlug(title),
            slug => db.Articles.AnyAsync(a => a.Slug == slug && a.Id != excludeArticleId));

    public static async Task<string> GenerateUniqueUserSlugAsync(this AppDbContext db, string name)
        => await MakeUniqueAsync(SlugHelper.GenerateSlug(name), slug => db.Users.AnyAsync(u => u.Slug == slug));

    private static async Task<string> MakeUniqueAsync(string baseSlug, Func<string, Task<bool>> existsAsync)
    {
        var slug = baseSlug;
        var counter = 1;
        while (await existsAsync(slug))
            slug = $"{baseSlug}-{counter++}";
        return slug;
    }
}
