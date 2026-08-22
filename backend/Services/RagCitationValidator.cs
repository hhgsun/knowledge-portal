using System.Text.Json;
using System.Text.RegularExpressions;
using KnowledgePortal.Api.Helpers;

namespace KnowledgePortal.Api.Services;

public record RagClaim(string Text, List<string> SourceIds);
public record RagEvidence(string SourceId, string ArticleId, string Title, string Slug, string SourceType,
    string? AttachmentId, string? SourceName, string? SourceLocation, string Passage, double Score);
public record ValidatedRagAnswer(string Answer, List<RagClaim> Claims, bool InsufficientContext,
    double CitationCoverage, double ClaimSupportCoverage, string GroundingStatus, List<string> Warnings);

public static partial class RagCitationValidator
{
    private const string RefuseInsufficient = "Bu konuda yeterli bilgi bulamadım.";
    private const double MinimumLexicalSupport = .65;
    private sealed record ModelOutput(string? Answer, List<RagClaim>? Claims, bool? InsufficientContext);
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

        if (parsed.InsufficientContext == true)
            return new(RefuseInsufficient, [], true, 1, 1, "insufficient_context", []);

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

            var passage = string.Join(' ', ids.Select(id => evidenceById[id].Passage));
            if (!IsLexicallySupported(claim.Text, passage))
            {
                warnings.Add($"Unsupported claim was removed: {claim.Text[..Math.Min(80, claim.Text.Length)]}");
                continue;
            }

            supported++;
            claims.Add(new RagClaim(claim.Text.Trim(), ids));
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

    private static ModelOutput? TryParse(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("```")) text = CodeFenceRegex().Replace(text, "").Trim();
        if (TryDeserialize(text, out var parsed)) return parsed;

        // Some local models prepend a short explanation or a thinking marker even when JSON mode
        // is requested. Accept only a complete JSON object from such a wrapper; the answer is still
        // rebuilt exclusively from evidence-bound claims below, so free text around it is ignored.
        ModelOutput? extracted = null;
        foreach (var candidate in ExtractJsonObjects(text))
            if (TryDeserialize(candidate, out parsed)) extracted = parsed;
        return extracted;
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

    private static bool IsLexicallySupported(string claim, string passage)
    {
        var claimTokens = SignificantTokens(claim);
        if (claimTokens.Count == 0) return false;
        var passageTokens = SignificantTokens(passage);
        if (claimTokens.Count(passageTokens.Contains) / (double)claimTokens.Count < MinimumLexicalSupport)
            return false;

        var claimNumbers = NumberRegex().Matches(claim).Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        var passageNumbers = NumberRegex().Matches(passage).Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        if (!claimNumbers.IsSubsetOf(passageNumbers)) return false;

        // A lexical overlap score alone treats "must" and "must not" as equivalent. Reject the
        // claim when explicit English/Turkish negation polarity differs from its cited passage.
        return HasNegation(claim) == HasNegation(passage);
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
}
