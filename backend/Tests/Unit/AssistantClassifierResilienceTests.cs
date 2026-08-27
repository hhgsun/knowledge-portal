using KnowledgePortal.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgePortal.Api.Tests.Unit;

public sealed class AssistantClassifierResilienceTests
{
    [Fact]
    public async Task CircuitBreaker_RejectsAfterConfiguredFailureThreshold()
    {
        var service = Build(("AgenticRouting:ClassifierCircuitFailureThreshold", "1"));
        await Assert.ThrowsAsync<HttpRequestException>(() => service.ExecuteAsync<string>(
            _ => throw new HttpRequestException("model unavailable"), CancellationToken.None));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(
            _ => Task.FromResult("blocked"), CancellationToken.None));
        Assert.Contains("circuit", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bulkhead_RejectsWhenQueueBudgetExpires()
    {
        var service = Build(
            ("AgenticRouting:ClassifierConcurrencyLimit", "1"),
            ("AgenticRouting:ClassifierQueueTimeoutSeconds", "1"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = service.ExecuteAsync(async _ =>
        {
            entered.SetResult();
            await release.Task;
            return "ok";
        }, CancellationToken.None);
        await entered.Task;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(
            _ => Task.FromResult("blocked"), CancellationToken.None));
        release.SetResult();
        await first;

        Assert.Contains("capacity", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cache_ExpiresAndNeverRequiresRawQueryStorage()
    {
        var service = Build(("AgenticRouting:ClassifierCacheSeconds", "0"));
        service.Set("sha256-only-key", new(AssistantRoute.KnowledgeSearch, .9,
            "document_lookup", false));

        Assert.False(service.TryGet("sha256-only-key", out _));
    }

    [Fact]
    public async Task CallerCancellation_DoesNotOpenClassifierCircuit()
    {
        var service = Build(("AgenticRouting:ClassifierCircuitFailureThreshold", "1"));
        using var cancelled = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = service.ExecuteAsync(async token =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return "never";
        }, cancelled.Token);
        await entered.Task;
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        var result = await service.ExecuteAsync(_ => Task.FromResult("healthy"), CancellationToken.None);

        Assert.Equal("healthy", result);
    }

    private static AssistantClassifierResilienceService Build(
        params (string Key, string Value)[] values)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(values.Select(value =>
            new KeyValuePair<string, string?>(value.Key, value.Value))).Build();
        var provider = new ServiceCollection().BuildServiceProvider();
        var metrics = new PortalMetrics(provider.GetRequiredService<IServiceScopeFactory>(), config);
        return new(config, metrics, NullLogger<AssistantClassifierResilienceService>.Instance);
    }
}
