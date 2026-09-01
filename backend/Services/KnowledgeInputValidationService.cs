namespace KnowledgePortal.Api.Services;

/// <summary>
/// Shared, transport-independent limits for grounded knowledge requests.
/// REST Assistant and MCP both call this service so adapters cannot bypass the
/// canonical question and scope budgets.
/// </summary>
public sealed class KnowledgeInputValidationService(IConfiguration config)
{
    public int MaxQuestionCharacters =>
        Math.Clamp(config.GetValue("Assistant:MaxMessageCharacters", 4000), 100, 20_000);

    public int MaxScopeItems =>
        Math.Clamp(config.GetValue("KnowledgeInput:MaxScopeItems", 50), 1, 200);

    public int MaxScopeValueCharacters =>
        Math.Clamp(config.GetValue("KnowledgeInput:MaxScopeValueCharacters", 200), 20, 500);

    public ServiceError? ValidateQuestion(string? question, string fieldName = "Question")
    {
        if (string.IsNullOrWhiteSpace(question))
            return new ServiceError(400, $"{fieldName} is required.");
        if (question.Length > MaxQuestionCharacters)
            return new ServiceError(400,
                $"{fieldName} cannot exceed {MaxQuestionCharacters} characters.");
        return null;
    }

    public ServiceError? ValidateAnswerProfile(string? answerProfile)
    {
        if (RagAnswerProfiles.TryParse(answerProfile, out _)) return null;
        return new ServiceError(400,
            $"Answer profile must be one of: {string.Join(", ", RagAnswerProfiles.Allowed)}.");
    }

    public ServiceError? ValidateScope(
        IEnumerable<string>? tags,
        IEnumerable<string>? authors,
        IEnumerable<string>? contentTypes,
        IReadOnlyDictionary<string, string[]>? facets)
    {
        var error = ValidateValues("Tags", tags);
        if (error != null) return error;
        error = ValidateValues("Authors", authors);
        if (error != null) return error;
        error = ValidateValues("Content types", contentTypes);
        if (error != null) return error;

        if (facets == null) return null;
        if (facets.Count > MaxScopeItems)
            return new ServiceError(400, $"Facets cannot contain more than {MaxScopeItems} categories.");
        foreach (var (category, values) in facets)
        {
            if (string.IsNullOrWhiteSpace(category) || category.Length > MaxScopeValueCharacters)
                return new ServiceError(400,
                    $"Facet category names must contain 1-{MaxScopeValueCharacters} characters.");
            error = ValidateValues($"Facet '{category}' values", values);
            if (error != null) return error;
        }
        return null;
    }

    private ServiceError? ValidateValues(string fieldName, IEnumerable<string>? values)
    {
        if (values == null) return null;
        var materialized = values.Take(MaxScopeItems + 1).ToArray();
        if (materialized.Length > MaxScopeItems)
            return new ServiceError(400, $"{fieldName} cannot contain more than {MaxScopeItems} values.");
        if (materialized.Any(value => string.IsNullOrWhiteSpace(value)
                                      || value.Length > MaxScopeValueCharacters))
            return new ServiceError(400,
                $"{fieldName} values must contain 1-{MaxScopeValueCharacters} characters.");
        return null;
    }
}
