using KnowledgePortal.Api.Models.Entities;
using KnowledgePortal.Api.Helpers;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace KnowledgePortal.Api.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(AppDbContext db)
    {
        // Admin user
        if (!await db.Users.AnyAsync(u => u.Email == "admin@knowledge.local"))
        {
            db.Users.Add(new User
            {
                Name = "Admin",
                Slug = "admin",
                Email = "admin@knowledge.local",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123", 12),
                Role = "admin"
            });
            await db.SaveChangesAsync();
        }

        // Backfill slugs for any users missing them
        var usersWithoutSlug = await db.Users.Where(u => u.Slug == "" || u.Slug == null!).ToListAsync();
        foreach (var user in usersWithoutSlug)
        {
            user.Slug = await GenerateUniqueUserSlugAsync(db, user.Name);
        }
        if (usersWithoutSlug.Count > 0) await db.SaveChangesAsync();

        // Default tags
        string[] defaultTags =
        [
            "getting-started", "tutorial", "troubleshooting", "best-practices",
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
            var contentTypes = new (string value, string label, int order)[]
            {
                ("reference", "Reference", 1),
                ("how-to", "How-To Guide", 2),
                ("adr", "ADR", 3),
                ("runbook", "Runbook", 4),
                ("faq", "FAQ", 5),
                ("policy", "Policy", 6),
                ("onboarding", "Onboarding", 7),
            };

            foreach (var (value, label, order) in contentTypes)
            {
                db.LookupValues.Add(new LookupValue
                {
                    Category = "content_type",
                    Value = value,
                    Label = label,
                    SortOrder = order
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

        var admin = await db.Users.FirstAsync(u => u.Email == "admin@knowledge.local");
        var allTags = await db.Tags.ToListAsync();

        var files = Directory.GetFiles(seedPath, "*.json").OrderBy(f => f);

        foreach (var file in files)
        {
            var json = await File.ReadAllTextAsync(file);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var title = root.GetProperty("title").GetString()!;
            var baseSlug = SlugHelper.GenerateSlug(title);
            var slug = baseSlug;
            var counter = 1;
            while (await db.Articles.AnyAsync(a => a.Slug == slug))
            {
                slug = $"{baseSlug}-{counter}";
                counter++;
            }
            var contentType = root.GetProperty("contentType").GetString() ?? "reference";
            var excerpt = root.TryGetProperty("excerpt", out var exc) ? exc.GetString() : null;
            var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "published" : "published";
            var contentJson = root.GetProperty("content").GetRawText();

            var article = new Article
            {
                Title = title,
                Slug = slug,
                Content = contentJson,
                Excerpt = excerpt,
                ContentType = contentType,
                Status = status,
                OwnerId = admin.Id,
                ReadTimeMinutes = ContentExtractor.CalculateReadTime(contentJson),
                PublishedAt = status == "published" ? DateTime.UtcNow : null,
                LastReviewedAt = status == "published" ? DateTime.UtcNow : null,
            };

            db.Articles.Add(article);
            await db.SaveChangesAsync();

            // Assign tags
            if (root.TryGetProperty("tags", out var tagsEl))
            {
                foreach (var tagEl in tagsEl.EnumerateArray())
                {
                    var tagSlug = tagEl.GetString();
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
    }

    public static async Task<string> GenerateUniqueUserSlugAsync(AppDbContext db, string name)
    {
        var baseSlug = SlugHelper.GenerateSlug(name);
        var slug = baseSlug;
        var counter = 1;
        while (await db.Users.AnyAsync(u => u.Slug == slug))
        {
            slug = $"{baseSlug}-{counter}";
            counter++;
        }
        return slug;
    }
}
