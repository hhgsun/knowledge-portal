using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Helpers;

/// <summary>
/// Shared article query filters used by the REST API, search, and MCP tools.
/// Response projections stay endpoint-specific; only query building is shared.
/// </summary>
public static class ArticleQueryExtensions
{
    public static IQueryable<Article> WherePublished(this IQueryable<Article> query)
        => query.Where(a => a.Status == "published");

    /// <summary>AND logic: article must have ALL specified tags.</summary>
    public static IQueryable<Article> WhereHasAllTags(this IQueryable<Article> query, IEnumerable<string> tagSlugs)
    {
        foreach (var tagSlug in tagSlugs)
            query = query.Where(a => a.ArticleTags.Any(at => at.Tag.Slug == tagSlug));
        return query;
    }

    /// <summary>OR logic: article content type must be one of the given values.</summary>
    public static IQueryable<Article> WhereContentTypeIn(this IQueryable<Article> query, ICollection<string> contentTypes)
        => query.Where(a => contentTypes.Contains(a.ContentType));

    /// <summary>OR logic: article owner must be one of the given users.</summary>
    public static IQueryable<Article> WhereOwnedByAny(this IQueryable<Article> query, ICollection<string> ownerIds)
        => query.Where(a => ownerIds.Contains(a.OwnerId));

    /// <summary>Resolves author slugs to user IDs for owner filtering.</summary>
    public static Task<List<string>> ResolveAuthorIdsAsync(this AppDbContext db, ICollection<string> authorSlugs)
        => db.Users.Where(u => authorSlugs.Contains(u.Slug)).Select(u => u.Id).ToListAsync();

    /// <summary>
    /// Reorders loaded items to match a ranked ID list (e.g. FTS relevance order),
    /// dropping IDs that were filtered out of the loaded set.
    /// </summary>
    public static List<T> OrderByRankedIds<T>(IEnumerable<string> rankedIds, IReadOnlyCollection<T> items, Func<T, string> idSelector) where T : class
        => rankedIds
            .Select(id => items.FirstOrDefault(x => idSelector(x) == id))
            .Where(x => x != null)
            .Select(x => x!)
            .ToList();
}
