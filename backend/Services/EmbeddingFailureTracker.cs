using System.Collections.Concurrent;

namespace KnowledgePortal.Api.Services;

/// <summary>
/// In-memory failure tracking for the embedding pipeline. A "poison" article whose
/// embedding keeps failing is retried with exponential backoff instead of every poll,
/// so one bad article can't spam the logs or hammer Ollama forever. State resets when
/// the article's content changes (UpdatedAt moves) or after a process restart —
/// both are moments where a fresh attempt is genuinely warranted.
/// </summary>
public sealed class EmbeddingFailureTracker(IConfiguration config)
{
    public sealed record FailureInfo(int Count, DateTime NextAttemptUtc, DateTime ArticleUpdatedAt);

    private readonly ConcurrentDictionary<string, FailureInfo> _failures = new();
    private readonly int _baseBackoffSeconds = Math.Max(1, config.GetValue("Ollama:BackoffSeconds", 30));
    private readonly int _maxBackoffSeconds = Math.Max(1, config.GetValue("Ollama:MaxFailureBackoffSeconds", 3600));

    /// <summary>True when the article may be attempted now (no failures recorded, backoff elapsed, or content changed since the last failure).</summary>
    public bool ShouldAttempt(string articleId, DateTime updatedAt, DateTime nowUtc)
    {
        if (!_failures.TryGetValue(articleId, out var info)) return true;
        if (info.ArticleUpdatedAt != updatedAt)
        {
            // Content changed since the failures — start fresh
            _failures.TryRemove(articleId, out _);
            return true;
        }
        return nowUtc >= info.NextAttemptUtc;
    }

    public FailureInfo RecordFailure(string articleId, DateTime updatedAt, DateTime nowUtc)
    {
        return _failures.AddOrUpdate(articleId,
            _ => new FailureInfo(1, nowUtc.AddSeconds(_baseBackoffSeconds), updatedAt),
            (_, prev) =>
            {
                var count = prev.ArticleUpdatedAt == updatedAt ? prev.Count + 1 : 1;
                var delay = Math.Min(_baseBackoffSeconds * Math.Pow(2, count - 1), _maxBackoffSeconds);
                return new FailureInfo(count, nowUtc.AddSeconds(delay), updatedAt);
            });
    }

    public void RecordSuccess(string articleId) => _failures.TryRemove(articleId, out _);

    public IReadOnlyDictionary<string, FailureInfo> Snapshot() => _failures;
}
