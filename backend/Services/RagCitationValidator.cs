using System.Text.Json;
using System.Text.RegularExpressions;

namespace KnowledgePortal.Api.Services;

public record RagClaim(string Text, List<string> SourceIds);
public record RagEvidence(string SourceId, string ArticleId, string Title, string Slug, string SourceType,
    string? AttachmentId, string? SourceName, string? SourceLocation, string Passage, double Score);
public record ValidatedRagAnswer(string Answer, List<RagClaim> Claims, bool InsufficientContext,
    double CitationCoverage, string GroundingStatus, List<string> Warnings);

public static partial class RagCitationValidator
{
    private sealed record ModelOutput(string? Answer, List<RagClaim>? Claims, bool InsufficientContext);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static ValidatedRagAnswer Validate(string raw, IReadOnlyCollection<RagEvidence> evidence)
    {
        var warnings = new List<string>();
        var parsed = TryParse(raw);
        if (parsed == null)
            return new(raw, [], false, 0, "unverified", ["Model did not return the required structured JSON output."]);

        var allowed = evidence.Select(x => x.SourceId).ToHashSet(StringComparer.Ordinal);
        var claims = new List<RagClaim>();
        var valid = 0;
        foreach (var claim in parsed.Claims ?? [])
        {
            if (string.IsNullOrWhiteSpace(claim.Text)) continue;
            var ids = claim.SourceIds.Where(allowed.Contains).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count != claim.SourceIds.Count)
                warnings.Add($"Claim contained unknown evidence IDs: {claim.Text[..Math.Min(80, claim.Text.Length)]}");
            if (ids.Count > 0) valid++;
            claims.Add(new RagClaim(claim.Text.Trim(), ids));
        }

        var coverage = claims.Count == 0 ? (parsed.InsufficientContext ? 1 : 0) : valid / (double)claims.Count;
        var answer = parsed.Answer?.Trim() ?? "";
        foreach (var id in CitationRegex().Matches(answer).Select(m => m.Groups[1].Value).Distinct())
            if (!allowed.Contains(id)) { answer = answer.Replace($"[{id}]", "", StringComparison.Ordinal); warnings.Add($"Unknown citation {id} was removed."); }

        var status = parsed.InsufficientContext ? "insufficient_context"
            : claims.Count == 0 ? "unverified"
            : coverage == 1 ? "citations_verified"
            : coverage > 0 ? "partially_verified" : "failed";
        return new(answer, claims, parsed.InsufficientContext, coverage, status, warnings.Distinct().ToList());
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

    [GeneratedRegex(@"\[(S\d+)\]")]
    private static partial Regex CitationRegex();
    [GeneratedRegex(@"^```(?:json)?\s*|\s*```$", RegexOptions.IgnoreCase)]
    private static partial Regex CodeFenceRegex();
}
