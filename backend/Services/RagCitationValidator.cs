using System.Text.Json;
using System.Text.RegularExpressions;
using KnowledgePortal.Api.Helpers;

namespace KnowledgePortal.Api.Services;

public record RagClaim(string Text, List<string> SourceIds);
public record RagEvidence(string SourceId, string ArticleId, string Title, string Slug, string SourceType,
    string? AttachmentId, string? SourceName, string? SourceLocation, string Passage, double Score,
    string? ChunkId = null, string? CanonicalUrl = null, int? PageNumber = null);
public record ValidatedRagAnswer(string Answer, List<RagClaim> Claims, bool InsufficientContext,
    double CitationCoverage, double ClaimSupportCoverage, string GroundingStatus, List<string> Warnings);

public static partial class RagCitationValidator
{
    private const string RefuseInsufficient = "Bu konuda yeterli bilgi bulamadım.";
    private const double MinimumLexicalSupport = .65;
    private sealed record ModelOutput(string? Answer, List<RagClaim>? Claims, bool? InsufficientContext,
        bool RecoveredFromCitedText = false);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static ValidatedRagAnswer Validate(string raw, IReadOnlyCollection<RagEvidence> evidence)
    {
        var warnings = new List<string>();
        var parsed = TryParse(raw);
        if (parsed == null)
            return new(RefuseInsufficient, [], true, 0, 0, "rejected_unstructured",
                ["Model output was rejected because it did not match the required structured JSON contract."]);

        if (parsed.RecoveredFromCitedText)
            warnings.Add("Model returned cited text instead of JSON; evidence-bound claims were recovered and validated.");

        if (parsed.InsufficientContext == true)
            return new(RefuseInsufficient, [], true, 1, 1, "insufficient_context", warnings);

        var allowed = evidence.Select(x => x.SourceId).ToHashSet(StringComparer.Ordinal);
        var evidenceById = evidence.ToDictionary(x => x.SourceId, StringComparer.Ordinal);
        var claims = new List<RagClaim>();
        var valid = 0;
        var supported = 0;
        foreach (var claim in parsed.Claims ?? [])
        {
            if (string.IsNullOrWhiteSpace(claim.Text)) continue;
            var requestedIds = claim.SourceIds ?? [];
            var ids = requestedIds.Where(allowed.Contains).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count != requestedIds.Count)
                warnings.Add($"Claim contained unknown evidence IDs: {claim.Text[..Math.Min(80, claim.Text.Length)]}");
            if (ids.Count > 0) valid++;
            if (ids.Count == 0) continue;

            // Validate against local sentence/clause windows from each cited item independently.
            // Joining whole chunks lets an unrelated negative sentence in one source flip the
            // polarity of an otherwise supported positive claim (and vice versa).
            var supportingIds = ids
                .Where(id => !IsTitleOnlyClaim(claim.Text, evidenceById[id])
                    && IsSupportedByEvidence(claim.Text, evidenceById[id].Passage))
                .ToList();
            if (supportingIds.Count == 0)
            {
                warnings.Add($"Unsupported claim was removed: {claim.Text[..Math.Min(80, claim.Text.Length)]}");
                continue;
            }
            if (supportingIds.Count != ids.Count)
                warnings.Add($"Non-supporting evidence IDs were removed from claim: {claim.Text[..Math.Min(80, claim.Text.Length)]}");

            supported++;
            claims.Add(new RagClaim(claim.Text.Trim(), supportingIds));
        }

        var claimCount = (parsed.Claims ?? []).Count(x => !string.IsNullOrWhiteSpace(x.Text));
        var coverage = claimCount == 0 ? 0 : valid / (double)claimCount;
        var supportCoverage = claimCount == 0 ? 0 : supported / (double)claimCount;

        foreach (var id in CitationRegex().Matches(parsed.Answer ?? "").Select(m => m.Groups[1].Value).Distinct())
            if (!allowed.Contains(id)) warnings.Add($"Unknown citation {id} was rejected.");

        if (claims.Count == 0)
            return new(RefuseInsufficient, [], true, coverage, supportCoverage, "rejected_unsupported", warnings.Distinct().ToList());

        // The model's prose is not trusted independently from its structured claims. Rebuild the
        // user-visible answer exclusively from claims that survived evidence-id and support checks,
        // so uncited sentences cannot bypass validation through the free-form answer field.
        var answer = string.Join("\n", claims.Select(claim =>
            $"{claim.Text.Trim()} {string.Join(' ', claim.SourceIds.Select(id => $"[{id}]"))}"));

