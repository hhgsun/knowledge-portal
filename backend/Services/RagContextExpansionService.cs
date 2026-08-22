using System.Text.RegularExpressions;
using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public sealed record RagExpansionResult(List<VectorChunkResult> Chunks, int AddedNeighbors,
    IReadOnlyList<string> ExpandedParentLocations);

/// <summary>
/// Selectively expands strong child hits with adjacent chunks from the same derived parent
/// section/page/sheet. It never crosses article, attachment, source, model, or ACL boundaries.
/// </summary>
public sealed class RagContextExpansionService(IConfiguration config)
{
    private static readonly Regex ChunkLocation = new(
        @"^(?<parent>.+):chunk:(?<index>\d+)$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));
    private readonly bool _enabled = config.GetValue("Ollama:ContextExpansion:Enabled", true);
    private readonly int _neighborCount = Math.Clamp(config.GetValue("Ollama:ContextExpansion:NeighborCount", 1), 0, 2);
    private readonly int _maxSeeds = Math.Clamp(config.GetValue("Ollama:ContextExpansion:MaxSeeds", 6), 1, 20);
    private readonly double _minSeedScore = Math.Clamp(config.GetValue("Ollama:ContextExpansion:MinSeedScore", .55), 0, 1);
    private readonly double _neighborScoreDecay = Math.Clamp(config.GetValue("Ollama:ContextExpansion:NeighborScoreDecay", .92), .5, 1);
    private readonly string _model = config["Ollama:EmbeddingModel"] ?? "bge-m3";

    public async Task<RagExpansionResult> ExpandAsync(AppDbContext db,
        IReadOnlyList<VectorChunkResult> ranked, IReadOnlySet<string> allowedArticleIds,
        CancellationToken ct = default)
    {
        if (!_enabled || _neighborCount == 0 || ranked.Count == 0)
            return new([.. ranked], 0, []);

        var seeds = ranked.Where(x => x.Score >= _minSeedScore && allowedArticleIds.Contains(x.ArticleId))
            .Select(x => (Chunk: x, Parsed: Parse(x.SourceLocation)))
            .Where(x => x.Parsed != null).Take(_maxSeeds).ToList();
        if (seeds.Count == 0) return new([.. ranked], 0, []);

        var articleIds = seeds.Select(x => x.Chunk.ArticleId).Distinct().ToList();
        var stored = await db.ArticleEmbeddings.AsNoTracking()
            .Where(x => articleIds.Contains(x.ArticleId) && x.ModelName == _model && x.Content != null)
            .Select(x => new { x.Id, x.ArticleId, x.ChunkIndex, x.Content, x.SourceType, x.AttachmentId,
                x.SourceName, x.SourceLocation })
            .ToListAsync(ct);

        var neighborsBySeed = new Dictionary<string, List<VectorChunkResult>>();
        var parents = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seed in seeds)
        {
            var parsed = seed.Parsed!.Value;
            var neighbors = stored.Select(x => (Row: x, Parsed: Parse(x.SourceLocation)))
                .Where(x => x.Parsed != null
                    && x.Row.ArticleId == seed.Chunk.ArticleId
                    && x.Row.SourceType == seed.Chunk.SourceType
                    && x.Row.AttachmentId == seed.Chunk.AttachmentId
                    && x.Parsed.Value.Parent == parsed.Parent
                    && Math.Abs(x.Parsed.Value.Index - parsed.Index) is >= 1 and <= 2
                    && Math.Abs(x.Parsed.Value.Index - parsed.Index) <= _neighborCount)
                .OrderBy(x => Math.Abs(x.Parsed!.Value.Index - parsed.Index))
                .ThenBy(x => x.Parsed!.Value.Index)
                .Select(x => new VectorChunkResult(x.Row.ArticleId, x.Row.ChunkIndex,
                    seed.Chunk.Score * Math.Pow(_neighborScoreDecay, Math.Abs(x.Parsed!.Value.Index - parsed.Index)),
                    x.Row.Content!, x.Row.SourceType, x.Row.AttachmentId, x.Row.SourceName,
                    x.Row.SourceLocation, x.Row.Id))
                .ToList();
            if (neighbors.Count == 0) continue;
            neighborsBySeed[Key(seed.Chunk)] = neighbors;
            parents.Add(parsed.Parent);
        }

        var output = new List<VectorChunkResult>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var added = 0;
        foreach (var chunk in ranked)
        {
            if (seen.Add(Key(chunk))) output.Add(chunk);
            if (!neighborsBySeed.TryGetValue(Key(chunk), out var neighbors)) continue;
            foreach (var neighbor in neighbors)
                if (seen.Add(Key(neighbor))) { output.Add(neighbor); added++; }
        }
        return new(output, added, parents.Order().ToList());
    }

    internal static (string Parent, int Index)? Parse(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;
        var match = ChunkLocation.Match(location);
        return match.Success && int.TryParse(match.Groups["index"].Value, out var index)
            ? (match.Groups["parent"].Value, index)
            : null;
    }

    private static string Key(VectorChunkResult x) =>
        $"{x.ArticleId}:{x.SourceType}:{x.AttachmentId}:{x.ChunkIndex}";
}
