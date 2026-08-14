using KnowledgePortal.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgePortal.Api.Tests.Unit;

public class RagResilienceServiceTests
{
    [Fact]
    public async Task ExecuteAsync_RetriesTransientFailureOnce()
    {
        var service = Build(); var calls = 0;

        var result = await service.ExecuteAsync("generation", 5, 1, true, _ =>
        {
            calls++; if (calls == 1) throw new HttpRequestException("temporary");
            return Task.FromResult("ok");
        }, CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ExecuteAsync_ConvertsStageCancellationToTypedTimeout()
    {
        var service = Build();

        await Assert.ThrowsAsync<RagStageTimeoutException>(() => service.ExecuteAsync("map", 1, 0, true,
            async ct => { await Task.Delay(TimeSpan.FromSeconds(10), ct); return "never"; }, CancellationToken.None));
    }

    [Fact]
    public async Task CircuitBreaker_RejectsCallAfterConfiguredFailureThreshold()
    {
        var service = Build(("RagResilience:CircuitBreakerFailureThreshold", "1"), ("RagResilience:CircuitBreakerSeconds", "30"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync<string>("generation", 5, 0, true,
            _ => throw new InvalidOperationException("fatal"), CancellationToken.None));

        await Assert.ThrowsAsync<RagCircuitOpenException>(() => service.ExecuteAsync("generation", 5, 0, true,
            _ => Task.FromResult("blocked"), CancellationToken.None));
    }

    [Fact]
    public async Task Bulkhead_RejectsWhenQueueWaitExpires()
    {
        var service = Build(("RagResilience:ConcurrencyLimit", "1"), ("RagResilience:QueueTimeoutSeconds", "1"));
        await using var lease = await service.EnterAsync(CancellationToken.None);

        await Assert.ThrowsAsync<RagBusyException>(async () => await service.EnterAsync(CancellationToken.None));
    }

    private static RagResilienceService Build(params (string Key, string Value)[] values) => new(
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value))).Build(),
        NullLogger<RagResilienceService>.Instance);
}
