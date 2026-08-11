namespace KnowledgePortal.Api.Helpers;

public record RankingMetrics(double RecallAtK, double Mrr, double NdcgAtK);

/// <summary>Offline relevance metrics for labelled search-query benchmark fixtures.</summary>
public static class RelevanceMetrics
{
    public static RankingMetrics Calculate(IReadOnlyList<string> rankedIds,
        IReadOnlyDictionary<string, int> relevance, int k)
    {
        var take = rankedIds.Take(Math.Max(1, k)).ToList();
        var relevantTotal = relevance.Count(x => x.Value > 0);
        var hits = take.Count(id => relevance.GetValueOrDefault(id) > 0);
        var recall = relevantTotal == 0 ? 0 : hits / (double)relevantTotal;

        var first = take.FindIndex(id => relevance.GetValueOrDefault(id) > 0);
        var mrr = first < 0 ? 0 : 1d / (first + 1);
        var dcg = take.Select((id, i) => Gain(relevance.GetValueOrDefault(id), i)).Sum();
        var ideal = relevance.Values.OrderByDescending(x => x).Take(take.Count)
            .Select((grade, i) => Gain(grade, i)).Sum();
        return new RankingMetrics(recall, mrr, ideal == 0 ? 0 : dcg / ideal);
    }

    private static double Gain(int grade, int zeroBasedRank)
        => (Math.Pow(2, Math.Max(0, grade)) - 1) / Math.Log2(zeroBasedRank + 2);
}
