using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public sealed record LlmModelOption(string Id, string Label);
public sealed record LlmModelSettings(
    IReadOnlyList<LlmModelOption> Models,
    string DefaultModel,
    string CatalogSource,
    string? CatalogWarning);

public sealed class LlmModelSelectionService(
    AppDbContext db,
    IConfiguration config,
    OllamaModelCatalogService catalog)
{
    public const string DefaultModelSettingKey = "llm.default_chat_model";

    public async Task<bool> IsAvailableAsync(string model, CancellationToken ct = default) =>
        await ResolveAsync(model, ct) != null;

    public async Task<string?> ResolveAsync(string model, CancellationToken ct = default) =>
        (await catalog.GetAsync(ct)).Models.FirstOrDefault(x =>
            string.Equals(x.Id, model.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;

    public async Task<string> GetDefaultModelAsync(CancellationToken ct = default)
    {
        var models = (await catalog.GetAsync(ct)).Models;
        var stored = await db.SystemSettings.AsNoTracking()
            .Where(x => x.Key == DefaultModelSettingKey).Select(x => x.Value).SingleOrDefaultAsync(ct);
        return Resolve(models, stored) ?? Resolve(models, ConfiguredDefault()) ?? models[0].Id;
    }

    public async Task<LlmModelSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        var discovered = await catalog.GetAsync(ct);
        var models = discovered.Models;
        var storedDefault = await db.SystemSettings.AsNoTracking()
            .Where(x => x.Key == DefaultModelSettingKey).Select(x => x.Value).SingleOrDefaultAsync(ct);
        var defaultModel = Resolve(models, storedDefault) ?? Resolve(models, ConfiguredDefault()) ?? models[0].Id;
        return new(models, defaultModel, discovered.Source, discovered.Warning);
    }

    public async Task SetDefaultModelAsync(string model, string updatedById, CancellationToken ct)
    {
        var canonical = await ResolveAsync(model, ct)
            ?? throw new ArgumentException("Model is not available from the Ollama catalog.", nameof(model));
        var setting = await db.SystemSettings.FindAsync([DefaultModelSettingKey], ct);
        if (setting == null)
        {
            setting = new SystemSetting { Key = DefaultModelSettingKey };
            db.SystemSettings.Add(setting);
        }
        setting.Value = canonical;
        setting.UpdatedById = updatedById;
        setting.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private string ConfiguredDefault() =>
        config["Ollama:ChatModel"]?.Trim() is { Length: > 0 } value ? value : "qwen2.5vl:7b";

    private static string? Resolve(IReadOnlyList<LlmModelOption> models, string? model) =>
        string.IsNullOrWhiteSpace(model) ? null : models.FirstOrDefault(x =>
            string.Equals(x.Id, model.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;
}
