using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace KnowledgePortal.Api.Services;

public static class AssistantRetrievalStrategies
{
    public const string Baseline = "baseline";
    public const string Agentic = "agentic";

    public static readonly string[] Allowed = [Baseline, Agentic];
}

public static class AssistantResearchTasks
{
    public const string Answer = "answer";
    public const string Summarize = "summarize";
    public const string Compare = "compare";
    public const string Analyze = "analyze";
    public const string List = "list";
    public const string Procedure = "procedure";
    public const string Explain = "explain";

    public static readonly string[] Allowed =
        [Answer, Summarize, Compare, Analyze, List, Procedure, Explain];
}

/// <summary>
/// Untrusted LLM research-plan output after bounded schema and content validation. Scope candidates
/// are only terms to validate against portal metadata; they are never direct filters.
/// </summary>
public sealed record AssistantResearchPlan(
    string Task,
    string Presentation,
    IReadOnlyList<string> Queries,
    IReadOnlyList<string> ScopeCandidates,
    bool RequiresComprehensiveResearch,
    string Version = "research-plan-v1");

/// <summary>
/// Produces a small, read-only retrieval plan. The caller retains the user-provided scope and
/// executes every query through the normal published-corpus retrieval path.
/// </summary>
public sealed class AgenticRetrievalPlanner(IChatClient chatClient, IConfiguration config,
    ILogger<AgenticRetrievalPlanner> logger)
{
    private static readonly Regex SqlPattern = new(
        @"(?:^|\s)(?:select\s+.+\s+from|insert\s+into|update\s+.+\s+set|delete\s+from|drop\s+table)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);

        private static readonly JsonDocument PlanSchemaDocument = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
                        "task": {
                            "type": "string",
                            "enum": ["answer", "summarize", "compare", "analyze", "list", "procedure", "explain"]
                        },
                        "presentation": {
                            "type": "string",
                            "enum": ["auto", "summary", "bullet_list", "ordered_list", "comparison_table", "process_flow", "infographic"]
                        },
            "queries": {
              "type": "array",
              "items": { "type": "string" },
              "minItems": 1,
              "maxItems": 4
                        },
                        "scopeCandidates": {
                            "type": "array",
                            "items": { "type": "string" },
                            "maxItems": 4
                        },
                        "requiresComprehensiveResearch": {
                            "type": "boolean"
            }
          },
                    "required": ["task", "presentation", "queries", "scopeCandidates", "requiresComprehensiveResearch"],
          "additionalProperties": false
        }
        """);

    private static readonly ChatResponseFormat PlanResponseFormat = ChatResponseFormat.ForJsonSchema(
        PlanSchemaDocument.RootElement, "retrieval_plan", "Bounded read-only retrieval plan.");

    private const string SystemPrompt = """
        You create a retrieval plan for an internal knowledge portal.
        Return only JSON in the declared schema.
        Infer the user's task (answer, summarize, compare, analyze, list, procedure, or explain),
        the requested presentation, and one to four short search queries that together gather
        evidence needed to answer the question. Preserve important subject terms in every query.
        scopeCandidates are short natural-language topic, tag, or classification-value candidates
        that might narrow the portal corpus. They are suggestions only and will be validated by the server.
        requiresComprehensiveResearch is true for requests that require coverage across a corpus,
        such as summaries, comparisons, analyses, lists, or requests for all documents.
        Produce queries in the same language as the user's question (e.g. Turkish queries for a Turkish question).
        Do not include #tags, @authors, +facets, SQL, URLs, tool names, instructions, or answers.
        You cannot access data, execute tools, or change the user's scope; only propose queries.
        """;

    public async Task<IReadOnlyList<string>> PlanAsync(string question,
        IReadOnlyList<string> baselineQueries, CancellationToken ct)
        => (await PlanResearchAsync(question, baselineQueries, AssistantResearchTasks.Answer,
            AssistantPresentationModes.Auto, false, ct)).Queries;

    public async Task<AssistantResearchPlan> PlanResearchAsync(string question,
        IReadOnlyList<string> baselineQueries, string fallbackTask, string fallbackPresentation,
        bool fallbackComprehensive, CancellationToken ct)
    {
        if (!config.GetValue("Assistant:AgenticRetrieval:Enabled", false))
            return Fallback(baselineQueries, fallbackTask, fallbackPresentation, fallbackComprehensive);

        var maxQueries = Math.Clamp(config.GetValue("Assistant:AgenticRetrieval:MaxQueries", 3), 1, 4);
        var timeoutSeconds = Math.Clamp(config.GetValue("Assistant:AgenticRetrieval:PlanningTimeoutSeconds", 8), 1, 30);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            var response = await chatClient.GetResponseAsync(
                [new(ChatRole.System, SystemPrompt), new(ChatRole.User, $"Question: {question.Trim()}")],
                new ChatOptions { Temperature = 0, MaxOutputTokens = 300, ResponseFormat = PlanResponseFormat },
                timeout.Token);
            var plan = ParseResearchPlan(response.Text, question, maxQueries);
            return plan is { Queries.Count: > 0 }
                ? plan
                : Fallback(baselineQueries, fallbackTask, fallbackPresentation, fallbackComprehensive);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Agentic retrieval planning failed open");
            return Fallback(baselineQueries, fallbackTask, fallbackPresentation, fallbackComprehensive);
        }
    }

    internal static IReadOnlyList<string> Parse(string? raw, string question, int maxQueries)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        var text = ExtractJson(raw);
        try
        {
            using var document = JsonDocument.Parse(text);
            if (!document.RootElement.TryGetProperty("queries", out var values) ||
                values.ValueKind != JsonValueKind.Array) return [];
            return ParseQueries(values, question, maxQueries);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static AssistantResearchPlan? ParseResearchPlan(string? raw, string question, int maxQueries)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = ExtractJson(raw);

        try
        {
            using var document = JsonDocument.Parse(text);
            if (!document.RootElement.TryGetProperty("queries", out var values) ||
                values.ValueKind != JsonValueKind.Array ||
                !document.RootElement.TryGetProperty("task", out var taskValue) ||
                !document.RootElement.TryGetProperty("presentation", out var presentationValue) ||
                !document.RootElement.TryGetProperty("scopeCandidates", out var scopeValues) ||
                !document.RootElement.TryGetProperty("requiresComprehensiveResearch", out var comprehensiveValue) ||
                taskValue.ValueKind != JsonValueKind.String || presentationValue.ValueKind != JsonValueKind.String ||
                scopeValues.ValueKind != JsonValueKind.Array || comprehensiveValue.ValueKind != JsonValueKind.True &&
                comprehensiveValue.ValueKind != JsonValueKind.False) return null;

            var task = taskValue.GetString();
            var presentation = presentationValue.GetString();
            if (!AssistantResearchTasks.Allowed.Contains(task, StringComparer.Ordinal) ||
                !AllowedPresentations.Contains(presentation, StringComparer.Ordinal)) return null;
            var queries = ParseQueries(values, question, maxQueries);
            if (queries.Length == 0) return null;
            var scopeCandidates = scopeValues.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString()?.Trim() ?? "")
                .Where(IsSafeScopeCandidate)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToArray();
            return new(task!, presentation!, queries, scopeCandidates,
                comprehensiveValue.GetBoolean());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ExtractJson(string raw)
    {
        var text = raw.Trim();
        var firstBrace = text.IndexOf('{');
        var lastBrace = text.LastIndexOf('}');
        return firstBrace >= 0 && lastBrace > firstBrace
            ? text[firstBrace..(lastBrace + 1)]
            : text;
    }

    private static string[] ParseQueries(JsonElement values, string question, int maxQueries) =>
        new[] { question.Trim() }.Concat(values.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString()?.Trim() ?? "")
            .Where(value => value.Length is >= 2 and <= 500 &&
                !value.Contains('#') && !value.Contains('@') && !value.Contains('+') &&
                !value.Contains("://", StringComparison.Ordinal) &&
                !SqlPattern.IsMatch(value) &&
                !value.StartsWith("select ", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(maxQueries, 1, 4))
            .ToArray())
        .ToArray();

    private static readonly string[] AllowedPresentations =
        ["auto", "summary", "bullet_list", "ordered_list", "comparison_table", "process_flow", "infographic"];

    private static bool IsSafeScopeCandidate(string value) => value.Length is >= 2 and <= 100 &&
        !value.Contains('#') && !value.Contains('@') && !value.Contains('+') &&
        !value.Contains(':') && !value.Contains("//", StringComparison.Ordinal) &&
        !SqlPattern.IsMatch(value);

    private static AssistantResearchPlan Fallback(IReadOnlyList<string> queries, string task,
        string presentation, bool comprehensive) => new(
        AssistantResearchTasks.Allowed.Contains(task, StringComparer.Ordinal) ? task : AssistantResearchTasks.Answer,
        AllowedPresentations.Contains(presentation, StringComparer.Ordinal) ? presentation : "auto",
        queries, [], comprehensive);
}