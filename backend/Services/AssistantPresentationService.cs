using KnowledgePortal.Api.Models;

namespace KnowledgePortal.Api.Services;

public sealed record AssistantPresentedAnswer(string Answer, AssistantContentBlockDto[] Blocks);

/// <summary>
/// Renders only already-grounded claims into an allow-listed UI vocabulary. It never invents facts,
/// executes markup or performs calculations. The Markdown compatibility answer and typed blocks are
/// derived from the same source-bound claim set.
/// </summary>
public sealed class AssistantPresentationService
{
    public AssistantPresentedAnswer Present(string answer, AssistantRagDto? rag, string presentation)
    {
        var claims = rag?.Claims ?? [];
        if (claims.Length == 0 || presentation == AssistantPresentationModes.Auto)
            return new(answer, [new("markdown", Text: answer)]);

        static string Cite(AssistantClaimDto claim) => claim.SourceIds.Length == 0
            ? claim.Text.Trim()
            : $"{claim.Text.Trim()} {string.Join(' ', claim.SourceIds.Select(id => $"[{id}]"))}";

        if (presentation == AssistantPresentationModes.Summary)
        {
            var selected = claims.Take(Math.Min(2, claims.Length)).Select(Cite).ToArray();
            var markdown = string.Join("\n\n", selected);
            return new(markdown, [new("paragraph", Text: markdown)]);
        }

        if (presentation == AssistantPresentationModes.Table)
        {
            var rows = claims.Select((claim, index) => new[]
            {
                (index + 1).ToString(),
                EscapeCell(claim.Text),
                string.Join(' ', claim.SourceIds.Select(id => $"[{id}]"))
            }).ToArray();
            var markdown = "| # | Bilgi | Kaynak |\n|---:|---|---|\n" +
                           string.Join('\n', rows.Select(row => $"| {row[0]} | {row[1]} | {row[2]} |"));
            return new(markdown,
                [new("table", Title: "Doğrulanmış bilgiler", Headers: ["#", "Bilgi", "Kaynak"], Rows: rows)]);
        }

        if (presentation == AssistantPresentationModes.Infographic)
        {
            var infographicItems = claims.Select(Cite).ToArray();
            var markdown = string.Join('\n', infographicItems.Select(item => $"- {item}"));
            return new(markdown,
                [new("infographic", Title: "Öne çıkan bilgiler", Items: infographicItems)]);
        }

        var items = claims.Select(Cite).ToArray();
        if (presentation == AssistantPresentationModes.ProcessFlow)
        {
            var steps = claims.Where(claim => claim.Role == "step").Select(Cite).ToArray();
            if (steps.Length == 0) steps = items;
            var markdown = string.Join('\n', steps.Select((item, index) => $"{index + 1}. {item}"));
            return new(markdown, [new("process_flow", Title: "Süreç", Items: steps)]);
        }

        var ordered = presentation == AssistantPresentationModes.OrderedList;
        var listMarkdown = string.Join('\n', items.Select((item, index) =>
            ordered ? $"{index + 1}. {item}" : $"- {item}"));
        return new(listMarkdown, [new(ordered ? "ordered_list" : "bullet_list", Items: items)]);
    }

    private static string EscapeCell(string value) => value.Replace("|", "\\|")
        .Replace("\r", " ").Replace("\n", " ").Trim();
}
