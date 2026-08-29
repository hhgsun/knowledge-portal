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
    /// <param name="alias">Alias the target table carries in the query.</param>
    /// <param name="idColumn">Column holding the article id — "id" on articles, "article_id" on
    /// article_embeddings.</param>
    /// <param name="tagArrayColumn">Set when the target carries a denormalized text[] of tag
    /// slugs (article_embeddings), which turns the whole tag filter into one containment test.
    /// Left null for articles, where tags have to be reached through article_tags.</param>
    public static string Build(ArticleFilter? filter, List<object> args, string alias = "a",
                               string idColumn = "id", string? tagArrayColumn = null)
    {
        if (filter == null) return "";
        var sb = new StringBuilder();

        // Preserve an explicitly empty author set as a no-match filter.
        if (filter.OwnerIds is not null)
            sb.Append($""" AND {alias}.owner_id = ANY({Placeholder(args, filter.OwnerIds.ToArray())})""");
        if (filter.ContentTypes is { Count: > 0 })
            sb.Append($""" AND {alias}.content_type = ANY({Placeholder(args, filter.ContentTypes.ToArray())})""");
        if (!string.IsNullOrWhiteSpace(filter.ApiKeyId))
            sb.Append($""" AND {alias}.created_via_api_key_id = {Placeholder(args, filter.ApiKeyId)}""");
        // Matches ApplyFilter: a non-null but empty ID list means "nothing matches"
        if (filter.ArticleIds != null)
            sb.Append($""" AND {alias}.{idColumn} = ANY({Placeholder(args, filter.ArticleIds.ToArray())})""");

        var tagSlugs = (filter.TagSlugs ?? []).ToArray();
        if (tagSlugs.Length > 0)
        {
            if (tagArrayColumn != null)
                // @> is "contains all of", which is exactly the AND semantics — and it stays a
                // plain filter on the row, so a vector scan keeps its index-ordered plan.
                sb.Append($""" AND {alias}.{tagArrayColumn} @> {Placeholder(args, tagSlugs)}""");
            else
                // AND logic: one EXISTS per slug, so the article must carry every requested tag
                foreach (var slug in tagSlugs)
                    sb.Append($"""
                         AND EXISTS (SELECT 1 FROM article_tags ats JOIN tags tg ON tg.id = ats.tag_id
                                     WHERE ats.article_id = {alias}.{idColumn} AND tg.slug = {Placeholder(args, slug)})
                        """);
        }

        // One EXISTS per category gives AND semantics between classification dimensions;
        // values inside a category use OR semantics. The same predicate works against both
        // articles (id) and article_embeddings (article_id) without changing the base schema.
        foreach (var (category, values) in filter.Facets ??
            new Dictionary<string, string[]>())
        {
            if (values.Length == 0) continue;
            sb.Append($"""
                 AND EXISTS (SELECT 1 FROM article_lookup_values alv
                             JOIN lookup_values lv ON lv.id = alv.lookup_value_id
                             WHERE alv.article_id = {alias}.{idColumn}
                               AND lv.category = {Placeholder(args, category)}
                               AND lv.value = ANY({Placeholder(args, values)}))
                """);
        }

        return sb.ToString();
    }

    /// <summary>Appends a value and returns the positional placeholder (<c>{n}</c>) EF binds it to.</summary>
    public static string Placeholder(List<object> args, object value)
    {
        args.Add(value);
        return $"{{{args.Count - 1}}}";
    }
}
