using System.Text.RegularExpressions;
using KnowledgePortal.Api.Helpers;

namespace KnowledgePortal.Api.Services;

public sealed record RagConflict(string Kind, IReadOnlyList<string> SourceIds,
    string? PreferredSourceId, string Resolution);
public sealed record RagConflictAssessment(string Status, IReadOnlyList<RagConflict> Conflicts)
{
    public static readonly RagConflictAssessment None = new("none_detected", []);
}

/// <summary>
/// Conservative deterministic conflict screening. It intentionally claims only numeric and explicit
/// polarity conflicts between locally relevant evidence sentences; broader semantic contradiction is
/// left to a future entailment evaluator.
/// </summary>
public static partial class RagConflictDetector
{
    public static RagConflictAssessment Assess(string question, IReadOnlyCollection<RagEvidence> evidence)
    {
        var queryTokens = Tokens(question);
        var candidates = evidence.SelectMany(item => Sentences(item.Passage)
                .Where(sentence => queryTokens.Count == 0 || Tokens(sentence).Overlaps(queryTokens))
                .Select(sentence => new Candidate(item, sentence, Tokens(sentence), Numbers(sentence),
                    HasNegation(sentence))))
            .ToList();
        var conflicts = new List<RagConflict>();
        for (var leftIndex = 0; leftIndex < candidates.Count; leftIndex++)
        for (var rightIndex = leftIndex + 1; rightIndex < candidates.Count; rightIndex++)
        {
            var left = candidates[leftIndex];
            var right = candidates[rightIndex];
            if (left.Evidence.SourceId == right.Evidence.SourceId) continue;
            var shared = left.Tokens.Intersect(right.Tokens).Count();
            if (shared < 2 && !left.Tokens.Intersect(queryTokens).Any(token => right.Tokens.Contains(token)))
                continue;

            var numeric = left.Numbers.Count > 0 && right.Numbers.Count > 0 &&
                          !left.Numbers.SetEquals(right.Numbers);
            var polarity = left.Negated != right.Negated && shared >= 2;
            if (!numeric && !polarity) continue;

            var ids = new[] { left.Evidence.SourceId, right.Evidence.SourceId }
                .Order(StringComparer.Ordinal).ToArray();
            if (conflicts.Any(conflict => conflict.SourceIds.SequenceEqual(ids))) continue;
            var preferred = Preferred(left.Evidence, right.Evidence);
            conflicts.Add(new RagConflict(numeric ? "numeric" : "polarity", ids,
                preferred?.SourceId, preferred == null ? "unresolved_equal_governance" : "preferred_by_governance"));
        }

        return conflicts.Count == 0 ? RagConflictAssessment.None : new("conflicts_detected", conflicts);
    }

    private static RagEvidence? Preferred(RagEvidence left, RagEvidence right)
    {
        var comparison = left.Approved.CompareTo(right.Approved);
        if (comparison == 0) comparison = left.AuthorityWeight.CompareTo(right.AuthorityWeight);
        if (comparison == 0) comparison = ReviewRank(left.ReviewState).CompareTo(ReviewRank(right.ReviewState));
        if (comparison == 0) comparison = left.ReliabilityScore.CompareTo(right.ReliabilityScore);
        if (comparison == 0 && DateTime.TryParse(left.UpdatedAt, out var leftUpdated) &&
            DateTime.TryParse(right.UpdatedAt, out var rightUpdated))
            comparison = leftUpdated.CompareTo(rightUpdated);
        return comparison == 0 ? null : comparison > 0 ? left : right;
    }

    private static int ReviewRank(string state) => state switch
    {
        "current" => 4, "due_soon" => 3, "not_recorded" => 2, "overdue" => 1, _ => 0
    };
    private static HashSet<string> Tokens(string text) => SlugHelper.Transliterate(text).ToLowerInvariant()
        .Split(new[] { ' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '"', '\'' },
            StringSplitOptions.RemoveEmptyEntries)
        .Where(token => token.Length >= 3 && !StopWords.Contains(token)).ToHashSet(StringComparer.Ordinal);
    private static HashSet<string> Numbers(string text) => NumberRegex().Matches(text)
        .Select(match => match.Value).ToHashSet(StringComparer.Ordinal);
    private static bool HasNegation(string text) => NegationRegex().IsMatch(
        SlugHelper.Transliterate(text).ToLowerInvariant());
    private static IEnumerable<string> Sentences(string text) => SentenceRegex().Split(text)
        .Select(sentence => sentence.Trim()).Where(sentence => sentence.Length > 0);

    private sealed record Candidate(RagEvidence Evidence, string Sentence, HashSet<string> Tokens,
        HashSet<string> Numbers, bool Negated);
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "ve", "veya", "ile", "icin", "bir", "the", "and", "for", "this", "that"
    };

    [GeneratedRegex(@"\b\d+(?:[.,]\d+)?\b")]
    private static partial Regex NumberRegex();
    [GeneratedRegex(@"\b(?:not|never|no|degil|yok|hayir)\b|mamal[ıi]|memeli|maz\b|mez\b", RegexOptions.IgnoreCase)]
    private static partial Regex NegationRegex();
    [GeneratedRegex(@"(?<=[.!?])\s+|\r?\n+")]
    private static partial Regex SentenceRegex();
}
