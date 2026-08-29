using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public sealed record ClassificationResolution(
    string ContentType,
    IReadOnlyDictionary<string, IReadOnlyList<LookupValue>> Values,
    IReadOnlySet<string> ChangedCategories);

/// <summary>
/// Owns validation and article assignments for controlled dynamic metadata.
/// content_type is mirrored to Article.ContentType for backwards-compatible APIs and indexes.
/// </summary>
public sealed class ClassificationService(AppDbContext db)
{
    private const string LegacyContentTypeFallback = "reference";

    public async Task<(ClassificationResolution? Resolution, ServiceError? Error)> ResolveAsync(
        string? contentType,
        Dictionary<string, string[]>? classifications,
        bool isCreate,
        CancellationToken ct = default)
    {
        var supplied = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawCategory, rawValues) in classifications ?? [])
        {
            var category = NormalizeKey(rawCategory);
            if (category.Length == 0)
                return (null, new ServiceError(400, "Classification category cannot be blank"));
            supplied[category] = (rawValues ?? [])
                .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        var categories = await db.LookupCategories.AsNoTracking()
            .Where(category => category.IsActive)
            .ToListAsync(ct);
        var categoryByKey = categories.ToDictionary(category => category.Key,
            StringComparer.OrdinalIgnoreCase);
        var allValues = await db.LookupValues.AsNoTracking()
            .Where(value => value.IsActive).ToListAsync(ct);

        // The legacy contentType field maps into the generic category only while that
        // seeded category is active. Deactivating/removing it must not break old articles.
        if (!string.IsNullOrWhiteSpace(contentType) && categoryByKey.ContainsKey("content_type"))
        {
            var requested = contentType.Trim();
            if (supplied.TryGetValue("content_type", out var genericTypes)
                && genericTypes.Count > 0
                && !genericTypes.Contains(requested, StringComparer.OrdinalIgnoreCase))
                return (null, new ServiceError(400,
                    "contentType and classifications.content_type must identify the same value"));
            if (genericTypes is { Count: > 0 })
            {
                genericTypes.RemoveAll(value => value.Equals(requested, StringComparison.OrdinalIgnoreCase));
                genericTypes.Insert(0, requested);
            }
            else supplied["content_type"] = [requested];
        }

        if (isCreate)
        {
            foreach (var category in categories)
            {
                if (supplied.ContainsKey(category.Key)) continue;
                var defaultValue = allValues.FirstOrDefault(value => value.Id == category.DefaultValueId);
                if (defaultValue != null)
                {
                    supplied[category.Key] = [defaultValue.Value];
                    continue;
                }
                if (category.IsRequired)
                    return (null, new ServiceError(400,
                        $"Classification '{category.Key}' is required and has no active default value"));
            }
        }

        var resolved = new Dictionary<string, IReadOnlyList<LookupValue>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (categoryKey, requestedValues) in supplied)
        {
            if (!categoryByKey.TryGetValue(categoryKey, out var category))
                return (null, new ServiceError(400,
                    $"Unknown or inactive classification category '{categoryKey}'"));
            if (category.Cardinality == "single" && requestedValues.Count > 1)
                return (null, new ServiceError(400,
                    $"Classification '{category.Key}' accepts a single value"));
            if (category.IsRequired && requestedValues.Count == 0)
                return (null, new ServiceError(400,
                    $"Classification '{category.Key}' is required"));

            var categoryValues = allValues.Where(value =>
                value.Category.Equals(category.Key, StringComparison.OrdinalIgnoreCase)).ToList();
            var selections = new List<LookupValue>();
            foreach (var requested in requestedValues)
            {
                var match = categoryValues.FirstOrDefault(value =>
                    value.Value.Equals(requested.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match == null)
                    return (null, new ServiceError(400,
                        $"Unknown or inactive value '{requested}' for classification '{category.Key}'"));
                if (selections.All(value => value.Id != match.Id)) selections.Add(match);
            }
            resolved[category.Key] = selections;
        }

        var resolvedContentType = resolved.GetValueOrDefault("content_type")?.FirstOrDefault()?.Value
            ?? contentType?.Trim()
            ?? allValues.FirstOrDefault(value => value.Category == "content_type")?.Value
            ?? LegacyContentTypeFallback;
        return (new ClassificationResolution(resolvedContentType, resolved,
            supplied.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)), null);
    }

    public async Task ApplyAsync(string articleId, ClassificationResolution resolution,
        CancellationToken ct = default)
    {
        if (resolution.ChangedCategories.Count == 0) return;
        var categoryKeys = resolution.ChangedCategories.ToArray();
        var categoryValueIds = await db.LookupValues.Where(value => categoryKeys.Contains(value.Category))
            .Select(value => value.Id).ToListAsync(ct);
        var existing = await db.ArticleLookupValues.Where(assignment => assignment.ArticleId == articleId
            && categoryValueIds.Contains(assignment.LookupValueId)).ToListAsync(ct);
        var desiredIds = resolution.Values
            .Where(entry => resolution.ChangedCategories.Contains(entry.Key))
            .SelectMany(entry => entry.Value).Select(value => value.Id).ToHashSet();
        db.ArticleLookupValues.RemoveRange(existing.Where(assignment =>
            !desiredIds.Contains(assignment.LookupValueId)));
        var existingIds = existing.Select(assignment => assignment.LookupValueId).ToHashSet();
        db.ArticleLookupValues.AddRange(desiredIds.Where(id => !existingIds.Contains(id))
            .Select(id => new ArticleLookupValue { ArticleId = articleId, LookupValueId = id }));
    }

    public async Task<Dictionary<string, Dictionary<string, string[]>>> GetAssignmentsAsync(
        IEnumerable<string> articleIds, CancellationToken ct = default)
    {
        var ids = articleIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        var rows = await db.ArticleLookupValues.AsNoTracking()
            .Where(assignment => ids.Contains(assignment.ArticleId))
            .Select(assignment => new
            {
                assignment.ArticleId,
                assignment.LookupValue.Category,
                assignment.LookupValue.Value
            }).ToListAsync(ct);

        return rows.GroupBy(row => row.ArticleId).ToDictionary(group => group.Key,
            group => group.GroupBy(row => row.Category).ToDictionary(
                category => category.Key,
                category => category.Select(row => row.Value).Distinct().Order().ToArray(),
                StringComparer.OrdinalIgnoreCase));
    }

    public async Task<(Dictionary<string, string[]> Facets, bool HasUnknown)> ResolveFacetFiltersAsync(
        IReadOnlyDictionary<string, string[]>? requested, CancellationToken ct = default)
    {
        if (requested == null || requested.Count == 0) return ([], false);
        var categories = await db.LookupCategories.AsNoTracking()
            .Where(category => category.IsActive && category.RagBehavior != "none")
            .ToDictionaryAsync(category => category.Key, StringComparer.OrdinalIgnoreCase, ct);
        var values = await db.LookupValues.AsNoTracking().Where(value => value.IsActive).ToListAsync(ct);
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var unknown = false;
        foreach (var (rawCategory, requestedValues) in requested)
        {
            var key = NormalizeKey(rawCategory);
            if (!categories.ContainsKey(key)) { unknown = true; continue; }
            var resolved = new List<string>();
            var allowed = values.Where(value => value.Category.Equals(key,
                StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var requestedValue in requestedValues ?? [])
            {
                var match = allowed.FirstOrDefault(value =>
                    value.Value.Equals(requestedValue.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match == null) unknown = true;
                else if (!resolved.Contains(match.Value, StringComparer.OrdinalIgnoreCase))
                    resolved.Add(match.Value);
            }
            if (resolved.Count > 0) result[key] = resolved.ToArray();
            else if ((requestedValues ?? []).Length > 0) unknown = true;
        }
        return (result, unknown);
    }

    public static string NormalizeKey(string value)
        => SlugHelper.GenerateTagSlug(value).Replace('-', '_');

    public static Dictionary<string, string[]> ParseFacetPairs(IEnumerable<string>? raw)
        => (raw ?? []).Select(value => value.Split(':', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && parts.All(part => part.Length > 0))
            .GroupBy(parts => parts[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key,
                group => group.Select(parts => parts[1]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase);

}
