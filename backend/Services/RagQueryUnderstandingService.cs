using System.Text.RegularExpressions;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;

namespace KnowledgePortal.Api.Services;

public sealed record RagExtractedFilters(IReadOnlyList<string> Tags, IReadOnlyList<string> Authors,
    IReadOnlyList<string> ContentTypes);

public sealed record RagQueryPlan(string OriginalQuery, string RewrittenQuery,
    IReadOnlyList<string> Queries, RagExtractedFilters ExtractedFilters,
    IReadOnlyList<string> Expansions, bool IsComplex, bool PrefersFreshSources,
    ArticleFilter? EffectiveFilter, string? HypotheticalDocument = null);

/// <summary>
/// Cheap deterministic query understanding. It extracts explicit metadata filters, expands a
/// centrally configured acronym/synonym dictionary, and decomposes only compound questions.
/// No LLM call is added to the common search path.
/// </summary>
public sealed class RagQueryUnderstandingService(IConfiguration config)
{
    private static readonly Regex FilterPattern = new(
        @"(?:^|\s)(?<kind>##|#|@|tag:|etiket:|author:|yazar:|type:|tür:|tur:)(?<value>[\p{L}\p{N}_-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    private static readonly Regex CompoundSeparator = new(
        @"\s+(?:ve\s+ayrıca|ayrıca|bununla\s+birlikte|ve|ile|and|also|versus|vs\.?)\s+|[;]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    private static readonly string[] ComplexSignals =
        ["karsilastir", "compare", "fark", "ozetle", "tum", "hepsi", "nedenleri", "adimlari", "iliskisi"];
    private static readonly string[] FreshnessSignals =
        ["guncel", "en yeni", "son surum", "latest", "newest", "current", "last year", "bu yil"];

    private readonly int _maxQueries = Math.Clamp(config.GetValue("Ollama:QueryUnderstanding:MaxQueries", 3), 1, 5);
    private readonly bool _rewriteEnabled = config.GetValue("Ollama:QueryUnderstanding:RewriteEnabled", true);
    private readonly bool _decompositionEnabled = config.GetValue("Ollama:QueryUnderstanding:DecompositionEnabled", true);
    private readonly Dictionary<string, string[]> _synonyms = config
        .GetSection("Ollama:QueryUnderstanding:Synonyms").GetChildren()
        .ToDictionary(x => x.Key.ToLowerInvariant(), x => x.Get<string[]>() ?? [], StringComparer.OrdinalIgnoreCase);

    public async Task<RagQueryPlan> UnderstandAsync(AppDbContext db, string query,
        ArticleFilter? existingFilter = null, CancellationToken ct = default,
        string? hypotheticalDocument = null)
    {
        var tags = new List<string>();
        var authors = new List<string>();
        var contentTypes = new List<string>();
        foreach (Match match in FilterPattern.Matches(query))
        {
            var kind = match.Groups["kind"].Value.ToLowerInvariant();
            var value = match.Groups["value"].Value.Trim().ToLowerInvariant();
            if (kind is "#" or "tag:" or "etiket:") tags.Add(value);
            else if (kind is "@" or "author:" or "yazar:") authors.Add(value);
            else contentTypes.Add(value);
        }

        var cleaned = FilterPattern.Replace(query, " ");
        cleaned = string.Join(' ', cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(cleaned)) cleaned = query.Trim();

        var expansions = new List<string>();
        if (_rewriteEnabled)
        {
            var tokens = Fold(cleaned).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            foreach (var entry in _synonyms)
                if (tokens.Contains(Fold(entry.Key))) expansions.AddRange(entry.Value.Where(x => !string.IsNullOrWhiteSpace(x)));
        }
        expansions = expansions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var rewritten = expansions.Count == 0 ? cleaned : $"{cleaned} {string.Join(' ', expansions)}";

        var folded = Fold(cleaned);
        var isComplex = ComplexSignals.Any(folded.Contains) || CompoundSeparator.IsMatch(cleaned);
        var queries = new List<string> { rewritten };
        if (_decompositionEnabled && isComplex)
        {
            queries.AddRange(CompoundSeparator.Split(cleaned)
                .Select(x => x.Trim()).Where(x => x.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2));
        }
        queries = queries.Distinct(StringComparer.OrdinalIgnoreCase).Take(_maxQueries).ToList();

        List<string>? resolvedOwners = existingFilter?.OwnerIds;
        if (authors.Count > 0)
            resolvedOwners = (resolvedOwners ?? []).Concat(await db.ResolveAuthorIdsAsync(authors)).Distinct().ToList();
        var mergedTags = Merge(existingFilter?.TagSlugs, tags);
        var mergedTypes = Merge(existingFilter?.ContentTypes, contentTypes);
        var effective = existingFilter == null && resolvedOwners == null && mergedTags == null && mergedTypes == null
            ? null
            : new ArticleFilter(resolvedOwners, mergedTypes, existingFilter?.ApiKeyId,
                existingFilter?.ArticleIds, mergedTags);

        return new(query, rewritten, queries,
            new(tags.Distinct().ToList(), authors.Distinct().ToList(), contentTypes.Distinct().ToList()),
            expansions, isComplex, FreshnessSignals.Any(folded.Contains), effective,
            string.IsNullOrWhiteSpace(hypotheticalDocument) ? null : hypotheticalDocument.Trim());
    }

    private static List<string>? Merge(IEnumerable<string>? existing, IEnumerable<string> added)
    {
        var values = (existing ?? []).Concat(added).Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return values.Count == 0 && existing == null ? null : values;
    }

    private static string Fold(string value) => Helpers.SlugHelper.Transliterate(value).ToLowerInvariant();
}
