using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public sealed record IndexJobClaim(string ArticleId, int Generation);

/// <summary>PostgreSQL-backed durable queue; no external broker is required.</summary>
public class IndexJobQueue(AppDbContext db, IConfiguration config)
{
    private readonly int _maxAttempts = Math.Max(1, config.GetValue("Indexing:MaxAttempts", 10));
    private readonly int _baseBackoffSeconds = Math.Max(1, config.GetValue("Indexing:BackoffSeconds", 30));
    private readonly int _maxBackoffSeconds = Math.Max(1, config.GetValue("Indexing:MaxBackoffSeconds", 3600));

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
            RETURNING j."ArticleId", j."Generation"
            """, now, expired, Math.Max(1, count), workerId).ToListAsync(ct);
#pragma warning restore EF1002
    }

    public Task CompleteAsync(IndexJobClaim claim, CancellationToken ct) => db.Database.ExecuteSqlRawAsync(
        """
        UPDATE index_jobs SET "Status" = 'completed', "CompletedAt" = {0}, "LockedAt" = NULL,
            "LockedBy" = NULL, "LastError" = NULL, "UpdatedAt" = {0}
        WHERE "ArticleId" = {1} AND "Generation" = {2} AND "Status" = 'processing'
        """, [DateTime.UtcNow, claim.ArticleId, claim.Generation], ct);

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
            WHERE "ArticleId" = {5} AND "Generation" = {6}
            """,
            [terminal ? "failed" : "pending", attempt, DateTime.UtcNow.AddSeconds(delay), message,
             DateTime.UtcNow, claim.ArticleId, claim.Generation], ct);
    }

    public async Task<int> BackfillDirtyArticlesAsync(CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return 0;
        return await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO index_jobs ("ArticleId", "Status", "Generation", "Priority", "AttemptCount",
                "AvailableAt", "CreatedAt", "UpdatedAt")
            SELECT a."Id", 'pending', 1, 10, 0, NOW(), NOW(), NOW()
            FROM articles a
            WHERE a."Status" = 'published' AND a."IndexedAt" IS NULL
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
            """, ct);
    }
}
