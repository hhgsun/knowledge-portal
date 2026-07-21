using KnowledgePortal.Api.Services;
using Microsoft.Extensions.Configuration;

namespace KnowledgePortal.Api.Tests.Unit;

public class EmbeddingFailureTrackerTests
{
    private static EmbeddingFailureTracker CreateTracker(int backoffSeconds = 30, int maxBackoffSeconds = 3600)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ollama:BackoffSeconds"] = backoffSeconds.ToString(),
                ["Ollama:MaxFailureBackoffSeconds"] = maxBackoffSeconds.ToString()
            })
            .Build();
        return new EmbeddingFailureTracker(config);
    }

    private static readonly DateTime Updated = new(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ShouldAttempt_UnknownArticle_ReturnsTrue()
    {
        var tracker = CreateTracker();

        Assert.True(tracker.ShouldAttempt("a1", Updated, Now));
    }

    [Fact]
    public void RecordFailure_BlocksUntilBackoffElapses()
    {
        var tracker = CreateTracker(backoffSeconds: 30);

        var info = tracker.RecordFailure("a1", Updated, Now);

        Assert.Equal(1, info.Count);
        Assert.Equal(Now.AddSeconds(30), info.NextAttemptUtc);
        Assert.False(tracker.ShouldAttempt("a1", Updated, Now.AddSeconds(29)));
        Assert.True(tracker.ShouldAttempt("a1", Updated, Now.AddSeconds(30)));
    }

    [Fact]
    public void RecordFailure_BackoffGrowsExponentially()
    {
        var tracker = CreateTracker(backoffSeconds: 30);

        tracker.RecordFailure("a1", Updated, Now);
        var second = tracker.RecordFailure("a1", Updated, Now);
        var third = tracker.RecordFailure("a1", Updated, Now);

        Assert.Equal(2, second.Count);
        Assert.Equal(Now.AddSeconds(60), second.NextAttemptUtc);
        Assert.Equal(3, third.Count);
        Assert.Equal(Now.AddSeconds(120), third.NextAttemptUtc);
    }

    [Fact]
    public void RecordFailure_BackoffIsCapped()
    {
        var tracker = CreateTracker(backoffSeconds: 30, maxBackoffSeconds: 100);

        EmbeddingFailureTracker.FailureInfo info = null!;
        for (var i = 0; i < 10; i++)
            info = tracker.RecordFailure("a1", Updated, Now);

        Assert.Equal(Now.AddSeconds(100), info.NextAttemptUtc);
    }

    [Fact]
    public void RecordSuccess_ClearsFailureState()
    {
        var tracker = CreateTracker();
        tracker.RecordFailure("a1", Updated, Now);

        tracker.RecordSuccess("a1");

        Assert.True(tracker.ShouldAttempt("a1", Updated, Now));
        Assert.Empty(tracker.Snapshot());
    }

    [Fact]
    public void ShouldAttempt_ContentChanged_ResetsAndReturnsTrue()
    {
        var tracker = CreateTracker();
        tracker.RecordFailure("a1", Updated, Now);

        var newUpdatedAt = Updated.AddMinutes(5);

        Assert.True(tracker.ShouldAttempt("a1", newUpdatedAt, Now));
        Assert.Empty(tracker.Snapshot());
    }

    [Fact]
    public void RecordFailure_AfterContentChange_RestartsCount()
    {
        var tracker = CreateTracker(backoffSeconds: 30);
        tracker.RecordFailure("a1", Updated, Now);
        tracker.RecordFailure("a1", Updated, Now);

        var info = tracker.RecordFailure("a1", Updated.AddMinutes(5), Now);

        Assert.Equal(1, info.Count);
        Assert.Equal(Now.AddSeconds(30), info.NextAttemptUtc);
    }
}
