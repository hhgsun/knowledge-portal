using System.Text;
using KnowledgePortal.Api.Services;

namespace KnowledgePortal.Api.Helpers;

/// <summary>
/// SQL twin of <see cref="ArticleService.ApplyFilter"/>. The search paths (full-text and
/// vector) are hand-written SQL because they rank inside the database, so they cannot compose
/// an <see cref="IQueryable{T}"/> — but the filters must still be part of the same statement.
/// Filtering after a ranked LIMIT is what makes a filtered search return nothing while real
/// matches sit just below the cut.
/// Keep this in sync with ApplyFilter: a filter that exists only in the LINQ version would
/// silently stop applying to search.
/// Every value enters through a positional placeholder, so nothing user-supplied is ever
/// concatenated into the statement.
/// </summary>
public static class ArticleFilterSql
{
    /// <summary>
    /// Renders <paramref name="filter"/> as WHERE predicates (each one prefixed with " AND ")
    /// against <paramref name="alias"/>, appending the bound values to <paramref name="args"/>.
    /// </summary>
    /// <param name="alias">Alias the articles table carries in the target query.</param>
    public static string Build(ArticleFilter? filter, List<object> args, string alias = "a")
    {
        if (filter == null) return "";
        var sb = new StringBuilder();

        if (filter.OwnerIds is { Count: > 0 })
            sb.Append($""" AND {alias}."OwnerId" = ANY({Placeholder(args, filter.OwnerIds.ToArray())})""");
        if (filter.ContentTypes is { Count: > 0 })
            sb.Append($""" AND {alias}."ContentType" = ANY({Placeholder(args, filter.ContentTypes.ToArray())})""");
        if (!string.IsNullOrWhiteSpace(filter.ApiKeyId))
            sb.Append($""" AND {alias}."CreatedViaApiKeyId" = {Placeholder(args, filter.ApiKeyId)}""");
        // Matches ApplyFilter: a non-null but empty ID list means "nothing matches"
        if (filter.ArticleIds != null)
            sb.Append($""" AND {alias}."Id" = ANY({Placeholder(args, filter.ArticleIds.ToArray())})""");
        // AND logic: one EXISTS per slug, so the article must carry every requested tag
        foreach (var slug in filter.TagSlugs ?? [])
            sb.Append($"""
                 AND EXISTS (SELECT 1 FROM article_tags ats JOIN tags tg ON tg."Id" = ats."TagId"
                             WHERE ats."ArticleId" = {alias}."Id" AND tg."Slug" = {Placeholder(args, slug)})
                """);

        return sb.ToString();
    }

    /// <summary>Appends a value and returns the positional placeholder (<c>{n}</c>) EF binds it to.</summary>
    public static string Placeholder(List<object> args, object value)
    {
        args.Add(value);
        return $"{{{args.Count - 1}}}";
    }
}
