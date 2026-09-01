using KnowledgePortal.Api.Helpers;

namespace KnowledgePortal.Api.Services;

public enum RagAnswerProfile
{
    Compact,
    Balanced,
    Comprehensive
}

/// <summary>
/// Normalizes the transport-level answer profile and promotes explicitly comprehensive questions
/// when the caller leaves profile selection on the default. Explicit caller choices always win.
/// </summary>
public static class RagAnswerProfiles
{
    public static readonly string[] Allowed = ["compact", "balanced", "comprehensive"];

    public static bool TryParse(string? value, out RagAnswerProfile profile)
    {
        profile = RagAnswerProfile.Balanced;
        if (string.IsNullOrWhiteSpace(value)) return true;
        return value.Trim().ToLowerInvariant() switch
        {
            "compact" => Set(RagAnswerProfile.Compact, out profile),
            "balanced" => Set(RagAnswerProfile.Balanced, out profile),
            "comprehensive" => Set(RagAnswerProfile.Comprehensive, out profile),
            _ => false
        };
    }

    public static RagAnswerProfile Resolve(string question, string? requested, string? configuredDefault = null)
    {
        if (!string.IsNullOrWhiteSpace(requested) && TryParse(requested, out var explicitProfile))
            return explicitProfile;

        var folded = SlugHelper.Transliterate(question).ToLowerInvariant();
        if (ComprehensiveSignals.Any(signal => folded.Contains(signal, StringComparison.Ordinal)))
            return RagAnswerProfile.Comprehensive;

        return TryParse(configuredDefault, out var defaultProfile)
            ? defaultProfile
            : RagAnswerProfile.Balanced;
    }

    public static string ToWireValue(this RagAnswerProfile profile) => profile switch
    {
        RagAnswerProfile.Compact => "compact",
        RagAnswerProfile.Comprehensive => "comprehensive",
        _ => "balanced"
    };

    public static string PromptInstruction(this RagAnswerProfile profile) => profile switch
    {
        RagAnswerProfile.Compact =>
            "Answer profile: COMPACT. Produce 1-4 distinct supported claims. Give the direct answer first and include only essential qualifications.",
        RagAnswerProfile.Comprehensive =>
            "Answer profile: COMPREHENSIVE. When evidence permits, produce 8-15 distinct supported claims covering the conclusion, main facets, workflow, reasons, defaults, constraints, exceptions, conflicts, trade-offs, and operational implications. Avoid repetition.",
        _ =>
            "Answer profile: BALANCED. When evidence permits, produce 5-8 distinct supported claims: a direct conclusion followed by the most useful explanation, steps, constraints, defaults, and exceptions. Avoid repetition."
    };

    private static readonly string[] ComprehensiveSignals =
    [
        "kapsamli", "detayli", "ayrintili", "derinlemesine", "tum yonleri", "butun yonleri",
        "comprehensive", "in detail", "detailed", "deep dive", "end to end", "end-to-end"
    ];

    private static bool Set(RagAnswerProfile value, out RagAnswerProfile target)
    {
        target = value;
        return true;
    }
}