        var status = coverage == 1 && supportCoverage == 1 ? "lexically_grounded" : "partially_grounded";
        return new(answer, claims, false, coverage, supportCoverage, status, warnings.Distinct().ToList());
    }

    public static ValidatedRagAnswer? TryBuildExtractiveFallback(string question,
        IReadOnlyCollection<RagEvidence> evidence, int maxClaims = 4, string? reason = null)
    {
        var queryTokens = SignificantTokens(question);
        if (queryTokens.Count == 0) return null;

        var candidates = evidence.SelectMany(item =>
                EvidenceSentences(item.Passage)
                    .Select(sentence => MarkdownPrefixRegex().Replace(sentence.Trim(), "").Trim())
                    .Where(sentence => sentence.Length is >= 20 and <= 500)
                    .Where(sentence => !SameNormalizedText(sentence, item.Title))
                    .Where(sentence => ContentSecurityService.Assess(sentence).RiskLevel is not ("high" or "critical"))
                    .Select(sentence => new
                    {
                        item.SourceId,
                        Text = sentence,
                        Overlap = SignificantTokens(sentence).Count(queryTokens.Contains),
                        item.Score
                    }))
            .Where(x => x.Overlap > 0)
            .OrderByDescending(x => x.Overlap)
            .ThenByDescending(x => x.Score)
            .GroupBy(x => SlugHelper.Transliterate(x.Text).ToLowerInvariant(), StringComparer.Ordinal)
            .Select(x => x.First())
            .Take(Math.Clamp(maxClaims, 1, 8))
            .Select(x => new RagClaim(x.Text, [x.SourceId]))
            .ToList();

        if (candidates.Count == 0) return null;

        // These are verbatim, secret-redacted sentences selected from known evidence IDs, rather
        // than model-authored paraphrases. Numeric and negation consistency therefore hold by
        // construction, while the normal model path remains subject to the stricter validator.
        var answer = string.Join("\n", candidates.Select(claim =>
            $"{claim.Text} {string.Join(' ', claim.SourceIds.Select(id => $"[{id}]"))}"));
        return new ValidatedRagAnswer(answer, candidates, false, 1, 1, "extractive_fallback",
            [$"{reason ?? "Structured model output failed"}; returning query-relevant passages extracted from verified evidence."]);
    }

    private static ModelOutput? TryParse(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("```")) text = CodeFenceRegex().Replace(text, "").Trim();
        if (TryDeserialize(text, out var parsed)) return parsed;

        // Some local models prepend a short explanation or a thinking marker even when JSON mode
        // is requested. Accept only a complete JSON object from such a wrapper; the answer is still
        // rebuilt exclusively from evidence-bound claims below, so free text around it is ignored.
        ModelOutput? extracted = null;
        var containedJsonObject = false;
        foreach (var candidate in ExtractJsonObjects(text))
        {
            containedJsonObject = true;
            if (TryDeserialize(candidate, out parsed)) extracted = parsed;
        }
        if (extracted != null) return extracted;

        // A JSON-looking response with a broken contract must not be reinterpreted as prose merely
        // because a citation happens to occur inside one of its fields.
        return containedJsonObject ? null : TryParseCitedText(text);
    }

    private static bool TryDeserialize(string text, out ModelOutput? parsed)
    {
        try
        {
            parsed = JsonSerializer.Deserialize<ModelOutput>(text, Json);
            return parsed is { Answer: not null, Claims: not null, InsufficientContext: not null }
                && parsed.Claims.All(x => x is { Text: not null, SourceIds: not null });
        }
        catch (JsonException)
        {
            parsed = null;
            return false;
        }
    }

    private static IEnumerable<string> ExtractJsonObjects(string text)
    {
        for (var start = 0; start < text.Length; start++)
        {
            if (text[start] != '{') continue;
            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"') inString = true;
                else if (c == '{') depth++;
                else if (c == '}' && --depth == 0)
                {
                    yield return text[start..(i + 1)];
                    break;
                }
            }
        }
    }

    private static ModelOutput? TryParseCitedText(string text)
    {
        var visible = ThinkBlockRegex().Replace(text, " ");
        var claims = new List<RagClaim>();
        foreach (var part in visible.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var ids = CitationRegex().Matches(part).Select(x => x.Groups[1].Value)
                .Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count == 0) continue;

            var claim = CitationRegex().Replace(part, " ");
            claim = MarkdownPrefixRegex().Replace(claim.Trim(), "").Trim();
            if (!string.IsNullOrWhiteSpace(claim)) claims.Add(new RagClaim(claim, ids));
        }

        // Never recover uncited prose. A recovered claim still has to survive the same known-ID,
        // lexical-overlap, number and negation checks as a schema-produced claim.
        return claims.Count == 0 ? null : new ModelOutput(visible, claims, false, true);
    }

    private static bool IsSupportedByEvidence(string claim, string passage) =>
        SupportWindows(passage).Any(window => IsLexicallySupported(claim, window));

    private static bool IsTitleOnlyClaim(string claim, RagEvidence evidence) =>
        SameNormalizedText(claim, evidence.Title);

    private static bool SameNormalizedText(string left, string right) =>
        NormalizeComparableText(left) == NormalizeComparableText(right);

    private static string NormalizeComparableText(string value) => string.Join(' ',
        SlugHelper.Transliterate(CitationRegex().Replace(MarkdownPrefixRegex().Replace(value.Trim(), ""), " "))
            .ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '"', '\'' },
                StringSplitOptions.RemoveEmptyEntries));

    private static IEnumerable<string> SupportWindows(string passage)
    {
        foreach (var sentence in EvidenceSentences(passage))
        {
            // Keep the full sentence for claims whose support spans adjacent clauses, then add
            // contrast-separated clauses so an unrelated "ancak ... desteklenmez" predicate does
            // not contaminate the polarity check for the matching clause.
            yield return sentence;
            foreach (var clause in ClauseBoundaryRegex().Split(sentence)
                         .Select(x => x.Trim()).Where(x => x.Length >= 3 && x != sentence))
                yield return clause;
        }
    }

    private static IEnumerable<string> EvidenceSentences(string passage) =>
        SentenceBoundaryRegex().Split(passage)
            .Select(sentence => MarkdownPrefixRegex().Replace(sentence.Trim(), "").Trim())
            .Where(sentence => !string.IsNullOrWhiteSpace(sentence));

    private static bool IsLexicallySupported(string claim, string supportWindow)
    {
        var claimTokens = SignificantTokens(claim);
        if (claimTokens.Count == 0) return false;
        var passageTokens = SignificantTokens(supportWindow);
        if (claimTokens.Count(claimToken => passageTokens.Any(passageToken =>
                InflectionAwareTokenMatch(claimToken, passageToken))) / (double)claimTokens.Count < MinimumLexicalSupport)
            return false;

        var claimNumbers = NumberRegex().Matches(claim).Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        var passageNumbers = NumberRegex().Matches(supportWindow).Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        if (!claimNumbers.IsSubsetOf(passageNumbers)) return false;

        // A lexical overlap score alone treats "must" and "must not" as equivalent. Reject the
        // claim when explicit English/Turkish negation polarity differs from its local support.
        return HasNegation(claim) == HasNegation(supportWindow);
    }

    private static bool InflectionAwareTokenMatch(string left, string right)
    {
        if (left == right) return true;

        // Turkish suffixes frequently turn a supported source word into a different surface token
        // (for example "sağlayan"/"sağlar" or "çağırmayı"/"çağırmasını"). Accept only a long,
        // dominant shared stem; number and negation consistency are still checked separately below.
        var shorter = Math.Min(left.Length, right.Length);
        if (shorter < 5) return false;
        var common = 0;
        while (common < shorter && left[common] == right[common]) common++;
        return common >= 5 && common / (double)shorter >= .7;
    }

    private static bool HasNegation(string text) => NegationRegex().IsMatch(SlugHelper.Transliterate(text).ToLowerInvariant());

    private static HashSet<string> SignificantTokens(string text)
    {
        var stop = new HashSet<string>(StringComparer.Ordinal)
        {
            "ve", "veya", "ile", "için", "bir", "bu", "şu", "da", "de", "the", "a", "an", "and", "or", "for", "to", "of", "is", "are"
        };
        return SlugHelper.Transliterate(text).ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '"', '\'' },
                StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 3 && !stop.Contains(token))
            .ToHashSet(StringComparer.Ordinal);
    }

    [GeneratedRegex(@"\[(S\d+)\]")]
    private static partial Regex CitationRegex();
    [GeneratedRegex(@"\b\d+(?:[.,]\d+)?\b")]
    private static partial Regex NumberRegex();
    [GeneratedRegex(@"\b(?:not|never|no|degil|yok|hayir)\b|mamal[ıi]|memeli|maz\b|mez\b", RegexOptions.IgnoreCase)]
    private static partial Regex NegationRegex();
    [GeneratedRegex(@"^```(?:json)?\s*|\s*```$", RegexOptions.IgnoreCase)]
    private static partial Regex CodeFenceRegex();
    [GeneratedRegex(@"<think\b[^>]*>.*?</think>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ThinkBlockRegex();
    [GeneratedRegex(@"^(?:#{1,6}\s+|[-*+>]\s+|\d+[.)]\s+)")]
    private static partial Regex MarkdownPrefixRegex();
    [GeneratedRegex(@"(?<=[.!?])\s+|\r?\n+")]
    private static partial Regex SentenceBoundaryRegex();
    [GeneratedRegex(@"\s*(?:;|\b(?:ama|ancak|fakat|lakin|but|however)\b)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ClauseBoundaryRegex();
}
