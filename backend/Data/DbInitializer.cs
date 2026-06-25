using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

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
                Email = "admin@knowledge.local",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123", 12),
                Role = "admin"
            });
            await db.SaveChangesAsync();
        }

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
    }
}
