using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore;

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
        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), basePath, articleId));
    }

    public static string GetFilePath(IConfiguration config, string articleId, string storedFileName)
        => Path.Combine(GetArticleDirectory(config, articleId), storedFileName);

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
            .Select(a => new { a.StoredFileName, a.FileName })
            .ToListAsync(ct);

        if (attachments.Count == 0) return "";

        var sb = new System.Text.StringBuilder();
        foreach (var att in attachments)
        {
            var extension = Path.GetExtension(att.FileName).ToLowerInvariant();
            var filePath = GetFilePath(config, articleId, att.StoredFileName);
            var text = AttachmentTextExtractor.ExtractText(filePath, extension);
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.Append(text);
                sb.Append(' ');
            }
        }

        return sb.ToString();
    }

    /// <summary>Builds articleId → attachment summaries (with download URLs) for a set of articles.</summary>
    public static async Task<Dictionary<string, List<object>>> GetAttachmentMapAsync(AppDbContext db, IReadOnlyCollection<string> articleIds)
    {
        var attachments = await db.ArticleAttachments
            .Where(a => articleIds.Contains(a.ArticleId))
            .OrderBy(a => a.CreatedAt)
            .Select(a => new { a.Id, a.ArticleId, a.FileName, a.ContentType, a.SizeBytes })
            .ToListAsync();

        return attachments.GroupBy(a => a.ArticleId).ToDictionary(
            g => g.Key,
            g => g.Select(a => (object)new { a.Id, a.FileName, a.ContentType, a.SizeBytes, DownloadUrl = GetDownloadUrl(a.Id) }).ToList());
    }
}
