using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public sealed record ResearchScopeResolution(
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, string[]> Facets,
    IReadOnlyList<string> IgnoredCandidates);

/// <summary>
/// Resolves sanitized planner candidates against active portal metadata. A tag match always wins
/// over a lookup match for the same phrase, so an LLM cannot invent a scope or broaden a request.
/// </summary>
public sealed class ResearchScopeResolver(AppDbContext db)
{
    public async Task<ResearchScopeResolution> ResolveAsync(IEnumerable<string> candidates,
        CancellationToken ct = default)
    {
        var terms = candidates.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Normalize).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (terms.Length == 0)
            return new([], new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase), []);

        var tags = await db.Tags.AsNoTracking().ToListAsync(ct);
        var activeValues = await db.LookupValues.AsNoTracking()
            .Join(db.LookupCategories.AsNoTracking().Where(category => category.IsActive && category.RagBehavior != "none"),
                value => value.Category, category => category.Key, (value, _) => value)
            .Where(value => value.IsActive).ToListAsync(ct);
        var resolvedTags = new List<string>();
        var facets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var ignored = new List<string>();
        foreach (var term in terms)
        {
            var tag = tags.FirstOrDefault(item => Normalize(item.Slug) == term || Normalize(item.Name) == term);
            if (tag != null)
            {
                resolvedTags.Add(tag.Slug);
                continue;
            }
            var value = activeValues.FirstOrDefault(item => Normalize(item.Value) == term || Normalize(item.Label) == term);
            if (value == null) { ignored.Add(term); continue; }
            if (!facets.TryGetValue(value.Category, out var values)) facets[value.Category] = values = [];
            values.Add(value.Value);
        }
        return new(resolvedTags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            facets.ToDictionary(entry => entry.Key, entry => entry.Value.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase), ignored);
    }

    private static string Normalize(string value) => ClassificationService.NormalizeKey(value);
}