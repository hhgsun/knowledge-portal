using System.Net.Http.Json;
using System.Text.Json;

namespace KnowledgePortal.Api.Services;

public sealed record OllamaModelCatalogResult(
    IReadOnlyList<LlmModelOption> Models,
    string Source,
    string? Warning = null);

/// <summary>Discovers chat-capable models from Ollama and keeps a last-known-good catalog.</summary>
public sealed class OllamaModelCatalogService(
    HttpClient client,
    IConfiguration config,
    ILogger<OllamaModelCatalogService> logger) : IDisposable
{
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private OllamaModelCatalogResult? cached;
    private IReadOnlyList<LlmModelOption>? lastSuccessful;
    private DateTimeOffset expiresAt;

    public async Task<OllamaModelCatalogResult> GetAsync(CancellationToken ct = default)
    {
        if (cached != null && DateTimeOffset.UtcNow < expiresAt) return cached;
        await refreshLock.WaitAsync(ct);
        try
        {
            if (cached != null && DateTimeOffset.UtcNow < expiresAt) return cached;
            try
            {
                using var response = await client.GetAsync("api/tags", ct);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var candidates = ParseCandidates(document.RootElement)
                    .Where(model => !IsConfiguredEmbeddingModel(model.Id) && !LooksEmbeddingOnly(model.Id))
                    .DistinctBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var verified = await FilterChatCapableAsync(candidates, ct);
                if (verified.Count == 0)
                    throw new InvalidOperationException("Ollama returned no chat-capable models.");

                lastSuccessful = verified;
                cached = new(verified, "ollama");
                expiresAt = DateTimeOffset.UtcNow.AddSeconds(
                    Math.Clamp(config.GetValue("Ollama:ModelCatalogCacheSeconds", 60), 5, 3600));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Ollama model catalog discovery failed");
                var fallback = lastSuccessful ?? [ConfiguredFallback()];
                cached = new(fallback, lastSuccessful == null ? "configured_fallback" : "stale_cache",
                    "Ollama model catalog is temporarily unavailable; a cached or configured fallback is shown.");
                expiresAt = DateTimeOffset.UtcNow.AddSeconds(
                    Math.Clamp(config.GetValue("Ollama:ModelCatalogFailureCacheSeconds", 15), 2, 300));
            }
            return cached;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    public void Dispose()
    {
        client.Dispose();
        refreshLock.Dispose();
    }

    private async Task<List<LlmModelOption>> FilterChatCapableAsync(
        IReadOnlyList<LlmModelOption> candidates, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(Math.Clamp(
            config.GetValue("Ollama:ModelCatalogCapabilityConcurrency", 4), 1, 16));
        var checks = candidates.Select(async model =>
        {
            await gate.WaitAsync(ct);
            try
            {
                using var response = await client.PostAsJsonAsync("api/show", new { model = model.Id }, ct);
                if (!response.IsSuccessStatusCode) return model;
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                if (!document.RootElement.TryGetProperty("capabilities", out var capabilities)
                    || capabilities.ValueKind != JsonValueKind.Array)
                    return model;
                return capabilities.EnumerateArray().Any(value =>
                    string.Equals(value.GetString(), "completion", StringComparison.OrdinalIgnoreCase))
                    ? model : null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Older/proxied Ollama deployments may not expose capabilities; tags plus
                // embedding-name filtering remains a backwards-compatible discovery path.
                return model;
            }
            finally
            {
                gate.Release();
            }
        });
        return (await Task.WhenAll(checks)).Where(model => model != null).Cast<LlmModelOption>()
            .OrderBy(model => model.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<LlmModelOption> ParseCandidates(JsonElement root)
    {
        if (!root.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (var item in models.EnumerateArray())
        {
            var id = ReadString(item, "model") ?? ReadString(item, "name");
            if (string.IsNullOrWhiteSpace(id)) continue;
            var details = item.TryGetProperty("details", out var value) ? value : default;
            var parameterSize = details.ValueKind == JsonValueKind.Object
                ? ReadString(details, "parameter_size") : null;
            var quantization = details.ValueKind == JsonValueKind.Object
                ? ReadString(details, "quantization_level") : null;
            var suffix = string.Join(" · ", new[] { parameterSize, quantization }
                .Where(text => !string.IsNullOrWhiteSpace(text)));
            yield return new(id.Trim(), suffix.Length == 0 ? id.Trim() : $"{id.Trim()} · {suffix}");
        }
    }

    private bool IsConfiguredEmbeddingModel(string model) => string.Equals(model,
        config["Ollama:EmbeddingModel"]?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool LooksEmbeddingOnly(string model)
    {
        var value = model.ToLowerInvariant();
        return value.Contains("embed", StringComparison.Ordinal)
               || value.Contains("embedding", StringComparison.Ordinal)
               || value.StartsWith("bge-", StringComparison.Ordinal)
               || value.StartsWith("nomic-embed", StringComparison.Ordinal)
               || value.StartsWith("mxbai-embed", StringComparison.Ordinal)
               || value.StartsWith("all-minilm", StringComparison.Ordinal);
    }

    private LlmModelOption ConfiguredFallback()
    {
        var model = config["Ollama:ChatModel"]?.Trim() is { Length: > 0 } value
            ? value : "qwen2.5vl:7b";
        return new(model, model);
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
}
