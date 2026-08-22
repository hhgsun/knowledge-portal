using System.Security.Cryptography;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public record AttachmentStorageHealthDto(long Files, long Bytes, long SampleMissingFiles,
    long PendingExtraction, long FailedExtraction, long TruncatedExtraction,
    int ChecksumsVerified, int ChecksumMismatches, long FreeBytes, string RootPath);

/// <summary>Bounded, read-only integrity probe for the local data/uploads store.</summary>
public class AttachmentStorageService(AppDbContext db, IConfiguration config)
{
    public async Task<AttachmentStorageHealthDto> CollectHealthAsync(CancellationToken ct = default)
    {
        var root = Path.GetDirectoryName(AttachmentHelper.GetArticleDirectory(config, "probe"))!;
        Directory.CreateDirectory(root);
        var stats = await db.ArticleAttachments.AsNoTracking().GroupBy(_ => 1).Select(g => new
        {
            Files = g.LongCount(),
            Bytes = g.Sum(x => x.SizeBytes),
            Pending = g.LongCount(x => x.ExtractionStatus == "pending"),
            Failed = g.LongCount(x => x.ExtractionStatus == "failed"),
            Truncated = g.LongCount(x => x.ExtractionTruncated)
        }).SingleOrDefaultAsync(ct);

        var sampleSize = Math.Clamp(config.GetValue("FileStorage:IntegritySampleSize", 100), 0, 1000);
        var sample = await db.ArticleAttachments.AsNoTracking().OrderByDescending(a => a.CreatedAt)
            .Take(sampleSize).Select(a => new { a.ArticleId, a.StoredFileName, a.Sha256 }).ToListAsync(ct);
        long missing = 0;
        var verified = 0;
        var mismatches = 0;
        foreach (var item in sample)
        {
            var path = AttachmentHelper.GetFilePath(config, item.ArticleId, item.StoredFileName);
            if (!File.Exists(path)) { missing++; continue; }
            if (string.IsNullOrWhiteSpace(item.Sha256)) continue; // legacy row; filled on next upload/re-import
            await using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
            verified++;
            if (!actual.Equals(item.Sha256, StringComparison.OrdinalIgnoreCase)) mismatches++;
        }

        var drive = new DriveInfo(Path.GetPathRoot(root)!);
        return new AttachmentStorageHealthDto(stats?.Files ?? 0, stats?.Bytes ?? 0, missing,
            stats?.Pending ?? 0, stats?.Failed ?? 0, stats?.Truncated ?? 0,
            verified, mismatches, drive.AvailableFreeSpace, root);
    }
}
