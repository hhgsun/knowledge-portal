using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace KnowledgePortal.Api.Services;

public class SourceImportService(AppDbContext db, ArticleService articleService, ArticleMutationService mutations, IConfiguration config,
    ILogger<SourceImportService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> TextExtensions = [".txt", ".md", ".markdown", ".csv", ".tsv", ".json", ".yaml", ".yml"];

    public async Task<SourceImportPreview> AnalyzeAsync(IFormFile file, int index, CancellationToken ct)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var title = Path.GetFileNameWithoutExtension(file.FileName).Replace('_', ' ').Replace('-', ' ').Trim();
        try
        {
            object content;
            if (extension is ".xlsx" or ".pdf" or ".docx" or ".pptx" or ".csv")
                content = await ReadStructuredUploadAsync(file, extension, ct);
            else if (TextExtensions.Contains(extension))
            {
                await using var stream = file.OpenReadStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: true);
                var text = await reader.ReadToEndAsync(ct);
                content = extension switch
                {
                    ".md" or ".markdown" => text,
                    ".tsv" => DelimitedToDoc(text, '\t'),
                    ".json" or ".yaml" or ".yml" => Doc(CodeBlock(text, extension.TrimStart('.'))),
                    _ => PlainTextToDoc(text)
                };
            }
            else
                return Preview(index, file.FileName, title, Doc(), false, "attachment",
                    warning: "This file cannot be converted; it will be kept as an attachment.");

            var markdown = content is string rawMarkdown ? rawMarkdown : ToMarkdown(JsonSerializer.SerializeToElement(content, JsonOptions));
            var plain = ContentExtractor.ExtractPlainText(markdown);
            if (string.IsNullOrWhiteSpace(plain))
                return Preview(index, file.FileName, title, Doc(), false, "attachment",
                    warning: "No usable text was found; the original will be kept as an attachment.");
            var excerpt = plain.Length <= 240 ? plain : plain[..240].TrimEnd() + "…";
            return new(index, file.FileName, title, excerpt, markdown, true, true, "content-and-attachment", null, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Source file {FileName} could not be analyzed", file.FileName);
            var reason = ex switch
            {
                IOException => $"The file could not be read: {ex.Message}",
                InvalidDataException or JsonException => $"The file content could not be parsed: {ex.Message}",
                _ => "The file could not be parsed. It may be damaged or its contents may not match the file extension."
            };
            return Preview(index, file.FileName, title, Doc(), false, "failed", analysisError: reason);
        }
    }

    private async Task<string> ReadStructuredUploadAsync(IFormFile file, string extension,
        CancellationToken ct)
    {
        var temporary = Path.Combine(Path.GetTempPath(), $"kp_source_{Guid.NewGuid():N}{extension}");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 128 * 1024, FileOptions.Asynchronous))
            {
                await using var input = file.OpenReadStream();
                await input.CopyToAsync(output, ct);
                await output.FlushAsync(ct);
            }
            var extraction = AttachmentTextExtractor.Extract(temporary, extension,
                Math.Clamp(config.GetValue("FileStorage:MaxExtractedCharacters",
                    AttachmentTextExtractor.DefaultMaxCharacters), 1_000, 5_000_000));
            if (extraction.Status == "failed")
                throw new InvalidDataException(extraction.Error ?? "Structured extraction failed");
            return extraction.Text;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public async Task<SourceImportCommitResult> CommitAsync(SourceImportCommitRequest request, IReadOnlyList<IFormFile> files,
        IReadOnlyList<IFormFile> attachments, ClaimsPrincipal user, CancellationToken ct)
    {
        var results = new List<SourceImportCommitItem>();
        var maxSize = config.GetValue("FileStorage:MaxFileSizeMB", 20) * 1024L * 1024L;
        var maxAttachments = config.GetValue("FileStorage:MaxAttachmentsPerArticle", 20);
        var allowed = config.GetSection("FileStorage:AllowedExtensions").Get<string[]>() ?? [];
        foreach (var draft in request.Drafts)
        {
            var title = draft.Title?.Trim() ?? "";
            var fileName = draft.SourceIndex >= 0 && draft.SourceIndex < files.Count
                ? Path.GetFileName(files[draft.SourceIndex].FileName)
                : $"Source #{draft.SourceIndex + 1}";
            IDbContextTransaction? transaction = null;
            var storedAttachments = new List<string>();
            string? articleId = null;
            var committed = false;
            try
            {
                var additionalIndexes = draft.AdditionalAttachmentIndexes ?? [];
                var additionalIncludeInIndex = draft.AdditionalAttachmentIncludeInIndex
                    ?? Enumerable.Repeat(true, additionalIndexes.Length).ToArray();
                if (additionalIndexes.Distinct().Count() != additionalIndexes.Length)
                    throw new InvalidDataException("Additional attachment indexes must be unique");
                if (additionalIndexes.Any(index => index < 0 || index >= attachments.Count))
                    throw new InvalidDataException("An additional attachment is missing from the request");
                if (additionalIncludeInIndex.Length != additionalIndexes.Length)
                    throw new InvalidDataException(
                        "Additional attachment index-inclusion flags must match the attachment indexes");
                var attachmentCount = additionalIndexes.Length
                    + (draft.KeepOriginal && draft.SourceIndex >= 0 && draft.SourceIndex < files.Count ? 1 : 0);
                if (attachmentCount > maxAttachments)
                    throw new InvalidDataException($"Maximum {maxAttachments} attachments per article reached");

                if (db.Database.IsRelational())
                    transaction = await db.Database.BeginTransactionAsync(ct);
                var contentMarkdown = draft.ContentMarkdown?.Trim() ?? "";
                var create = await mutations.CreateAsync(
                    new CreateArticleCommand(title, contentMarkdown, draft.Excerpt, draft.Status,
                        draft.ContentType, draft.Tags),
                    user, "Source import", queueReindex: false, ct: ct);
                if (create.Error != null) throw new InvalidDataException(create.Error.Message);
                var article = create.Article!;
                articleId = article.Id;

                if (draft.KeepOriginal && draft.SourceIndex >= 0 && draft.SourceIndex < files.Count)
                    storedAttachments.Add(await SaveAttachmentAsync(article, files[draft.SourceIndex], maxSize, allowed,
                        user.GetUserId(), draft.OriginalIncludeInIndex, ct));
                for (var i = 0; i < additionalIndexes.Length; i++)
                    storedAttachments.Add(await SaveAttachmentAsync(article,
                        attachments[additionalIndexes[i]], maxSize, allowed, user.GetUserId(),
                        additionalIncludeInIndex[i], ct));
                await articleService.QueueReindexAsync(article, ct);
                if (transaction != null) await transaction.CommitAsync(ct);
                committed = true;
                results.Add(new(draft.SourceIndex, fileName, article.Id, article.Slug, article.Title, null));
            }
            catch (OperationCanceledException)
            {
                await RollbackDraftAsync(transaction, articleId);
                throw;
            }
            catch (Exception ex)
            {
                await RollbackDraftAsync(transaction, articleId);
                logger.LogWarning(ex, "Source import row {SourceIndex} was rolled back", draft.SourceIndex);
                results.Add(new(draft.SourceIndex, fileName, null, null, title, ex.Message));
            }
            finally
            {
                if (!committed && articleId != null)
                {
                    foreach (var storedAttachment in storedAttachments)
                    {
                        try { AttachmentHelper.MoveToTrash(config, articleId, storedAttachment); }
                        catch (Exception ex) { logger.LogError(ex, "Failed to clean rolled-back source attachment {StoredFileName}", storedAttachment); }
                    }
                }
                if (transaction != null) await transaction.DisposeAsync();
            }
        }
        return new(results.Count(x => x.ArticleId != null), results.Count(x => x.Error != null), results.ToArray());
    }

    private async Task<string> SaveAttachmentAsync(Article article, IFormFile file, long maxSize,
        string[] allowed, string userId, bool includeInIndex, CancellationToken ct)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (file.Length == 0 || file.Length > maxSize) throw new InvalidDataException("Original file is empty or exceeds the attachment size limit");
        if (!allowed.Contains(ext, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException($"File type '{ext}' is not allowed as an attachment");
        var stored = $"{Guid.NewGuid():N}"[..21] + ext;
        var dir = AttachmentHelper.GetArticleDirectory(config, article.Id);
        Directory.CreateDirectory(dir);
        string sha256;
        try
        {
            await using (var input = file.OpenReadStream())
                sha256 = await AttachmentHelper.SaveAtomicAsync(config, article.Id, stored, input, ct);
            db.ArticleAttachments.Add(new ArticleAttachment
            {
                ArticleId = article.Id,
                FileName = Path.GetFileName(file.FileName),
                StoredFileName = stored,
                ContentType = file.ContentType,
                SizeBytes = file.Length,
                Sha256 = sha256,
                IncludeInIndex = includeInIndex,
                ExtractionCharacterLimit = Math.Clamp(config.GetValue("FileStorage:MaxExtractedCharacters",
                    AttachmentTextExtractor.DefaultMaxCharacters), 1_000, 5_000_000),
                UploadedById = userId
            });
            await db.SaveChangesAsync(ct);
            return stored;
        }
        catch
        {
            try { AttachmentHelper.MoveToTrash(config, article.Id, stored); }
            catch (Exception ex) { logger.LogError(ex, "Failed to clean unsuccessful source attachment {StoredFileName}", stored); }
            throw;
        }
    }

    private async Task RollbackDraftAsync(IDbContextTransaction? transaction, string? articleId)
    {
        if (transaction != null)
            await transaction.RollbackAsync(CancellationToken.None);
        else if (articleId != null)
        {
            // The Docker-free provider has no transactions; compensate so tests and alternate
            // providers preserve the same all-or-nothing contract.
            db.ChangeTracker.Clear();
            var persisted = await db.Articles.FindAsync([articleId], CancellationToken.None);
            if (persisted != null)
            {
                db.Articles.Remove(persisted);
                await db.SaveChangesAsync(CancellationToken.None);
            }
        }
        db.ChangeTracker.Clear();
    }

    private static object MarkdownToDoc(string text)
    {
        var blocks = new List<object>();
        foreach (var line in text.Replace("\r", "").Split('\n'))
        {
            var match = Regex.Match(line, "^(#{1,3})\\s+(.+)$");
            if (match.Success) blocks.Add(Heading(match.Groups[2].Value, match.Groups[1].Value.Length));
            else if (line.StartsWith("> ")) blocks.Add(new { type = "blockquote", content = new[] { Paragraph(line[2..]) } });
            else if (!string.IsNullOrWhiteSpace(line)) blocks.Add(Paragraph(line));
        }
        return new { type = "doc", content = blocks };
    }

    private static object DelimitedToDoc(string text, char separator)
    {
        var rows = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select((line, i) => new { type = "tableRow", content = line.Split(separator).Select(value => Cell(value.Trim().Trim('"'), i == 0)).ToArray() }).ToArray();
        return Doc(new { type = "table", content = rows });
    }
    private static object PlainTextToDoc(string text) => new { type = "doc", content = Paragraphs(text).ToArray() };
    private static IEnumerable<object> Paragraphs(string text) => text.Replace("\r", "").Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Select(x => Paragraph(x.Trim()));
    private static object Doc(params object[] content) => new { type = "doc", content };
    private static object Paragraph(string text) => new { type = "paragraph", content = string.IsNullOrEmpty(text) ? [] : new[] { new { type = "text", text } } };
    private static object Heading(string text, int level) => new { type = "heading", attrs = new { level }, content = new[] { new { type = "text", text } } };
    private static object Cell(string text, bool header) => new { type = header ? "tableHeader" : "tableCell", content = new[] { Paragraph(text) } };
    private static object CodeBlock(string text, string language) => new { type = "codeBlock", attrs = new { language }, content = new[] { new { type = "text", text } } };
    private static SourceImportPreview Preview(int i, string file, string title, object content, bool parsed, string mode,
        string? warning = null, string? analysisError = null)
        => new(i, file, title, null, ToMarkdown(JsonSerializer.SerializeToElement(content, JsonOptions)), parsed, true, mode,
            warning, analysisError);

    private static string ToMarkdown(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Array) return string.Join("\n", node.EnumerateArray().Select(ToMarkdown));
        if (node.ValueKind != JsonValueKind.Object) return node.ValueKind == JsonValueKind.String ? node.GetString() ?? "" : "";
        var type = node.TryGetProperty("type", out var t) ? t.GetString() : null;
        var children = node.TryGetProperty("content", out var c) ? ToMarkdown(c) : node.TryGetProperty("text", out var x) ? x.GetString() ?? "" : "";
        return type switch
        {
            "doc" => children.Trim(),
            "heading" => new string('#', node.TryGetProperty("attrs", out var a) && a.TryGetProperty("level", out var l) ? l.GetInt32() : 2) + " " + children + "\n",
            "paragraph" => children + "\n",
            "blockquote" => string.Join("\n", children.Split('\n').Where(s => s.Length > 0).Select(s => "> " + s)) + "\n",
            "codeBlock" => "```\n" + children + "\n```\n",
            "tableCell" or "tableHeader" => children + " | ",
            "table" or "tableRow" => children + "\n",
            _ => children
        };
    }
}
