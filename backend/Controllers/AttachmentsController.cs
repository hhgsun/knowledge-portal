using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Authorize]
public class AttachmentsController(AppDbContext db, IConfiguration config, ArticleService articleService,
    ILogger<AttachmentsController> logger) : ControllerBase
{
    private static readonly Dictionary<string, string[]> MimeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = ["image/png"],
        [".jpg"] = ["image/jpeg"],
        [".jpeg"] = ["image/jpeg"],
        [".gif"] = ["image/gif"],
        [".webp"] = ["image/webp"],
        [".svg"] = ["image/svg+xml"],
        [".pdf"] = ["application/pdf"],
        [".md"] = ["text/markdown", "text/plain", "application/octet-stream"],
        [".txt"] = ["text/plain"],
        [".docx"] = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/octet-stream"],
        [".xlsx"] = ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/octet-stream"],
        [".pptx"] = ["application/vnd.openxmlformats-officedocument.presentationml.presentation", "application/octet-stream"],
        [".xls"] = ["application/vnd.ms-excel", "application/octet-stream"],
        [".yaml"] = ["text/yaml", "application/x-yaml", "text/plain", "application/octet-stream"],
        [".json"] = ["application/json", "text/plain"],
        [".csv"] = ["text/csv", "text/plain", "application/octet-stream"],
    };

    [HttpGet("api/articles/{articleId}/attachments")]
    public async Task<IActionResult> List(string articleId)
    {
        var article = await db.Articles.FindAsync(articleId);
        if (article == null) return NotFound(new { error = "Article not found" });

        var attachments = await db.ArticleAttachments
            .Where(a => a.ArticleId == articleId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new AttachmentResponse(
                a.Id,
                a.FileName,
                a.ContentType,
                a.SizeBytes,
                AttachmentHelper.GetDownloadUrl(a.Id),
                a.CreatedAt.ToString("o")))
            .ToArrayAsync();

        return Ok(new AttachmentListResponse(attachments, attachments.Length));
    }

    [HttpPost("api/articles/{articleId}/attachments")]
    [RequestSizeLimit(20_971_520)]
    public async Task<IActionResult> Upload(string articleId, IFormFile file)
    {
        var article = await db.Articles.FindAsync(articleId);
        if (article == null) return NotFound(new { error = "Article not found" });

        // Permission check: edit_own or edit_any
        var userId = User.GetUserId();
        if (!RbacService.CanEditArticle(User, article.OwnerId == userId))
            return StatusCode(403, new { error = "You do not have permission to upload attachments to this article" });

        // Validate file
        if (file.Length == 0)
            return BadRequest(new { error = "File is empty" });

        var maxSizeMB = config.GetValue("FileStorage:MaxFileSizeMB", 20);
        if (file.Length > maxSizeMB * 1024L * 1024L)
            return BadRequest(new { error = $"File size exceeds {maxSizeMB}MB limit" });

        // Extension whitelist check
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var allowedExtensions = config.GetSection("FileStorage:AllowedExtensions").Get<string[]>()
            ?? [".png", ".jpg", ".jpeg", ".gif", ".webp", ".pdf", ".md", ".txt", ".docx", ".xlsx", ".pptx", ".xls", ".yaml", ".json", ".csv", ".svg"];

        if (string.IsNullOrEmpty(extension) || !allowedExtensions.Contains(extension))
            return BadRequest(new { error = $"File type '{extension}' is not allowed" });

        // MIME type validation
        if (MimeMap.TryGetValue(extension, out var allowedMimes))
        {
            if (!allowedMimes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
                return BadRequest(new { error = $"Content type '{file.ContentType}' does not match file extension '{extension}'" });
        }

        // Max attachments per article check
        var maxAttachments = config.GetValue("FileStorage:MaxAttachmentsPerArticle", 20);
        var currentCount = await db.ArticleAttachments.CountAsync(a => a.ArticleId == articleId);
        if (currentCount >= maxAttachments)
            return BadRequest(new { error = $"Maximum {maxAttachments} attachments per article reached" });

        // Generate safe stored filename
        var storedFileName = $"{Guid.NewGuid().ToString("N")[..21]}{extension}";
        var articleDir = AttachmentHelper.GetArticleDirectory(config, articleId);
        Directory.CreateDirectory(articleDir);

        var filePath = Path.Combine(articleDir, storedFileName);

        // Same-volume temporary write + atomic rename; checksum is persisted with metadata.
        string sha256;
        await using (var input = file.OpenReadStream())
            sha256 = await AttachmentHelper.SaveAtomicAsync(config, articleId, storedFileName, input, HttpContext.RequestAborted);

        // Save metadata to DB
        var attachment = new ArticleAttachment
        {
            ArticleId = articleId,
            FileName = Path.GetFileName(file.FileName),
            StoredFileName = storedFileName,
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            Sha256 = sha256,
            UploadedById = userId
        };

        db.ArticleAttachments.Add(attachment);
        article.ApprovedById = null;
        article.ApprovedAt = null;
        try { await db.SaveChangesAsync(); }
        catch
        {
            AttachmentHelper.MoveToTrash(config, articleId, storedFileName);
            throw;
        }

        // Attachment text is part of the search index
        await articleService.QueueReindexAsync(article);

        return StatusCode(201, new AttachmentResponse(
            attachment.Id,
            attachment.FileName,
            attachment.ContentType,
            attachment.SizeBytes,
            AttachmentHelper.GetDownloadUrl(attachment.Id),
            attachment.CreatedAt.ToString("o")));
    }

    [HttpDelete("api/articles/{articleId}/attachments/{attachmentId}")]
    [RequireSessionAuth] // destructive deletes are session-only — API keys cannot delete
    public async Task<IActionResult> Delete(string articleId, string attachmentId)
    {
        var article = await db.Articles.FindAsync(articleId);
        if (article == null) return NotFound(new { error = "Article not found" });

        // Permission check
        if (!RbacService.CanEditArticle(User, article.OwnerId == User.GetUserId()))
            return StatusCode(403, new { error = "You do not have permission to delete attachments from this article" });

        var attachment = await db.ArticleAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId && a.ArticleId == articleId);
        if (attachment == null) return NotFound(new { error = "Attachment not found" });

        // Delete file from disk
        var filePath = AttachmentHelper.GetFilePath(config, articleId, attachment.StoredFileName);
        db.ArticleAttachments.Remove(attachment);
        article.ApprovedById = null;
        article.ApprovedAt = null;
        await db.SaveChangesAsync();
        try { AttachmentHelper.MoveToTrash(config, articleId, attachment.StoredFileName); }
        catch (Exception ex) { logger.LogError(ex, "Failed to move deleted attachment {AttachmentId} to trash", attachmentId); }

        // Attachment text is part of the search index
        await articleService.QueueReindexAsync(article);

        return Ok(new { message = "Attachment deleted" });
    }

    [HttpGet("api/attachments/{id}/download")]
    [AllowAnonymous]
    public async Task<IActionResult> Download(string id, [FromQuery] string? token = null)
    {
        // Allow token via query param (for <img> tags that can't send headers)
        if (!User.Identity?.IsAuthenticated == true)
        {
            if (string.IsNullOrEmpty(token))
                return Unauthorized(new { error = "Authentication required" });

            // Validate the token manually
            var jwtService = HttpContext.RequestServices.GetRequiredService<JwtService>();
            var principal = jwtService.ValidateToken(token);
            if (principal == null)
                return Unauthorized(new { error = "Invalid token" });
        }

        var attachment = await db.ArticleAttachments.FindAsync(id);
        if (attachment == null) return NotFound(new { error = "Attachment not found" });

        var filePath = AttachmentHelper.GetFilePath(config, attachment.ArticleId, attachment.StoredFileName);

        if (!System.IO.File.Exists(filePath))
            return NotFound(new { error = "File not found on disk" });

        return PhysicalFile(filePath, attachment.ContentType, attachment.FileName);
    }
}
