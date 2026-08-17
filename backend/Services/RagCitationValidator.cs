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
    private sealed record ModelOutput(string? Answer, List<RagClaim>? Claims, bool InsufficientContext);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ValidatedRagAnswer Validate(string raw, IReadOnlyCollection<RagEvidence> evidence)
    {
        var warnings = new List<string>();
        var parsed = TryParse(raw);
        if (parsed == null)
            return new(raw, [], false, 0, 0, "unverified", ["Model did not return the required structured JSON output."]);

        var allowed = evidence.Select(x => x.SourceId).ToHashSet(StringComparer.Ordinal);
        var evidenceById = evidence.ToDictionary(x => x.SourceId, StringComparer.Ordinal);
        var claims = new List<RagClaim>();
        var valid = 0;
        var supported = 0;
        foreach (var claim in parsed.Claims ?? [])
        {
            if (string.IsNullOrWhiteSpace(claim.Text)) continue;
            var ids = claim.SourceIds.Where(allowed.Contains).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count != claim.SourceIds.Count)
                warnings.Add($"Claim contained unknown evidence IDs: {claim.Text[..Math.Min(80, claim.Text.Length)]}");
            if (ids.Count > 0) valid++;
            if (ids.Count > 0 && IsLexicallySupported(claim.Text,
                    string.Join(' ', ids.Select(id => evidenceById[id].Passage))))
                supported++;
            claims.Add(new RagClaim(claim.Text.Trim(), ids));
        }

        var coverage = claims.Count == 0 ? (parsed.InsufficientContext ? 1 : 0) : valid / (double)claims.Count;
        var supportCoverage = claims.Count == 0 ? (parsed.InsufficientContext ? 1 : 0) : supported / (double)claims.Count;
        var answer = parsed.Answer?.Trim() ?? "";
        foreach (var id in CitationRegex().Matches(answer).Select(m => m.Groups[1].Value).Distinct())
            if (!allowed.Contains(id)) { answer = answer.Replace($"[{id}]", "", StringComparison.Ordinal); warnings.Add($"Unknown citation {id} was removed."); }

        var status = parsed.InsufficientContext ? "insufficient_context"
            : claims.Count == 0 ? "unverified"
            : coverage < 1 ? (coverage > 0 ? "partially_cited" : "failed")
            : supportCoverage == 1 ? "lexically_grounded"
            : supportCoverage > 0 ? "partially_grounded"
            : "citation_ids_verified";
        if (coverage == 1 && supportCoverage < 1)
            warnings.Add("One or more cited claims lack sufficient lexical support in their cited passages.");
        return new(answer, claims, parsed.InsufficientContext, coverage, supportCoverage, status, warnings.Distinct().ToList());
    }

    private static ModelOutput? TryParse(string raw)
    {
        try
        {
            var text = raw.Trim();
            if (text.StartsWith("```")) text = CodeFenceRegex().Replace(text, "").Trim();
            return JsonSerializer.Deserialize<ModelOutput>(text, Json);
        }
        catch (JsonException) { return null; }
    }

    private static bool IsLexicallySupported(string claim, string passage)
    {
        var claimTokens = SignificantTokens(claim);
        if (claimTokens.Count == 0) return false;
        var passageTokens = SignificantTokens(passage);
        return claimTokens.Count(passageTokens.Contains) / (double)claimTokens.Count >= .35;
    }

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
    [GeneratedRegex(@"^```(?:json)?\s*|\s*```$", RegexOptions.IgnoreCase)]
    private static partial Regex CodeFenceRegex();
}
