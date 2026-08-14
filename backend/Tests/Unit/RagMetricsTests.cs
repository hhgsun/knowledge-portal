using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using KnowledgePortal.Api.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgePortal.Api.Tests.Unit;

public class RagMetricsTests
{
    [Fact]
    public void PortalMetrics_EmitsRagMeasurementsWithLowCardinalityTags()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        var observed = new ConcurrentBag<(string Name, IReadOnlyList<KeyValuePair<string, object?>> Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        { if (instrument.Meter.Name == PortalMetrics.MeterName && instrument.Name.StartsWith("kp_rag_")) l.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((i, _, tags, _) => observed.Add((i.Name, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((i, _, tags, _) => observed.Add((i.Name, tags.ToArray())));
        listener.Start();
        var metrics = new PortalMetrics(provider.GetRequiredService<IServiceScopeFactory>());

        metrics.RagRequests.Add(1, new KeyValuePair<string, object?>[] { new("mode", "narrow"), new("outcome", "success") });
        metrics.RagDuration.Record(12.5, new KeyValuePair<string, object?>[] { new("mode", "narrow"), new("outcome", "success") });
        metrics.RagCitationCoverage.Record(1, new KeyValuePair<string, object?>[] { new("mode", "narrow") });

        Assert.Contains(observed, x => x.Name == "kp_rag_requests" && x.Tags.Any(t => t.Key == "mode"));
        Assert.Contains(observed, x => x.Name == "kp_rag_duration_ms");
        Assert.Contains(observed, x => x.Name == "kp_rag_citation_coverage");
        Assert.DoesNotContain(observed.SelectMany(x => x.Tags), tag => tag.Key.Contains("query", StringComparison.OrdinalIgnoreCase));
    }
}
