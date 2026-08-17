using KnowledgePortal.Api.Models.Entities;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace KnowledgePortal.Api.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(AppDbContext db)
    {
        // Admin user
        if (!await db.Users.AnyAsync(u => u.Email == "admin@finagotech.com.tr"))
        {
            db.Users.Add(new User
            {
                Name = "Admin",
                Slug = "admin",
                Email = "admin@finagotech.com.tr",
                PasswordHash = UserService.HashPassword("1q2w3E*/"),
                Role = "admin"
            });
            await db.SaveChangesAsync();
        }

        // Backfill slugs for any users missing them
        var usersWithoutSlug = await db.Users.Where(u => u.Slug == "" || u.Slug == null!).ToListAsync();
        foreach (var user in usersWithoutSlug)
        {
            user.Slug = await db.GenerateUniqueUserSlugAsync(user.Name);
        }
        if (usersWithoutSlug.Count > 0) await db.SaveChangesAsync();

        // Default tags
        string[] defaultTags =
        [
            "project-knowledge-portal", "getting-started", "tutorial", "troubleshooting", "best-practices",
            "api", "deployment", "security", "performance", "testing", "monitoring"
        ];

        foreach (var slug in defaultTags)
        {
            if (!await db.Tags.AnyAsync(t => t.Slug == slug))
            {
                db.Tags.Add(new Tag
                {
                    Name = slug.Replace("-", " "),
                    Slug = slug
                });
            }
        }
        await db.SaveChangesAsync();

        // Default lookup values
        if (!await db.LookupValues.AnyAsync())
        {
            var contentTypes = new (string value, string label, int order, string color, string icon)[]
            {
                ("reference", "Reference", 1, "blue", "book-open"),
                ("how-to", "How-To Guide", 2, "green", "list-checks"),
                ("adr", "ADR", 3, "purple", "scale"),
                ("runbook", "Runbook", 4, "orange", "terminal"),
                ("faq", "FAQ", 5, "amber", "circle-help"),
                ("policy", "Policy", 6, "red", "shield"),
                ("onboarding", "Onboarding", 7, "teal", "rocket"),
            };

            foreach (var (value, label, order, color, icon) in contentTypes)
            {
                db.LookupValues.Add(new LookupValue
                {
                    Category = "content_type",
                    Value = value,
                    Label = label,
                    SortOrder = order,
                    Color = color,
                    Icon = icon
                });
            }

            await db.SaveChangesAsync();
        }

        // Seed articles (only if no articles exist)
        if (!await db.Articles.AnyAsync())
        {
            await SeedArticlesAsync(db);
        }
    }

    private static async Task SeedArticlesAsync(AppDbContext db)
    {
        var seedPath = Path.Combine(AppContext.BaseDirectory, "SeedData", "articles");
        if (!Directory.Exists(seedPath)) return;

        var admin = await db.Users.FirstAsync(u => u.Email == "admin@finagotech.com.tr");
        var allTags = await db.Tags.ToListAsync();

        var files = Directory.GetFiles(seedPath, "*.md").OrderBy(f => f);

        foreach (var file in files)
        {
            var source = await File.ReadAllTextAsync(file);
            var (metadata, contentMarkdown) = ParseMarkdownSeed(source, file);

            var title = metadata.Title;
            var slug = await db.GenerateUniqueArticleSlugAsync(title);

            var article = new Article
            {
                Title = title,
                Slug = slug,
                Content = contentMarkdown,
                Excerpt = metadata.Excerpt,
                ContentType = metadata.ContentType,
                Status = metadata.Status,
                OwnerId = admin.Id,
                ReadTimeMinutes = ContentExtractor.CalculateReadTime(contentMarkdown),
                PublishedAt = metadata.Status == "published" ? DateTime.UtcNow : null,
                LastReviewedAt = null,
                VersionCounter = 1,
            };

            db.Articles.Add(article);
            db.ArticleVersions.Add(new ArticleVersion
            {
                ArticleId = article.Id,
                Title = article.Title,
                Content = article.Content,
                ChangedBy = admin.Id,
                ChangeSummary = "Initial seeded version",
                Version = 1
            });
            await db.SaveChangesAsync();

            // Assign tags
            foreach (var tagSlug in metadata.Tags)
            {
                var tag = allTags.FirstOrDefault(t => t.Slug == tagSlug);
                if (tag != null)
                {
                    db.ArticleTags.Add(new ArticleTag
                    {
                        ArticleId = article.Id,
                        TagId = tag.Id
                    });
                }
            }
            await db.SaveChangesAsync();
        }
    }

    internal static (SeedArticleMetadata Metadata, string Markdown) ParseMarkdownSeed(string source, string fileName)
    {
        var normalized = source.Replace("\r\n", "\n");
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
            throw new InvalidDataException($"Seed article '{fileName}' is missing JSON front matter.");

        var end = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (end < 0)
            throw new InvalidDataException($"Seed article '{fileName}' has unterminated JSON front matter.");

        var metadataJson = normalized[4..end];
        var metadata = JsonSerializer.Deserialize<SeedArticleMetadata>(metadataJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException($"Seed article '{fileName}' has invalid metadata.");

        if (string.IsNullOrWhiteSpace(metadata.Title))
            throw new InvalidDataException($"Seed article '{fileName}' is missing a title.");

        var markdown = normalized[(end + 5)..].Trim();
        return (metadata, markdown);
    }

    internal sealed class SeedArticleMetadata
    {
        public string Title { get; init; } = "";
        public string ContentType { get; init; } = "reference";
        public string[] Tags { get; init; } = [];
        public string? Excerpt { get; init; }
        public string Status { get; init; } = "published";
    }
}
