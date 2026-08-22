using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;
using KnowledgePortal.Api.Models.Entities;

namespace KnowledgePortal.Api.Helpers;

/// <summary>
/// Shared attachment concerns: disk paths under FileStorage:BasePath,
/// download URLs, and the per-article attachment map used in list/search responses.
/// </summary>
public static class AttachmentHelper
{
    public static string GetArticleDirectory(IConfiguration config, string articleId)
    {
        var basePath = config["FileStorage:BasePath"] ?? "../data/uploads";
        var root = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), basePath));
        var result = Path.GetFullPath(Path.Combine(root, Path.GetFileName(articleId)));
        if (!result.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Attachment path escaped the configured storage root");
        return result;
    }

    public static string GetFilePath(IConfiguration config, string articleId, string storedFileName)
        => Path.Combine(GetArticleDirectory(config, articleId), Path.GetFileName(storedFileName));

    /// <summary>Writes on the destination volume, fsyncs, hashes, then atomically renames.</summary>
    public static async Task<string> SaveAtomicAsync(IConfiguration config, string articleId,
        string storedFileName, Stream source, CancellationToken ct = default)
    {
        var dir = GetArticleDirectory(config, articleId);
        Directory.CreateDirectory(dir);
        var destination = GetFilePath(config, articleId, storedFileName);
        var temporary = Path.Combine(dir, $".{Path.GetFileName(storedFileName)}.{Guid.NewGuid():N}.upload");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var buffer = new byte[128 * 1024];
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    hash.AppendData(buffer, 0, read);
                }
                await output.FlushAsync(ct);
            }
            File.Move(temporary, destination, overwrite: false);
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    public static void MoveToTrash(IConfiguration config, string articleId, string storedFileName)
    {
        var source = GetFilePath(config, articleId, storedFileName);
        if (!File.Exists(source)) return;
        var root = Path.GetDirectoryName(GetArticleDirectory(config, articleId))!;
        var trash = Path.Combine(root, ".trash", Path.GetFileName(articleId));
        Directory.CreateDirectory(trash);
        File.Move(source, Path.Combine(trash,
            $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}-{Path.GetFileName(storedFileName)}"));
    }

    public static void MoveArticleToTrash(IConfiguration config, string articleId)
    {
        var source = GetArticleDirectory(config, articleId);
        if (!Directory.Exists(source)) return;
        var root = Path.GetDirectoryName(source)!;
        var trash = Path.Combine(root, ".trash");
        Directory.CreateDirectory(trash);
        Directory.Move(source, Path.Combine(trash,
            $"{Path.GetFileName(articleId)}-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"));
    }

    public static string GetDownloadUrl(string attachmentId)
        => $"/api/attachments/{attachmentId}/download";

    /// <summary>
    /// Extracts and concatenates searchable text from all attachments of an article.
    /// Used by both the embedding pipeline and the FTS index builder.
    /// </summary>
    public static async Task<string> GetAttachmentTextAsync(AppDbContext db, IConfiguration config, string articleId, CancellationToken ct = default)
    {
        var attachments = await db.ArticleAttachments
            .Where(a => a.ArticleId == articleId)
            .ToListAsync(ct);

        if (attachments.Count == 0) return "";

        var sb = new System.Text.StringBuilder();
        foreach (var att in attachments)
        {
            var extraction = GetOrExtract(config, att);
            if (!string.IsNullOrWhiteSpace(extraction.Text))
            {
                sb.Append(extraction.Text);
                sb.Append(' ');
            }
        }

        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);

        return sb.ToString();
    }

    public static AttachmentExtractionResult GetOrExtract(IConfiguration config, ArticleAttachment attachment)
    {
        var extractionLimit = Math.Clamp(config.GetValue("FileStorage:MaxExtractedCharacters",
            AttachmentTextExtractor.DefaultMaxCharacters), 1_000, 5_000_000);
        if (attachment.ExtractedAt != null && (attachment.ExtractionStatus is "completed" or "no_text")
            && attachment.ExtractionCharacterLimit == extractionLimit)
        {
            try
            {
                var segments = string.IsNullOrWhiteSpace(attachment.ExtractedSegmentsJson)
                    ? []
                    : JsonSerializer.Deserialize<List<AttachmentTextSegment>>(attachment.ExtractedSegmentsJson) ?? [];
                return new(attachment.ExtractionStatus, attachment.ExtractedText ?? "", segments,
                    attachment.ExtractionError, attachment.ExtractionTruncated,
                    attachment.ExtractedCharacters, attachment.ExtractionCharacterLimit);
            }
            catch (JsonException)
            {
                // Legacy/corrupt extraction metadata is regenerated from the immutable file.
            }
        }

        var path = GetFilePath(config, attachment.ArticleId, attachment.StoredFileName);
        var result = AttachmentTextExtractor.Extract(path, Path.GetExtension(attachment.FileName), extractionLimit);
        attachment.ExtractionStatus = result.Status;
        attachment.ExtractionError = result.Error;
        attachment.ExtractedText = result.Text;
        attachment.ExtractedSegmentsJson = JsonSerializer.Serialize(result.Segments);
        attachment.ExtractionTruncated = result.Truncated;
        attachment.ExtractedCharacters = result.ExtractedCharacters;
        attachment.ExtractionCharacterLimit = result.CharacterLimit;
        attachment.ExtractedAt = DateTime.UtcNow;
        return result;
    }

    /// <summary>Builds articleId → attachment summaries (with download URLs) for a set of articles.</summary>
    public static async Task<Dictionary<string, List<object>>> GetAttachmentMapAsync(AppDbContext db, IReadOnlyCollection<string> articleIds)
    {
        var attachments = await db.ArticleAttachments
            .Where(a => articleIds.Contains(a.ArticleId))
            .OrderBy(a => a.CreatedAt)
            .Select(a => new { a.Id, a.ArticleId, a.FileName, a.ContentType, a.SizeBytes, a.CreatedAt,
                a.ExtractionStatus, a.ExtractionTruncated, a.ExtractedCharacters, a.ExtractionCharacterLimit })
            .ToListAsync();

        return attachments.GroupBy(a => a.ArticleId).ToDictionary(
            g => g.Key,
            g => g.Select(a => (object)new
            {
                a.Id,
                a.FileName,
                a.ContentType,
                a.SizeBytes,
                DownloadUrl = GetDownloadUrl(a.Id),
                a.ExtractionStatus,
                a.ExtractionTruncated,
                a.ExtractedCharacters,
                a.ExtractionCharacterLimit,
                CreatedAt = a.CreatedAt.ToString("o")
            }).ToList());
    }
}
