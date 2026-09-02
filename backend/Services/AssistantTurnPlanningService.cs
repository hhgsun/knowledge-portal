using System.Text.Json;
using System.Text.RegularExpressions;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Models;

namespace KnowledgePortal.Api.Services;

public static class AssistantTurnActions
{
    public const string Retrieve = "retrieve";
    public const string TransformPrevious = "transform_previous";
    public const string Clarify = "clarify";
}

public static class AssistantPresentationModes
{
    public const string Auto = "auto";
    public const string Summary = "summary";
    public const string Bullets = "bullet_list";
    public const string OrderedList = "ordered_list";
    public const string Table = "comparison_table";
    public const string ProcessFlow = "process_flow";
    public const string Infographic = "infographic";
}

public sealed record AssistantStoredTurnState(
    string OriginalRequest,
    string NormalizedQuery,
    string Intent,
    string Presentation,
    string Answer,
    AssistantRagDto? Rag,
    string AnswerProfile,
    int Version = 1);

public sealed record AssistantTurnPlan(
    string OriginalMessage,
    string StandaloneQuery,
    string Action,
    string Intent,
    string Presentation,
    string Strategy,
    string? ClarificationQuestion = null,
    AssistantStoredTurnState? PreviousState = null,
    string? HypotheticalDocument = null);

