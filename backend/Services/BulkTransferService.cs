using System.Security.Claims;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public class BulkTransferService(AppDbContext db, ArticleService articleService)
{
    public const int MaxRecords = 5_000;
    public const int MaxFileSizeMb = 100;
    private static readonly HashSet<string> ValidStatuses = ["draft", "published", "archived"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static byte[] CreateJsonLinesTemplate()
    {
        var row = new
        {
            externalId = "example-howto-001",
            title = "VPN Kurulum Rehberi",
            excerpt = "Windows için şirket VPN kurulumu.",
            status = "draft",
            contentType = "how-to",
            contentMarkdown = "## Kurulum adımları\n\nVPN istemcisini kurun ve kurumsal hesabınızla giriş yapın.",
            tags = new[] { "vpn", "network" }
        };
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(row, JsonOptions) + Environment.NewLine);
    }

    public static byte[] CreateCsvTemplate()
    {
        var output = new StringBuilder("externalId,title,excerpt,status,contentType,tags,contentMarkdown\r\n");
        output.AppendJoin(',', Csv("example-howto-001"), Csv("VPN Kurulum Rehberi"),
            Csv("Windows için şirket VPN kurulumu."), Csv("draft"), Csv("how-to"),
            Csv("vpn|network"), Csv("VPN istemcisini kurun ve kurumsal hesabınızla giriş yapın.")).Append("\r\n");
        return Encoding.UTF8.GetBytes(output.ToString());
    }

    public static byte[] CreateMarkdownTemplate()
    {
        var item = new BulkImportItem("example-howto-001", "VPN Kurulum Rehberi",
            "Windows için şirket VPN kurulumu.", "draft", "how-to",
            "## Kurulum adımları\n\nVPN istemcisini kurun ve kurumsal hesabınızla giriş yapın.",
            ["vpn", "network"]);
        return Encoding.UTF8.GetBytes(SerializeMarkdown(item));
    }

    public async Task<List<BulkImportItem>> ReadAsync(Stream stream, string fileName, CancellationToken ct)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".jsonl" or ".ndjson" => await ReadJsonLinesAsync(stream, ct),
            ".csv" => await ReadCsvAsync(stream, ct),
            ".md" or ".markdown" => [await ReadMarkdownAsync(stream, fileName, ct)],
            ".zip" => await ReadMarkdownArchiveAsync(stream, ct),
            _ => throw new InvalidDataException("Only .md, .markdown, .zip, .jsonl, .ndjson and .csv files are supported")
        };
    }

    public async Task<BulkImportResult> ImportAsync(
        IReadOnlyList<BulkImportItem> items, ClaimsPrincipal user, bool dryRun, string conflictPolicy, CancellationToken ct)
    {
        if (items.Count > MaxRecords)
            throw new InvalidDataException($"A single import may contain at most {MaxRecords} records");
        if (conflictPolicy is not ("skip" or "update" or "duplicate"))
            throw new InvalidDataException("conflictPolicy must be skip, update or duplicate");

        var validContentTypes = (await db.LookupValues
            .Where(x => x.Category == "content_type" && x.IsActive)
            .Select(x => x.Value).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var errors = new List<BulkImportError>();
        var created = 0;
        var updated = 0;
        var skipped = 0;
        var userId = user.GetUserId();
        var role = user.GetRole();

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var error = Validate(item, validContentTypes, role);
            if (error != null)
            {
                errors.Add(new(index + 1, item.Title, error));
                continue;
            }

            var title = item.Title.Trim();
            var existing = await db.Articles
                .Include(a => a.ArticleTags)
                .FirstOrDefaultAsync(a => a.Slug == SlugHelper.GenerateSlug(title), ct);

            if (existing != null && conflictPolicy == "skip")
            {
                skipped++;
                continue;
            }
            if (existing != null && conflictPolicy == "update" &&
                !RbacService.CanEditArticle(user, existing.OwnerId == userId))
            {
                errors.Add(new(index + 1, title, "You do not have permission to update the matching article"));
                continue;
            }

            if (dryRun)
            {
                if (existing != null && conflictPolicy == "update") updated++; else created++;
                continue;
            }

            var content = item.ContentMarkdown?.Trim();
            var status = item.Status ?? "draft";

            if (existing != null && conflictPolicy == "update")
            {
                var contentChanged = existing.Content != content;
                existing.Title = title;
                existing.Excerpt = item.Excerpt?.Trim();
                existing.Status = status;
                existing.ContentType = item.ContentType ?? "reference";
                existing.Content = content;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.ReadTimeMinutes = ContentExtractor.CalculateReadTime(content);
                if (status == "published")
                {
                    existing.PublishedAt ??= DateTime.UtcNow;
                    existing.LastReviewedAt = DateTime.UtcNow;
                }
                if (contentChanged)
                    await articleService.AddVersionAsync(existing.Id, existing.Title, content, userId, "Bulk import");
                db.ArticleTags.RemoveRange(existing.ArticleTags);
                if (item.Tags is { Length: > 0 })
                    await articleService.AttachTagsAsync(existing.Id, item.Tags, CanCreateTags(user));
                await db.SaveChangesAsync(ct);
                await articleService.QueueReindexAsync(existing);
                updated++;
            }
            else
            {
                var article = new Article
                {
                    Title = title,
                    Slug = await db.GenerateUniqueArticleSlugAsync(title),
                    Excerpt = item.Excerpt?.Trim(),
                    Status = status,
                    ContentType = item.ContentType ?? "reference",
                    Content = content,
                    OwnerId = userId,
                    CreatedViaApiKeyId = user.GetApiKeyId(),
                    ReadTimeMinutes = ContentExtractor.CalculateReadTime(content),
                    PublishedAt = status == "published" ? DateTime.UtcNow : null,
                    LastReviewedAt = null
                };
                db.Articles.Add(article);
                await articleService.AddVersionAsync(article.Id, article.Title, content, userId, "Bulk import");
                if (item.Tags is { Length: > 0 })
                    await articleService.AttachTagsAsync(article.Id, item.Tags, CanCreateTags(user));
                await db.SaveChangesAsync(ct);
                await articleService.QueueReindexAsync(article);
                created++;
            }
        }

        return new(dryRun, items.Count, created, updated, skipped, errors.Count, errors);
    }

    public async Task<byte[]> ExportJsonLinesAsync(IQueryable<Article> query, CancellationToken ct)
    {
        var articles = await query.Include(a => a.ArticleTags).ThenInclude(x => x.Tag)
            .OrderBy(a => a.CreatedAt).Take(MaxRecords).ToListAsync(ct);
        var output = new StringBuilder();
        foreach (var article in articles)
        {
            output.AppendLine(JsonSerializer.Serialize(new
            {
                externalId = article.Id,
                article.Title,
                article.Excerpt,
                article.Status,
                article.ContentType,
                contentMarkdown = article.Content,
                tags = article.ArticleTags.Select(x => x.Tag.Slug).ToArray()
            }, JsonOptions));
        }
        return Encoding.UTF8.GetBytes(output.ToString());
    }

    public async Task<byte[]> ExportCsvAsync(IQueryable<Article> query, CancellationToken ct)
    {
        var articles = await query.Include(a => a.ArticleTags).ThenInclude(x => x.Tag)
            .OrderBy(a => a.CreatedAt).Take(MaxRecords).ToListAsync(ct);
        var output = new StringBuilder("externalId,title,excerpt,status,contentType,tags,contentMarkdown\r\n");
        foreach (var a in articles)
            output.AppendJoin(',', Csv(a.Id), Csv(a.Title), Csv(a.Excerpt), Csv(a.Status), Csv(a.ContentType),
                Csv(string.Join('|', a.ArticleTags.Select(x => x.Tag.Slug))), Csv(a.Content)).Append("\r\n");
        return Encoding.UTF8.GetBytes(output.ToString());
    }

    public async Task<byte[]> ExportMarkdownArchiveAsync(IQueryable<Article> query, CancellationToken ct)
    {
        var articles = await query.Include(a => a.ArticleTags).ThenInclude(x => x.Tag)
            .OrderBy(a => a.CreatedAt).Take(MaxRecords).ToListAsync(ct);
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var article in articles)
            {
                var baseName = string.IsNullOrWhiteSpace(article.Slug) ? SlugHelper.GenerateSlug(article.Title) : article.Slug;
                if (string.IsNullOrWhiteSpace(baseName)) baseName = article.Id;
                var name = $"{baseName}.md";
                for (var suffix = 2; !usedNames.Add(name); suffix++) name = $"{baseName}-{suffix}.md";
                var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await using var writer = new StreamWriter(entryStream, new UTF8Encoding(false));
                await writer.WriteAsync(SerializeMarkdown(new BulkImportItem(article.Id, article.Title, article.Excerpt,
                    article.Status, article.ContentType, article.Content,
                    article.ArticleTags.Select(x => x.Tag.Slug).ToArray())));
            }
        }
        return output.ToArray();
    }

    private static string? Validate(BulkImportItem item, HashSet<string> contentTypes, string role)
    {
        if (string.IsNullOrWhiteSpace(item.Title) || item.Title.Trim().Length > 300) return "Title is required (1-300 chars)";
        var status = item.Status ?? "draft";
        if (!ValidStatuses.Contains(status)) return $"Invalid status '{status}'";
        if (status == "archived" && role is not ("admin" or "editor")) return "Archive permission is required";
        if (item.ContentType != null && !contentTypes.Contains(item.ContentType)) return $"Invalid contentType '{item.ContentType}'";
        return null;
    }

    private static bool CanCreateTags(ClaimsPrincipal user) =>
        user.GetSource() == "api-key" || RbacService.HasPermission(user, Permissions.TagsManage);

    private static async Task<List<BulkImportItem>> ReadJsonLinesAsync(Stream stream, CancellationToken ct)
    {
        var result = new List<BulkImportItem>();
        using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: true);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try { result.Add(JsonSerializer.Deserialize<BulkImportItem>(line, JsonOptions) ?? throw new JsonException()); }
            catch (JsonException ex) { throw new InvalidDataException($"Invalid JSONL at record {result.Count + 1}: {ex.Message}"); }
            if (result.Count > MaxRecords) throw new InvalidDataException($"A single import may contain at most {MaxRecords} records");
        }
        return result;
    }

    private static async Task<List<BulkImportItem>> ReadCsvAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: true);
        var text = await reader.ReadToEndAsync(ct);
        var rows = ParseCsv(text);
        if (rows.Count == 0) return [];
        var headers = rows[0].Select((value, index) => (value, index))
            .ToDictionary(x => x.value.Trim(), x => x.index, StringComparer.OrdinalIgnoreCase);
        if (!headers.ContainsKey("title")) throw new InvalidDataException("CSV must contain a title column");
        string? Get(List<string> row, string name) => headers.TryGetValue(name, out var i) && i < row.Count && !string.IsNullOrWhiteSpace(row[i]) ? row[i] : null;
        var result = new List<BulkImportItem>();
        foreach (var row in rows.Skip(1).Where(r => r.Any(x => !string.IsNullOrWhiteSpace(x))))
        {
            var content = Get(row, "contentMarkdown");
            result.Add(new(Get(row, "externalId"), Get(row, "title") ?? "", Get(row, "excerpt"), Get(row, "status"),
                Get(row, "contentType"), content, Get(row, "tags")?.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
            if (result.Count > MaxRecords) throw new InvalidDataException($"A single import may contain at most {MaxRecords} records");
        }
        return result;
    }

    private static async Task<BulkImportItem> ReadMarkdownAsync(Stream stream, string fileName, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: true);
        var source = await reader.ReadToEndAsync(ct);
        return ParseMarkdown(source, fileName);
    }

    private static async Task<List<BulkImportItem>> ReadMarkdownArchiveAsync(Stream stream, CancellationToken ct)
    {
        var result = new List<BulkImportItem>();
        long expandedBytes = 0;
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            foreach (var entry in archive.Entries.Where(e =>
                         e.FullName.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                         e.FullName.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase)))
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                expandedBytes += entry.Length;
                if (expandedBytes > MaxFileSizeMb * 1024L * 1024L)
                    throw new InvalidDataException($"Expanded Markdown content may not exceed {MaxFileSizeMb} MB");
                await using var entryStream = entry.Open();
                result.Add(await ReadMarkdownAsync(entryStream, entry.FullName, ct));
                if (result.Count > MaxRecords)
                    throw new InvalidDataException($"A single import may contain at most {MaxRecords} records");
            }
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) when (ex is IOException or NotSupportedException)
        {
            throw new InvalidDataException("The ZIP archive is invalid", ex);
        }
        if (result.Count == 0) throw new InvalidDataException("The ZIP archive contains no Markdown files");
        return result;
    }

    private static BulkImportItem ParseMarkdown(string source, string fileName)
    {
        var normalized = source.Replace("\r\n", "\n").TrimStart('\uFEFF');
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
            throw new InvalidDataException($"Markdown file '{fileName}' is missing JSON-compatible front matter");
        var end = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (end < 0) throw new InvalidDataException($"Markdown file '{fileName}' has unterminated front matter");
        try
        {
            var metadata = JsonSerializer.Deserialize<BulkImportItem>(normalized[4..end], JsonOptions)
                ?? throw new JsonException();
            return metadata with { ContentMarkdown = normalized[(end + 5)..].Trim() };
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Markdown file '{fileName}' has invalid front matter: {ex.Message}", ex);
        }
    }

    private static string SerializeMarkdown(BulkImportItem item)
    {
        var metadata = new
        {
            externalId = item.ExternalId,
            item.Title,
            item.Excerpt,
            status = item.Status ?? "draft",
            contentType = item.ContentType ?? "reference",
            tags = item.Tags ?? []
        };
        return $"---\n{JsonSerializer.Serialize(metadata, new JsonSerializerOptions(JsonOptions) { WriteIndented = true })}\n---\n\n{item.ContentMarkdown?.Trim() ?? ""}\n";
    }

    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var field = new StringBuilder(); var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"' && quoted && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
            else if (c == '"') quoted = !quoted;
            else if (c == ',' && !quoted) { row.Add(field.ToString()); field.Clear(); }
            else if ((c == '\r' || c == '\n') && !quoted)
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear(); rows.Add(row); row = [];
            }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }

    private static string Csv(string? value)
    {
        value ??= "";
        if (value.Length > 0 && "=+-@".Contains(value[0])) value = "'" + value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
