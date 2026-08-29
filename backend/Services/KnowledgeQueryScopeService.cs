using System.Security.Claims;
using System.Text.RegularExpressions;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public sealed record KnowledgeQueryScopeRequest(
    string Query,
    bool OnlyOwnContent = false,
    IEnumerable<string>? Tags = null,
    IEnumerable<string>? Authors = null,
    IEnumerable<string>? ContentTypes = null,
    IReadOnlyDictionary<string, string[]>? Facets = null);

public sealed record KnowledgeQueryScope(
    string QueryText,
    ArticleFilter Filter,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> ContentTypes,
    IReadOnlyDictionary<string, string[]> Facets,
    bool HasUnknownTags,
    bool HasUnknownFacets);

/// <summary>
/// Resolves the query syntax and ACL-aware filters shared by document search and
/// grounded knowledge answering. Retrieval consumers stay separate while their
/// interpretation of scope remains identical.
/// </summary>
public sealed partial class KnowledgeQueryScopeService(AppDbContext db, ClassificationService classifications)
{
    public async Task<KnowledgeQueryScope> ResolveAsync(
        KnowledgeQueryScopeRequest request,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        var parsed = Parse(request.Query);
        var requestedTags = Merge(parsed.Tags, request.Tags);
        var requestedAuthors = Merge(parsed.Authors, request.Authors);
        var requestedContentTypes = Merge(parsed.ContentTypes, request.ContentTypes);
        var requestedFacets = MergeFacets(parsed.Facets, request.Facets);
        var resolvedFacets = await classifications.ResolveFacetFiltersAsync(requestedFacets, cancellationToken);

        var resolvedTags = requestedTags.Count == 0
            ? []
            : await db.Tags
                .AsNoTracking()
                .Where(tag => requestedTags.Contains(tag.Slug))
                .Select(tag => tag.Slug)
                .ToListAsync(cancellationToken);

        var authorIds = requestedAuthors.Count == 0
            ? []
            : await db.Users
                .AsNoTracking()
                .Where(user => requestedAuthors.Contains(user.Slug))
                .Select(user => user.Id)
                .ToListAsync(cancellationToken);

        var filter = new ArticleFilter(
            OwnerIds: requestedAuthors.Count > 0 ? authorIds : null,
            ContentTypes: requestedContentTypes.Count > 0 ? requestedContentTypes.ToList() : null,
            ApiKeyId: request.OnlyOwnContent ? principal.GetApiKeyId() : null,
            TagSlugs: requestedTags.Count > 0 ? resolvedTags : null,
            Facets: requestedFacets.Count > 0 ? resolvedFacets.Facets : null);

        return new KnowledgeQueryScope(
            parsed.Text,
            filter,
            requestedTags,
            requestedAuthors,
            requestedContentTypes,
            resolvedFacets.Facets,
            requestedTags.Count != resolvedTags.Count,
            resolvedFacets.HasUnknown);
    }

    private static IReadOnlyList<string> Merge(
        IReadOnlyList<string> inlineValues,
        IEnumerable<string>? explicitValues)
        => inlineValues
            .Concat(explicitValues ?? [])
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(value => value.Trim().ToLowerInvariant())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static ParsedKnowledgeQuery Parse(string rawQuery)
    {
        var tags = TagPattern().Matches(rawQuery).Select(match => match.Groups[1].Value).ToArray();
        var authors = AuthorPattern().Matches(rawQuery).Select(match => match.Groups[1].Value).ToArray();
        var contentTypes = Array.Empty<string>();
        var facetPairs = GenericFacetPattern().Matches(rawQuery)
            .Select(match => (Category: match.Groups[1].Value, Value: match.Groups[2].Value))
            .Concat(LegacyFacetPattern().Matches(rawQuery)
                .Select(match => (Category: match.Groups[1].Value, Value: match.Groups[2].Value)));
        var facets = facetPairs
            .GroupBy(match => ClassificationService.NormalizeKey(match.Category))
            .ToDictionary(group => group.Key,
                group => group.Select(match => match.Value).Distinct().ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var text = GenericFacetPattern().Replace(rawQuery, " ");
        text = LegacyFacetPattern().Replace(text, " ");
        text = TagPattern().Replace(text, " ");
        text = AuthorPattern().Replace(text, " ");
        text = WhitespacePattern().Replace(text, " ").Trim();

        return new ParsedKnowledgeQuery(text, tags, authors, contentTypes, facets);
    }

    private sealed record ParsedKnowledgeQuery(
        string Text,
        IReadOnlyList<string> Tags,
        IReadOnlyList<string> Authors,
        IReadOnlyList<string> ContentTypes,
        IReadOnlyDictionary<string, string[]> Facets);

    private static Dictionary<string, string[]> MergeFacets(
        IReadOnlyDictionary<string, string[]> inline,
        IReadOnlyDictionary<string, string[]>? explicitValues)
    {
        var result = inline.ToDictionary(entry => entry.Key, entry => entry.Value,
            StringComparer.OrdinalIgnoreCase);
        foreach (var (rawCategory, values) in explicitValues ?? new Dictionary<string, string[]>())
        {
            var category = ClassificationService.NormalizeKey(rawCategory);
            result[category] = result.GetValueOrDefault(category, [])
                .Concat(values ?? []).Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        return result;
    }

    [GeneratedRegex(@"(?<!\S)\+([\p{L}\p{N}_-]+):([\p{L}\p{N}_.-]+)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex GenericFacetPattern();

    [GeneratedRegex(@"(?<!\S)facet:([\p{L}\p{N}_-]+)=([\p{L}\p{N}_.-]+)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LegacyFacetPattern();

    [GeneratedRegex(@"(?<!\S)#(?!#)([\p{L}\p{N}_-]+)", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"(?<!\S)@([\p{L}\p{N}._-]+)", RegexOptions.CultureInvariant)]
    private static partial Regex AuthorPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
