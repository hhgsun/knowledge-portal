using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public sealed record IndexJobClaim(string ArticleId, int Generation, string LockedBy = "");

/// <summary>PostgreSQL-backed durable queue; no external broker is required.</summary>
public class IndexJobQueue(AppDbContext db, IConfiguration config)
{
    private readonly int _maxAttempts = Math.Max(1, config.GetValue("Indexing:MaxAttempts", 10));
    private readonly int _baseBackoffSeconds = Math.Max(1, config.GetValue("Indexing:BackoffSeconds", 30));
    private readonly int _maxBackoffSeconds = Math.Max(1, config.GetValue("Indexing:MaxBackoffSeconds", 3600));
    private readonly int _leaseMinutes = Math.Max(1, config.GetValue("Indexing:LeaseMinutes", 15));

    public async Task EnqueueAsync(string articleId, int priority = 100, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        if (db.Database.IsRelational())
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO index_jobs (article_id, status, generation, priority, attempt_count,
                    available_at, created_at, updated_at)
                VALUES ({0}, 'pending', 1, {1}, 0, {2}, {2}, {2})
                ON CONFLICT (article_id) DO UPDATE SET
                    status = 'pending',
                    generation = index_jobs.generation + 1,
                    priority = GREATEST(index_jobs.priority, EXCLUDED.priority),
                    attempt_count = 0,
                    available_at = EXCLUDED.available_at,
                    locked_at = NULL,
                    locked_by = NULL,
                    last_error = NULL,
                    completed_at = NULL,
                    updated_at = EXCLUDED.updated_at
                """, [articleId, priority, now], ct);
            return;
        }

        var job = await db.IndexJobs.FindAsync([articleId], ct);
        if (job == null)
            db.IndexJobs.Add(new IndexJob { ArticleId = articleId, Priority = priority, AvailableAt = now });
        else
        {
            job.Status = "pending";
            job.Generation++;
            job.Priority = Math.Max(job.Priority, priority);
            job.AttemptCount = 0;
            job.AvailableAt = now;
            job.LockedAt = null;
            job.LockedBy = null;
            job.LastError = null;
            job.CompletedAt = null;
            job.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<IndexJobClaim>> ClaimAsync(string workerId, int count, TimeSpan lease, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return [];
        var now = DateTime.UtcNow;
        var expired = now.Subtract(lease);
#pragma warning disable EF1002
        return await db.Database.SqlQueryRaw<IndexJobClaim>(
            """
            WITH picked AS (
                SELECT article_id FROM index_jobs
                WHERE ((status = 'pending' AND available_at <= {0})
                    OR (status = 'processing' AND locked_at < {1}))
                ORDER BY priority DESC, available_at, created_at
                FOR UPDATE SKIP LOCKED
                LIMIT {2}
            )
            UPDATE index_jobs j SET
                status = 'processing', locked_at = {0}, locked_by = {3}, updated_at = {0}
            FROM picked WHERE j.article_id = picked.article_id
            RETURNING j.article_id AS "ArticleId", j.generation AS "Generation", j.locked_by AS "LockedBy"
            """, now, expired, Math.Max(1, count), workerId).ToListAsync(ct);
#pragma warning restore EF1002
    }

    public Task CompleteAsync(IndexJobClaim claim, CancellationToken ct) => db.Database.ExecuteSqlRawAsync(
        """
        UPDATE index_jobs SET status = 'completed', completed_at = {0}, locked_at = NULL,
            locked_by = NULL, last_error = NULL, updated_at = {0}
        WHERE article_id = {1} AND generation = {2} AND status = 'processing'
          AND locked_by = {3}
        """, [DateTime.UtcNow, claim.ArticleId, claim.Generation, claim.LockedBy], ct);

    public Task<int> RenewLeaseAsync(IndexJobClaim claim, CancellationToken ct) => db.Database.ExecuteSqlRawAsync(
        """
        UPDATE index_jobs SET locked_at = {0}, updated_at = {0}
        WHERE article_id = {1} AND generation = {2} AND status = 'processing'
          AND locked_by = {3}
        """, [DateTime.UtcNow, claim.ArticleId, claim.Generation, claim.LockedBy], ct);

    public async Task FailAsync(IndexJobClaim claim, Exception error, CancellationToken ct)
    {
        var currentAttempt = await db.IndexJobs.AsNoTracking()
            .Where(j => j.ArticleId == claim.ArticleId && j.Generation == claim.Generation)
            .Select(j => (int?)j.AttemptCount).SingleOrDefaultAsync(ct);
        if (currentAttempt == null) return; // a newer generation superseded this worker

        var attempt = currentAttempt.Value + 1;
        var terminal = attempt >= _maxAttempts;
        var delay = Math.Min(_maxBackoffSeconds, _baseBackoffSeconds * Math.Pow(2, Math.Min(attempt - 1, 20)));
        var message = error.ToString();
        if (message.Length > 4000) message = message[..4000];
        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE index_jobs SET status = {0}, attempt_count = {1}, available_at = {2},
                locked_at = NULL, locked_by = NULL, last_error = {3}, updated_at = {4}
            WHERE article_id = {5} AND generation = {6} AND locked_by = {7}
            """,
            [terminal ? "failed" : "pending", attempt, DateTime.UtcNow.AddSeconds(delay), message,
             DateTime.UtcNow, claim.ArticleId, claim.Generation, claim.LockedBy], ct);
    }

    public async Task<int> BackfillDirtyArticlesAsync(CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return 0;
        var semanticEnabled = config.GetValue("Ollama:Enabled", false);
        return await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO index_jobs (article_id, status, generation, priority, attempt_count,
                available_at, created_at, updated_at)
            SELECT a.id, 'pending', 1, 10, 0, NOW(), NOW(), NOW()
            FROM articles a
            WHERE a.status = 'published'
              AND (a.fts_indexed_at IS NULL OR ({0} AND a.indexed_at IS NULL))
            ON CONFLICT (article_id) DO UPDATE SET
                status = 'pending', generation = index_jobs.generation + 1,
                priority = GREATEST(index_jobs.priority, 10), attempt_count = 0,
                available_at = NOW(), locked_at = NULL, locked_by = NULL,
                last_error = NULL, completed_at = NULL, updated_at = NOW()
            """, [semanticEnabled], ct);
    }

    /// <summary>
    /// Repairs gaps between article index markers and the durable queue without disturbing work
    /// that is already pending/processing or deliberately parked as a terminal failure. Unlike
    /// the startup/manual backfill, this is safe to run periodically while workers are active.
    /// Completed jobs are re-opened only when the article is still dirty; missing jobs are added.
    /// </summary>
    public async Task<int> ReconcileDirtyArticlesAsync(CancellationToken ct)
    {
        var semanticEnabled = config.GetValue("Ollama:Enabled", false);
        if (db.Database.IsRelational())
        {
            return await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO index_jobs (article_id, status, generation, priority, attempt_count,
                    available_at, created_at, updated_at)
                SELECT a.id, 'pending', 1, 10, 0, NOW(), NOW(), NOW()
                FROM articles a
                WHERE a.status = 'published'
                  AND (a.fts_indexed_at IS NULL OR ({0} AND a.indexed_at IS NULL))
                ON CONFLICT (article_id) DO UPDATE SET
                    status = 'pending', generation = index_jobs.generation + 1,
                    priority = GREATEST(index_jobs.priority, 10), attempt_count = 0,
                    available_at = NOW(), locked_at = NULL, locked_by = NULL,
                    last_error = NULL, completed_at = NULL, updated_at = NOW()
                WHERE index_jobs.status = 'completed'
                """, [semanticEnabled], ct);
        }

        // The production path is PostgreSQL, but mirroring the state transition for the
        // InMemory provider keeps the reconciliation policy covered by the Docker-free suite.
        var dirtyArticles = await db.Articles
            .Where(a => a.Status == "published" &&
                (a.FtsIndexedAt == null || (semanticEnabled && a.IndexedAt == null)))
            .Select(a => a.Id)
            .ToListAsync(ct);
        if (dirtyArticles.Count == 0) return 0;

        var jobs = await db.IndexJobs
            .Where(j => dirtyArticles.Contains(j.ArticleId))
            .ToDictionaryAsync(j => j.ArticleId, ct);
        var now = DateTime.UtcNow;
        var reconciled = 0;
        foreach (var articleId in dirtyArticles)
        {
            if (!jobs.TryGetValue(articleId, out var job))
            {
                db.IndexJobs.Add(new IndexJob
                {
                    ArticleId = articleId,
                    Priority = 10,
                    AvailableAt = now,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                reconciled++;
                continue;
            }

            if (job.Status != "completed") continue;
            job.Status = "pending";
            job.Generation++;
            job.Priority = Math.Max(job.Priority, 10);
            job.AttemptCount = 0;
            job.AvailableAt = now;
            job.LockedAt = null;
            job.LockedBy = null;
            job.LastError = null;
            job.CompletedAt = null;
            job.UpdatedAt = now;
            reconciled++;
        }

        if (reconciled > 0) await db.SaveChangesAsync(ct);
        return reconciled;
    }

    /// <summary>
    /// Repairs only published articles whose lexical or semantic index marker is missing. Missing,
    /// completed, failed and delayed-retry jobs are made immediately available; a processing job is
    /// reclaimed only after its lease expires. Healthy articles and actively leased jobs are never
    /// disturbed, so this operation is safe to expose as the routine admin recovery action instead
    /// of a corpus-wide reindex.
    /// </summary>
    public async Task<int> RepairDirtyArticlesAsync(CancellationToken ct)
    {
        var semanticEnabled = config.GetValue("Ollama:Enabled", false);
        var now = DateTime.UtcNow;
        var expired = now.Subtract(TimeSpan.FromMinutes(_leaseMinutes));

        if (db.Database.IsRelational())
        {
            return await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO index_jobs (article_id, status, generation, priority, attempt_count,
                    available_at, created_at, updated_at)
                SELECT a.id, 'pending', 1, 100, 0, {1}, {1}, {1}
                FROM articles a
                WHERE a.status = 'published'
                  AND (a.fts_indexed_at IS NULL OR ({0} AND a.indexed_at IS NULL))
                ON CONFLICT (article_id) DO UPDATE SET
                    status = 'pending', generation = index_jobs.generation + 1,
                    priority = GREATEST(index_jobs.priority, 100), attempt_count = 0,
                    available_at = {1}, locked_at = NULL, locked_by = NULL,
                    last_error = NULL, completed_at = NULL, updated_at = {1}
                WHERE index_jobs.status IN ('completed', 'failed')
                   OR (index_jobs.status = 'pending'
                       AND (index_jobs.attempt_count > 0 OR index_jobs.available_at > {1}))
                   OR (index_jobs.status = 'processing'
                       AND (index_jobs.locked_at IS NULL OR index_jobs.locked_at < {2}))
                """, [semanticEnabled, now, expired], ct);
        }

        var dirtyArticles = await db.Articles
            .Where(a => a.Status == "published" &&
                (a.FtsIndexedAt == null || (semanticEnabled && a.IndexedAt == null)))
            .Select(a => a.Id)
            .ToListAsync(ct);
        if (dirtyArticles.Count == 0) return 0;

        var jobs = await db.IndexJobs
            .Where(j => dirtyArticles.Contains(j.ArticleId))
            .ToDictionaryAsync(j => j.ArticleId, ct);
        var repaired = 0;
        foreach (var articleId in dirtyArticles)
        {
            if (!jobs.TryGetValue(articleId, out var job))
            {
                db.IndexJobs.Add(new IndexJob
                {
                    ArticleId = articleId,
                    Priority = 100,
                    AvailableAt = now,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                repaired++;
                continue;
            }

            var repairable = job.Status is "completed" or "failed"
                || (job.Status == "pending" && (job.AttemptCount > 0 || job.AvailableAt > now))
                || (job.Status == "processing" && (job.LockedAt == null || job.LockedAt < expired));
            if (!repairable) continue;

            job.Status = "pending";
            job.Generation++;
            job.Priority = Math.Max(job.Priority, 100);
            job.AttemptCount = 0;
            job.AvailableAt = now;
            job.LockedAt = null;
            job.LockedBy = null;
            job.LastError = null;
            job.CompletedAt = null;
            job.UpdatedAt = now;
            repaired++;
        }

        if (repaired > 0) await db.SaveChangesAsync(ct);
        return repaired;
    }
}
