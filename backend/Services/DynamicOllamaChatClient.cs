using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using OllamaSharp;

namespace KnowledgePortal.Api.Services;

/// <summary>Routes each scoped chat call to the user's effective, Ollama-discovered model.</summary>
public sealed class DynamicOllamaChatClient(
    LlmModelSelectionService selection,
    IHttpContextAccessor httpContextAccessor,
    OllamaChatClientFactory factory) : IChatClient
{
    private string? effectiveModel;

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync(cancellationToken);
        return await ((IChatClient)client).GetResponseAsync(messages, options, cancellationToken);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync(cancellationToken);
        await foreach (var update in ((IChatClient)client).GetStreamingResponseAsync(messages, options, cancellationToken))
            yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
        // Clients are shared and owned by the singleton factory.
    }

    private async Task<OllamaApiClient> GetClientAsync(CancellationToken ct)
    {
        if (effectiveModel == null)
        {
            var principal = httpContextAccessor.HttpContext?.User;
            effectiveModel = principal?.Identity?.IsAuthenticated == true
                ? (await selection.GetSettingsAsync(principal, ct)).EffectiveModel
                : await selection.GetDefaultModelAsync(ct);
        }
        return factory.Get(effectiveModel);
    }
}

public sealed class OllamaChatClientFactory(IConfiguration config) : IDisposable
{
    private readonly ConcurrentDictionary<string, OllamaApiClient> clients =
        new(StringComparer.OrdinalIgnoreCase);

    public OllamaApiClient Get(string model) => clients.GetOrAdd(model, Create);

    public void Dispose()
    {
        foreach (var client in clients.Values) client.Dispose();
        clients.Clear();
    }

    private OllamaApiClient Create(string model)
    {
        var baseUrl = config["Ollama:BaseUrl"] ?? "http://localhost:11434";
        if (!baseUrl.EndsWith('/')) baseUrl += "/";
        var timeout = TimeSpan.FromSeconds(config.GetValue("Ollama:TimeoutSeconds", 300));
        return new(new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = timeout }, model);
    }
}