/// <summary>
/// Deterministic control plane for an Assistant turn. Retrieval queries and response instructions
/// remain separate: presentation-only follow-ups reuse the prior verified state, while knowledge
/// follow-ups still go through the bounded contextualizer. This service makes no authorization
/// decision and never treats model output as an executable action.
/// </summary>
public sealed partial class AssistantTurnPlanningService(AssistantQueryContextualizer contextualizer)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AssistantTurnPlan> PlanAsync(string message,
        IReadOnlyList<AssistantConversationTurn> history, CancellationToken ct = default)
    {
        var original = Compact(message);
        var previous = PreviousState(history);
        var presentation = DetectPresentation(original);
        var intent = DetectIntent(original, presentation);

        if (IsPresentationOnly(original))
        {
            if (previous != null)
                return new(original, previous.NormalizedQuery, AssistantTurnActions.TransformPrevious,
                    intent, presentation, "deterministic_transform", PreviousState: previous);

            return new(original, original, AssistantTurnActions.Clarify, intent, presentation,
                "deterministic_clarification", ClarificationQuestion(original));
        }

        var contextualized = await contextualizer.ContextualizeAsync(original, history, ct);
        return new(original, contextualized.StandaloneQuery, AssistantTurnActions.Retrieve,
            intent, presentation, contextualized.Strategy, PreviousState: previous,
            HypotheticalDocument: contextualized.HypotheticalDocument);
    }

    internal static string DetectPresentation(string message)
    {
        var folded = Fold(message);
        if (TablePattern().IsMatch(folded)) return AssistantPresentationModes.Table;
        if (InfographicPattern().IsMatch(folded)) return AssistantPresentationModes.Infographic;
        if (FlowPattern().IsMatch(folded)) return AssistantPresentationModes.ProcessFlow;
        if (OrderedPattern().IsMatch(folded)) return AssistantPresentationModes.OrderedList;
        if (BulletPattern().IsMatch(folded)) return AssistantPresentationModes.Bullets;
        if (SummaryPattern().IsMatch(folded)) return AssistantPresentationModes.Summary;
        return AssistantPresentationModes.Auto;
    }

    internal static bool IsPresentationOnly(string message) =>
        PresentationOnlyPattern().IsMatch(Fold(message));

    private static string DetectIntent(string message, string presentation)
    {
        var folded = Fold(message);
        if (presentation == AssistantPresentationModes.Table || ComparePattern().IsMatch(folded))
            return "compare";
        if (presentation == AssistantPresentationModes.Infographic) return "summarize";
        if (presentation == AssistantPresentationModes.ProcessFlow) return "explain_process";
        if (presentation == AssistantPresentationModes.Summary) return "summarize";
        if (presentation is AssistantPresentationModes.OrderedList or AssistantPresentationModes.Bullets)
            return "list";
        if (AnalysisPattern().IsMatch(folded)) return "analyze";
        if (ProcedurePattern().IsMatch(folded)) return "procedure";
        if (ExplanationPattern().IsMatch(folded)) return "explain";
        return "answer";
    }

    private static AssistantStoredTurnState? PreviousState(
        IReadOnlyList<AssistantConversationTurn> history)
    {
        foreach (var turn in history.Reverse())
        {
            if (turn.Role != "assistant" || string.IsNullOrWhiteSpace(turn.TurnStateJson)) continue;
            try
            {
                var state = JsonSerializer.Deserialize<AssistantStoredTurnState>(turn.TurnStateJson, Json);
                if (state is { Version: 1, Rag.Claims.Length: > 0 } &&
                    !string.IsNullOrWhiteSpace(state.Answer)) return state;
            }
            catch (JsonException)
            {
                // Legacy/corrupt optional state must not break the conversation.
            }
        }
        return null;
    }

    private static string ClarificationQuestion(string message) => DetectPresentation(message) switch
    {
        AssistantPresentationModes.Table => "Hangi bilgileri tablo halinde göstermemi istersiniz?",
        AssistantPresentationModes.Infographic => "Hangi bilgileri infografik halinde göstermemi istersiniz?",
        AssistantPresentationModes.ProcessFlow => "Hangi süreci şema halinde göstermemi istersiniz?",
        AssistantPresentationModes.Summary => "Hangi içeriği özetlememi istersiniz?",
        _ => "Hangi konu veya bilgileri sıralamamı istersiniz?"
    };

    private static string Compact(string value) =>
        string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static string Fold(string value) => SlugHelper.Transliterate(Compact(value)).ToLowerInvariant();

    [GeneratedRegex(@"\b(?:tablo|matris)\b|tabloya\s+dok|tablo\s+halinde", RegexOptions.None, 100)]
    private static partial Regex TablePattern();
    [GeneratedRegex(@"\b(?:infografik|bilgi\s+kartlari)\b", RegexOptions.None, 100)]
    private static partial Regex InfographicPattern();
    [GeneratedRegex(@"\b(?:sema|diyagram|akis\s+semasi|semalastir)\b", RegexOptions.None, 100)]
    private static partial Regex FlowPattern();
    [GeneratedRegex(@"\b(?:sirala|numaralandir|numarali\s+liste)\b", RegexOptions.None, 100)]
    private static partial Regex OrderedPattern();
    [GeneratedRegex(@"\b(?:listele|maddele|maddeler\s+halinde|madde\s+madde)\b", RegexOptions.None, 100)]
    private static partial Regex BulletPattern();
    [GeneratedRegex(@"\b(?:ozetle|ozet\s+gec|kisalt|kisa\s+ozet|iki\s+cumle)\b", RegexOptions.None, 100)]
    private static partial Regex SummaryPattern();
    [GeneratedRegex(@"\b(?:karsilastir|farklari|kiyasla|versus|vs)\b", RegexOptions.None, 100)]
    private static partial Regex ComparePattern();
    [GeneratedRegex(@"\b(?:analiz|analiz\s+et|egilim|trend|hesapla|topla|ortalama)\b", RegexOptions.None, 100)]
    private static partial Regex AnalysisPattern();
    [GeneratedRegex(@"\b(?:nasil|adimlari|kurulum|uygula|uygulanir|prosedur)\b", RegexOptions.None, 100)]
    private static partial Regex ProcedurePattern();
    [GeneratedRegex(@"\b(?:acikla|anlat|neden|nedir|detaylandir|ayrintilandir|what\s+is)\b", RegexOptions.None, 100)]
    private static partial Regex ExplanationPattern();
    [GeneratedRegex(@"^(?:(?:lutfen|peki|bunu|bunlari|cevabi|yaniti|yukaridakileri)\s+)*(?:(?:sirala|listele|maddele|numaralandir|ozetle|kisalt|semalastir)|(?:tablo|tabloya\s+dok|tablo\s+halinde|infografik|bilgi\s+kartlari|akis\s+semasi|madde\s+madde|maddeler\s+halinde|ozet\s+gec|iki\s+cumlede\s+ozetle))(?:\s+(?:lutfen|tekrar|yeniden|halinde|olarak|yap|goster|ver))?[?.!]*$", RegexOptions.None, 100)]
    private static partial Regex PresentationOnlyPattern();
}
