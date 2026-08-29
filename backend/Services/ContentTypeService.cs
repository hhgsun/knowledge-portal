using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

/// <summary>Validates the legacy contentType field while the generic classification API is adopted.</summary>
public sealed class ContentTypeService(AppDbContext db)
{
    public const string DefaultValue = "reference";
    private Dictionary<string, string>? _activeValues;

    public async Task<IReadOnlyCollection<string>> GetActiveValuesAsync(CancellationToken ct = default)
        => (await LoadAsync(ct)).Values.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public async Task<(string? Value, ServiceError? Error)> ResolveAsync(
        string? requested,
        CancellationToken ct = default)
    {
        var candidate = string.IsNullOrWhiteSpace(requested) ? DefaultValue : requested.Trim();
        var active = await LoadAsync(ct);
        if (active.Count == 0) return (candidate, null);
        return active.TryGetValue(candidate, out var canonical)
            ? (canonical, null)
            : (null, new ServiceError(400,
                $"Invalid contentType. Allowed: {string.Join(", ", active.Values.Order(StringComparer.OrdinalIgnoreCase))}"));
    }

    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken ct)
    {
        if (_activeValues != null) return _activeValues;

        _activeValues = (await db.LookupValues
                .Where(value => value.Category == "content_type" && value.IsActive)
                .Select(value => value.Value)
                .ToListAsync(ct))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(value => value, value => value, StringComparer.OrdinalIgnoreCase);
        return _activeValues;
    }
}
