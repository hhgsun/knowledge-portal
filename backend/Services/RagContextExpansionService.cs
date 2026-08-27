using System.Text.RegularExpressions;
using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public sealed record RagExpansionResult(List<VectorChunkResult> Chunks, int ExpandedParentCount,
    IReadOnlyList<string> ExpandedParentLocations);

/// <summary>
/// Resolves strong searchable child hits to their larger, structure-bounded parent context.
/// Parent lookup is constrained by the already-authorized article set. During a rolling upgrade,
/// legacy rows without a parent FK retain the old adjacent-chunk fallback.
/// </summary>
public sealed class RagContextExpansionService(IConfiguration config)
{
    private static readonly Regex LegacyChunkLocation = new(
        @"^(?<parent>.+):chunk:(?<index>\d+)$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));
    private readonly bool _enabled = config.GetValue("Ollama:ContextExpansion:Enabled", true);
    private readonly int _legacyNeighborCount = Math.Clamp(
        config.GetValue("Ollama:ContextExpansion:NeighborCount", 1), 0, 2);
    private readonly int _maxSeeds = Math.Clamp(config.GetValue("Ollama:ContextExpansion:MaxSeeds", 8), 1, 20);
    private readonly double _minSeedScore = Math.Clamp(
        config.GetValue("Ollama:ContextExpansion:MinSeedScore", .50), 0, 1);
    private readonly double _legacyNeighborScoreDecay = Math.Clamp(
        config.GetValue("Ollama:ContextExpansion:NeighborScoreDecay", .92), .5, 1);
    private readonly string _model = config["Ollama:EmbeddingModel"] ?? "bge-m3";

    public async Task<RagExpansionResult> ExpandAsync(AppDbContext db,
        IReadOnlyList<VectorChunkResult> ranked, IReadOnlySet<string> allowedArticleIds,
        CancellationToken ct = default)
    {
        if (!_enabled || ranked.Count == 0)
            return new([.. ranked], 0, []);

        var allowedIds = allowedArticleIds.ToList();
        var strong = ranked
            .Where(x => x.Score >= _minSeedScore && allowedArticleIds.Contains(x.ArticleId))
            .Take(_maxSeeds).ToList();
        if (strong.Count == 0) return new([.. ranked], 0, []);

        var parentIds = strong.Select(x => x.ParentChunkId)
            .Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().Distinct().ToList();
        var parents = parentIds.Count == 0
            ? []
            : await db.ArticleChunkParents.AsNoTracking()
                .Where(x => parentIds.Contains(x.Id) && allowedIds.Contains(x.ArticleId))
                .ToListAsync(ct);
        var parentById = parents.ToDictionary(x => x.Id, StringComparer.Ordinal);

        // Keeps old chunks useful while durable jobs rebuild the corpus with parent FKs.
        var legacySeeds = _legacyNeighborCount == 0
            ? []
            : strong.Where(x => x.ParentChunkId == null && Parse(x.SourceLocation) != null).ToList();
        var legacyBySeed = await LoadLegacyNeighborsAsync(db, legacySeeds, allowedIds, ct);

        var output = new List<VectorChunkResult>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var expandedLocations = new HashSet<string>(StringComparer.Ordinal);
        var expandedParents = 0;

        foreach (var chunk in ranked)
        {
            if (chunk.ParentChunkId is { } parentId && parentById.TryGetValue(parentId, out var parent))
            {
                var key = $"parent:{parent.Id}";
                if (seen.Add(key))
                {
                    output.Add(new VectorChunkResult(parent.ArticleId, -(parent.ParentIndex + 1),
                        chunk.Score, parent.Content, parent.SourceType, parent.AttachmentId,
                        parent.SourceName, parent.SourceLocation, parent.Id, parent.Id));
                    expandedParents++;
                    if (parent.SourceLocation != null) expandedLocations.Add(parent.SourceLocation);
                }
                continue;
            }

            if (seen.Add(Key(chunk))) output.Add(chunk);
            if (!legacyBySeed.TryGetValue(Key(chunk), out var neighbors)) continue;
            var parsed = Parse(chunk.SourceLocation);
            if (parsed != null) expandedLocations.Add(parsed.Value.Parent);
            foreach (var neighbor in neighbors)
                if (seen.Add(Key(neighbor))) output.Add(neighbor);
        }

        return new(output, expandedParents, expandedLocations.Order().ToList());
    }

    private async Task<Dictionary<string, List<VectorChunkResult>>> LoadLegacyNeighborsAsync(
        AppDbContext db, IReadOnlyList<VectorChunkResult> seeds,
        IReadOnlyList<string> allowedArticleIds, CancellationToken ct)
    {
        if (seeds.Count == 0) return [];
        var articleIds = seeds.Select(x => x.ArticleId).Distinct().ToList();
        var stored = await db.ArticleEmbeddings.AsNoTracking()
            .Where(x => articleIds.Contains(x.ArticleId) && allowedArticleIds.Contains(x.ArticleId)
                && x.ModelName == _model && x.Content != null && x.ParentChunkId == null)
            .Select(x => new { x.Id, x.ArticleId, x.ChunkIndex, x.Content, x.SourceType,
                x.AttachmentId, x.SourceName, x.SourceLocation })
            .ToListAsync(ct);

        var result = new Dictionary<string, List<VectorChunkResult>>(StringComparer.Ordinal);
        foreach (var seed in seeds)
        {
            var parsed = Parse(seed.SourceLocation)!.Value;
            var neighbors = stored.Select(x => (Row: x, Parsed: Parse(x.SourceLocation)))
                .Where(x => x.Parsed != null && x.Row.ArticleId == seed.ArticleId
                    && x.Row.SourceType == seed.SourceType && x.Row.AttachmentId == seed.AttachmentId
                    && x.Parsed.Value.Parent == parsed.Parent
                    && Math.Abs(x.Parsed.Value.Index - parsed.Index) is >= 1 and <= 2
                    && Math.Abs(x.Parsed.Value.Index - parsed.Index) <= _legacyNeighborCount)
                .OrderBy(x => Math.Abs(x.Parsed!.Value.Index - parsed.Index))
                .ThenBy(x => x.Parsed!.Value.Index)
                .Select(x => new VectorChunkResult(x.Row.ArticleId, x.Row.ChunkIndex,
                    seed.Score * Math.Pow(_legacyNeighborScoreDecay,
                        Math.Abs(x.Parsed!.Value.Index - parsed.Index)),
                    x.Row.Content!, x.Row.SourceType, x.Row.AttachmentId, x.Row.SourceName,
                    x.Row.SourceLocation, x.Row.Id))
                .ToList();
            if (neighbors.Count > 0) result[Key(seed)] = neighbors;
        }
        return result;
    }

    internal static (string Parent, int Index)? Parse(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;
        var match = LegacyChunkLocation.Match(location);
        return match.Success && int.TryParse(match.Groups["index"].Value, out var index)
            ? (match.Groups["parent"].Value, index)
            : null;
    }

    private static string Key(VectorChunkResult x) =>
        $"{x.ArticleId}:{x.SourceType}:{x.AttachmentId}:{x.ChunkIndex}";
}
