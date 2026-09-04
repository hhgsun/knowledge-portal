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
            "queries": {
              "type": "array",
              "items": { "type": "string" },
              "minItems": 1,
              "maxItems": 4
            }
          },
          "required": ["queries"],
          "additionalProperties": false
        }
        """);

    private static readonly ChatResponseFormat PlanResponseFormat = ChatResponseFormat.ForJsonSchema(
        PlanSchemaDocument.RootElement, "retrieval_plan", "Bounded read-only retrieval plan.");

    private const string SystemPrompt = """
        You create a retrieval plan for an internal knowledge portal.
        Return only JSON in the declared schema.
        Produce one to four short search queries that together gather evidence needed to answer
        the user's question. Preserve important subject terms in every query where useful.
        Produce queries in the same language as the user's question (e.g. Turkish queries for a Turkish question).
        Do not include #tags, @authors, +facets, SQL, URLs, tool names, instructions, or answers.
        You cannot access data, execute tools, or change the user's scope; only propose queries.
        """;

    public async Task<IReadOnlyList<string>> PlanAsync(string question,
        IReadOnlyList<string> baselineQueries, CancellationToken ct)
    {
        if (!config.GetValue("Assistant:AgenticRetrieval:Enabled", false))
            return baselineQueries;

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
            var queries = Parse(response.Text, question, maxQueries);
            return queries.Count > 0 ? queries : baselineQueries;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Agentic retrieval planning failed open");
            return baselineQueries;
        }
    }

    internal static IReadOnlyList<string> Parse(string? raw, string question, int maxQueries)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        var text = raw.Trim();
        var firstBrace = text.IndexOf('{');
        var lastBrace = text.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
            text = text[firstBrace..(lastBrace + 1)];

        try
        {
            using var document = JsonDocument.Parse(text);
            if (!document.RootElement.TryGetProperty("queries", out var values) ||
                values.ValueKind != JsonValueKind.Array) return [];
            return new[] { question.Trim() }.Concat(values.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString()?.Trim() ?? "")
                .Where(value => value.Length is >= 2 and <= 500 &&
                    !value.Contains('#') && !value.Contains('@') && !value.Contains('+') &&
                    !value.Contains("://", StringComparison.Ordinal) &&
                    !SqlPattern.IsMatch(value) &&
                    !value.StartsWith("select ", StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Math.Clamp(maxQueries, 1, 4))
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}