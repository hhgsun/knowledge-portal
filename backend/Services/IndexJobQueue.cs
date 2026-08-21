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
                INSERT INTO index_jobs ("ArticleId", "Status", "Generation", "Priority", "AttemptCount",
                    "AvailableAt", "CreatedAt", "UpdatedAt")
                VALUES ({0}, 'pending', 1, {1}, 0, {2}, {2}, {2})
                ON CONFLICT ("ArticleId") DO UPDATE SET
                    "Status" = 'pending',
                    "Generation" = index_jobs."Generation" + 1,
                    "Priority" = GREATEST(index_jobs."Priority", EXCLUDED."Priority"),
                    "AttemptCount" = 0,
                    "AvailableAt" = EXCLUDED."AvailableAt",
                    "LockedAt" = NULL,
                    "LockedBy" = NULL,
                    "LastError" = NULL,
                    "CompletedAt" = NULL,
                    "UpdatedAt" = EXCLUDED."UpdatedAt"
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
                SELECT "ArticleId" FROM index_jobs
                WHERE (("Status" = 'pending' AND "AvailableAt" <= {0})
                    OR ("Status" = 'processing' AND "LockedAt" < {1}))
                ORDER BY "Priority" DESC, "AvailableAt", "CreatedAt"
                FOR UPDATE SKIP LOCKED
                LIMIT {2}
            )
            UPDATE index_jobs j SET
                "Status" = 'processing', "LockedAt" = {0}, "LockedBy" = {3}, "UpdatedAt" = {0}
            FROM picked WHERE j."ArticleId" = picked."ArticleId"
            RETURNING j."ArticleId", j."Generation", j."LockedBy"
            """, now, expired, Math.Max(1, count), workerId).ToListAsync(ct);
#pragma warning restore EF1002
    }

    public Task CompleteAsync(IndexJobClaim claim, CancellationToken ct) => db.Database.ExecuteSqlRawAsync(
        """
        UPDATE index_jobs SET "Status" = 'completed', "CompletedAt" = {0}, "LockedAt" = NULL,
            "LockedBy" = NULL, "LastError" = NULL, "UpdatedAt" = {0}
        WHERE "ArticleId" = {1} AND "Generation" = {2} AND "Status" = 'processing'
          AND "LockedBy" = {3}
        """, [DateTime.UtcNow, claim.ArticleId, claim.Generation, claim.LockedBy], ct);

    public Task<int> RenewLeaseAsync(IndexJobClaim claim, CancellationToken ct) => db.Database.ExecuteSqlRawAsync(
        """
        UPDATE index_jobs SET "LockedAt" = {0}, "UpdatedAt" = {0}
        WHERE "ArticleId" = {1} AND "Generation" = {2} AND "Status" = 'processing'
          AND "LockedBy" = {3}
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
            UPDATE index_jobs SET "Status" = {0}, "AttemptCount" = {1}, "AvailableAt" = {2},
                "LockedAt" = NULL, "LockedBy" = NULL, "LastError" = {3}, "UpdatedAt" = {4}
            WHERE "ArticleId" = {5} AND "Generation" = {6} AND "LockedBy" = {7}
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
            INSERT INTO index_jobs ("ArticleId", "Status", "Generation", "Priority", "AttemptCount",
                "AvailableAt", "CreatedAt", "UpdatedAt")
            SELECT a."Id", 'pending', 1, 10, 0, NOW(), NOW(), NOW()
            FROM articles a
            WHERE a."Status" = 'published'
              AND (a."FtsIndexedAt" IS NULL OR ({0} AND a."IndexedAt" IS NULL))
            ON CONFLICT ("ArticleId") DO UPDATE SET
                "Status" = 'pending',
                "Generation" = index_jobs."Generation" + 1,
                "Priority" = GREATEST(index_jobs."Priority", 10),
                "AttemptCount" = 0,
                "AvailableAt" = NOW(),
                "LockedAt" = NULL,
                "LockedBy" = NULL,
                "LastError" = NULL,
                "CompletedAt" = NULL,
                "UpdatedAt" = NOW()
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
                INSERT INTO index_jobs ("ArticleId", "Status", "Generation", "Priority", "AttemptCount",
                    "AvailableAt", "CreatedAt", "UpdatedAt")
                SELECT a."Id", 'pending', 1, 10, 0, NOW(), NOW(), NOW()
                FROM articles a
                WHERE a."Status" = 'published'
                  AND (a."FtsIndexedAt" IS NULL OR ({0} AND a."IndexedAt" IS NULL))
                ON CONFLICT ("ArticleId") DO UPDATE SET
                    "Status" = 'pending',
                    "Generation" = index_jobs."Generation" + 1,
                    "Priority" = GREATEST(index_jobs."Priority", 10),
                    "AttemptCount" = 0,
                    "AvailableAt" = NOW(),
                    "LockedAt" = NULL,
                    "LockedBy" = NULL,
                    "LastError" = NULL,
                    "CompletedAt" = NULL,
                    "UpdatedAt" = NOW()
                WHERE index_jobs."Status" = 'completed'
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
                INSERT INTO index_jobs ("ArticleId", "Status", "Generation", "Priority", "AttemptCount",
                    "AvailableAt", "CreatedAt", "UpdatedAt")
                SELECT a."Id", 'pending', 1, 100, 0, {1}, {1}, {1}
                FROM articles a
                WHERE a."Status" = 'published'
                  AND (a."FtsIndexedAt" IS NULL OR ({0} AND a."IndexedAt" IS NULL))
                ON CONFLICT ("ArticleId") DO UPDATE SET
                    "Status" = 'pending',
                    "Generation" = index_jobs."Generation" + 1,
                    "Priority" = GREATEST(index_jobs."Priority", 100),
                    "AttemptCount" = 0,
                    "AvailableAt" = {1},
                    "LockedAt" = NULL,
                    "LockedBy" = NULL,
                    "LastError" = NULL,
                    "CompletedAt" = NULL,
                    "UpdatedAt" = {1}
                WHERE index_jobs."Status" IN ('completed', 'failed')
                   OR (index_jobs."Status" = 'pending'
                       AND (index_jobs."AttemptCount" > 0 OR index_jobs."AvailableAt" > {1}))
                   OR (index_jobs."Status" = 'processing'
                       AND (index_jobs."LockedAt" IS NULL OR index_jobs."LockedAt" < {2}))
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
