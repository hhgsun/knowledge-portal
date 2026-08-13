using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;
using NPOI.SS.UserModel;
using UglyToad.PdfPig;

namespace KnowledgePortal.Api.Services;

public class SourceImportService(AppDbContext db, ArticleService articleService, IConfiguration config)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> TextExtensions = [".txt", ".md", ".markdown", ".csv", ".tsv", ".json", ".yaml", ".yml"];

    public async Task<SourceImportPreview> AnalyzeAsync(IFormFile file, int index, CancellationToken ct)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var title = Path.GetFileNameWithoutExtension(file.FileName).Replace('_', ' ').Replace('-', ' ').Trim();
        try
        {
            await using var stream = file.OpenReadStream();
            object content;
            if (extension is ".xlsx" or ".xls") content = ReadWorkbook(stream);
            else if (extension == ".pdf") content = ReadPdf(stream);
            else if (extension == ".docx") content = ReadOpenXmlText(stream, true);
            else if (extension == ".pptx") content = ReadOpenXmlText(stream, false);
            else if (TextExtensions.Contains(extension))
            {
                using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: true);
                var text = await reader.ReadToEndAsync(ct);
                content = extension switch
                {
                    ".md" or ".markdown" => text,
                    ".csv" => DelimitedToDoc(text, ','),
                    ".tsv" => DelimitedToDoc(text, '\t'),
                    ".json" or ".yaml" or ".yml" => Doc(CodeBlock(text, extension.TrimStart('.'))),
                    _ => PlainTextToDoc(text)
                };
            }
            else
                return Preview(index, file.FileName, title, Doc(), false, "attachment", "This file cannot be converted; it will be kept as an attachment.");

            var markdown = content is string rawMarkdown ? rawMarkdown : ToMarkdown(JsonSerializer.SerializeToElement(content, JsonOptions));
            var plain = ContentExtractor.ExtractPlainText(markdown);
            if (string.IsNullOrWhiteSpace(plain))
                return Preview(index, file.FileName, title, Doc(), false, "attachment", "No usable text was found; the original will be kept as an attachment.");
            var excerpt = plain.Length <= 240 ? plain : plain[..240].TrimEnd() + "…";
            return new(index, file.FileName, title, excerpt, markdown, true, true, "content-and-attachment", null);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or JsonException)
        {
            return Preview(index, file.FileName, title, Doc(), false, "attachment", $"Could not parse the file: {ex.Message}");
        }
    }

    public async Task<SourceImportCommitResult> CommitAsync(SourceImportCommitRequest request, IReadOnlyList<IFormFile> files,
        ClaimsPrincipal user, CancellationToken ct)
    {
        var results = new List<SourceImportCommitItem>();
        var validTypes = (await db.LookupValues.Where(x => x.Category == "content_type" && x.IsActive)
            .Select(x => x.Value).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var maxSize = config.GetValue("FileStorage:MaxFileSizeMB", 20) * 1024L * 1024L;
        var allowed = config.GetSection("FileStorage:AllowedExtensions").Get<string[]>() ?? [];
        foreach (var draft in request.Drafts)
        {
            var title = draft.Title?.Trim() ?? "";
            try
            {
                if (title.Length is < 1 or > 300) throw new InvalidDataException("Title is required (1-300 chars)");
                var contentType = draft.ContentType ?? "reference";
                if (!validTypes.Contains(contentType)) throw new InvalidDataException($"Invalid content type: {contentType}");
                var status = draft.Status ?? "draft";
                if (status is not ("draft" or "published" or "archived")) throw new InvalidDataException("Invalid status");
                if (user.GetRole() == "viewer" && status == "archived") status = "draft";
                var contentMarkdown = draft.ContentMarkdown?.Trim() ?? "";
                var article = new Article
                {
                    Title = title, Slug = await db.GenerateUniqueArticleSlugAsync(title), Content = contentMarkdown,
                    Excerpt = draft.Excerpt?.Trim(), Status = status, ContentType = contentType,
                    OwnerId = user.GetUserId(), CreatedViaApiKeyId = user.GetApiKeyId(),
                    PublishedAt = status == "published" ? DateTime.UtcNow : null,
                    LastReviewedAt = null,
                    ReadTimeMinutes = ContentExtractor.CalculateReadTime(contentMarkdown)
                };
                db.Articles.Add(article);
                await articleService.AddVersionAsync(article.Id, article.Title, article.Content, user.GetUserId(), "Source import");
                if (draft.Tags is { Length: > 0 })
                    await articleService.AttachTagsAsync(article.Id, draft.Tags,
                        user.GetSource() == "api-key" || RbacService.HasPermission(user, Permissions.TagsManage));
                await db.SaveChangesAsync(ct);

                if (draft.KeepOriginal && draft.SourceIndex >= 0 && draft.SourceIndex < files.Count)
                    await SaveAttachmentAsync(article, files[draft.SourceIndex], maxSize, allowed, user.GetUserId(), ct);
                await articleService.QueueReindexAsync(article);
                results.Add(new(draft.SourceIndex, article.Id, article.Slug, article.Title, null));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or DbUpdateException)
            {
                results.Add(new(draft.SourceIndex, null, null, title, ex.Message));
            }
        }
        return new(results.Count(x => x.ArticleId != null), results.Count(x => x.Error != null), results.ToArray());
    }

    private async Task SaveAttachmentAsync(Article article, IFormFile file, long maxSize, string[] allowed, string userId, CancellationToken ct)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (file.Length == 0 || file.Length > maxSize) throw new InvalidDataException("Original file is empty or exceeds the attachment size limit");
        if (!allowed.Contains(ext, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException($"File type '{ext}' is not allowed as an attachment");
        var stored = $"{Guid.NewGuid():N}"[..21] + ext;
        var dir = AttachmentHelper.GetArticleDirectory(config, article.Id);
        Directory.CreateDirectory(dir);
        string sha256;
        await using (var input = file.OpenReadStream())
            sha256 = await AttachmentHelper.SaveAtomicAsync(config, article.Id, stored, input, ct);
        db.ArticleAttachments.Add(new ArticleAttachment { ArticleId = article.Id, FileName = Path.GetFileName(file.FileName), StoredFileName = stored, ContentType = file.ContentType, SizeBytes = file.Length, Sha256 = sha256, UploadedById = userId });
        await db.SaveChangesAsync(ct);
    }

    private static object ReadWorkbook(Stream stream)
    {
        using var workbook = WorkbookFactory.Create(stream);
        var blocks = new List<object>();
        for (var s = 0; s < workbook.NumberOfSheets; s++)
        {
            var sheet = workbook.GetSheetAt(s); blocks.Add(Heading(sheet.SheetName, 2));
            var rows = new List<object>();
            var formatter = new DataFormatter();
            foreach (IRow row in sheet)
            {
                var cells = new List<object>();
                var last = Math.Max(0, (int)row.LastCellNum);
                for (var c = 0; c < last; c++) cells.Add(Cell(formatter.FormatCellValue(row.GetCell(c)), row.RowNum == sheet.FirstRowNum));
                if (cells.Count > 0) rows.Add(new { type = "tableRow", content = cells });
            }
            if (rows.Count > 0) blocks.Add(new { type = "table", content = rows });
        }
        return new { type = "doc", content = blocks };
    }

    private static object ReadPdf(Stream stream)
    {
        using var pdf = PdfDocument.Open(stream); var blocks = new List<object>();
        foreach (var page in pdf.GetPages()) { blocks.Add(Heading($"Page {page.Number}", 2)); blocks.AddRange(Paragraphs(page.Text)); }
        return new { type = "doc", content = blocks };
    }

    private static object ReadOpenXmlText(Stream stream, bool word)
    {
        string text;
        if (word) { using var doc = WordprocessingDocument.Open(stream, false); text = doc.MainDocumentPart?.Document.Body?.InnerText ?? ""; }
        else { using var doc = PresentationDocument.Open(stream, false); text = string.Join("\n\n", doc.PresentationPart?.SlideParts.Select(x => x.Slide.InnerText) ?? []); }
        return PlainTextToDoc(text);
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
    private static SourceImportPreview Preview(int i, string file, string title, object content, bool parsed, string mode, string warning)
        => new(i, file, title, null, ToMarkdown(JsonSerializer.SerializeToElement(content, JsonOptions)), parsed, true, mode, warning);

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
