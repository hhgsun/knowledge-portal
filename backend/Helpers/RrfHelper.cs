namespace KnowledgePortal.Api.Helpers;

/// <summary>
/// Reciprocal Rank Fusion for hybrid search: merges the full-text and semantic
/// candidate rankings into weighted scores with a per-item match type.
/// </summary>
public static class RrfHelper
{
    /// <summary>
    /// score = alpha / (k + rank + 1) per leg, summed when an item appears in both.
    /// MatchType is "fulltext", "semantic", or "both".
    /// </summary>
    public static Dictionary<string, (double Score, string MatchType)> Merge(
        IReadOnlyList<string> fulltextIds,
        IReadOnlyList<string>? semanticIds,
        int k = 60,
        double alphaFulltext = 0.4,
        double alphaSemantic = 0.6)
    {
        var scores = new Dictionary<string, (double Score, string MatchType)>();

        for (var i = 0; i < fulltextIds.Count; i++)
            scores[fulltextIds[i]] = (alphaFulltext / (k + i + 1), "fulltext");

        if (semanticIds != null)
        {
            for (var i = 0; i < semanticIds.Count; i++)
            {
                var id = semanticIds[i];
                var score = alphaSemantic / (k + i + 1);
                scores[id] = scores.TryGetValue(id, out var existing)
                    ? (existing.Score + score, "both")
                    : (score, "semantic");
            }
        }

        return scores;
    }
}
