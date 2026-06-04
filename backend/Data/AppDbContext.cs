using Microsoft.EntityFrameworkCore;
using KnowledgePortal.Api.Models.Entities;

namespace KnowledgePortal.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<ArticleVersion> ArticleVersions => Set<ArticleVersion>();
    public DbSet<ArticleTag> ArticleTags => Set<ArticleTag>();
    public DbSet<ArticleFeedback> ArticleFeedback => Set<ArticleFeedback>();
    public DbSet<ArticleView> ArticleViews => Set<ArticleView>();
    public DbSet<SearchQuery> SearchQueries => Set<SearchQuery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ─── Users ─────────────────────────────────────────
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(u => u.Id);
            e.Property(u => u.Name).IsRequired();
            e.Property(u => u.Email).IsRequired();
            e.Property(u => u.PasswordHash).IsRequired();
            e.Property(u => u.Role).IsRequired().HasDefaultValue("viewer");
            e.Property(u => u.CreatedAt).IsRequired();
            e.Property(u => u.UpdatedAt).IsRequired();
            e.HasIndex(u => u.Email).IsUnique();
        });

        // ─── ApiKeys ──────────────────────────────────────
        modelBuilder.Entity<ApiKey>(e =>
        {
            e.ToTable("api_keys");
            e.HasKey(k => k.Id);
            e.Property(k => k.UserId).IsRequired();
            e.Property(k => k.KeyHash).IsRequired();
            e.Property(k => k.KeyPrefix).IsRequired().HasMaxLength(8);
            e.Property(k => k.Name).IsRequired();
            e.Property(k => k.CreatedAt).IsRequired();
            e.HasIndex(k => k.KeyPrefix);
            e.HasOne(k => k.User).WithMany(u => u.ApiKeys).HasForeignKey(k => k.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ─── Tags ──────────────────────────────────────────
        modelBuilder.Entity<Tag>(e =>
        {
            e.ToTable("tags");
            e.HasKey(t => t.Id);
            e.Property(t => t.Name).IsRequired();
            e.Property(t => t.Slug).IsRequired();
            e.HasIndex(t => t.Slug).IsUnique();
        });

        // ─── Articles ─────────────────────────────────────
        modelBuilder.Entity<Article>(e =>
        {
            e.ToTable("articles");
            e.HasKey(a => a.Id);
            e.Property(a => a.Title).IsRequired();
            e.Property(a => a.Slug).IsRequired();
            e.Property(a => a.Status).IsRequired().HasDefaultValue("draft");
            e.Property(a => a.OwnerId).IsRequired();
            e.Property(a => a.ContentType).IsRequired().HasDefaultValue("reference");
            e.Property(a => a.Difficulty).IsRequired().HasDefaultValue("beginner");
            e.Property(a => a.ReviewIntervalDays).HasDefaultValue(90);
            e.Property(a => a.CreatedAt).IsRequired();
            e.Property(a => a.UpdatedAt).IsRequired();
            e.HasIndex(a => a.Slug).IsUnique();
            e.HasOne(a => a.Owner).WithMany(u => u.Articles).HasForeignKey(a => a.OwnerId);
            e.HasOne(a => a.CreatedViaApiKey).WithMany().HasForeignKey(a => a.CreatedViaApiKeyId).OnDelete(DeleteBehavior.SetNull);
        });

        // ─── ArticleVersions ──────────────────────────────
        modelBuilder.Entity<ArticleVersion>(e =>
        {
            e.ToTable("article_versions");
            e.HasKey(v => v.Id);
            e.Property(v => v.ArticleId).IsRequired();
            e.Property(v => v.Title).IsRequired();
            e.Property(v => v.ChangedBy).IsRequired();
            e.Property(v => v.Version).IsRequired();
            e.Property(v => v.CreatedAt).IsRequired();
            e.HasOne(v => v.Article).WithMany(a => a.Versions).HasForeignKey(v => v.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(v => v.ChangedByUser).WithMany().HasForeignKey(v => v.ChangedBy);
        });

        // ─── ArticleTags (composite PK) ───────────────────
        modelBuilder.Entity<ArticleTag>(e =>
        {
            e.ToTable("article_tags");
            e.HasKey(at => new { at.ArticleId, at.TagId });
            e.HasOne(at => at.Article).WithMany(a => a.ArticleTags).HasForeignKey(at => at.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(at => at.Tag).WithMany(t => t.ArticleTags).HasForeignKey(at => at.TagId).OnDelete(DeleteBehavior.Cascade);
        });

        // ─── ArticleFeedback ──────────────────────────────
        modelBuilder.Entity<ArticleFeedback>(e =>
        {
            e.ToTable("article_feedback");
            e.HasKey(f => f.Id);
            e.Property(f => f.ArticleId).IsRequired();
            e.Property(f => f.Helpful).IsRequired(false);
            e.Property(f => f.CreatedAt).IsRequired();
            e.HasOne(f => f.Article).WithMany(a => a.Feedback).HasForeignKey(f => f.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(f => f.User).WithMany().HasForeignKey(f => f.UserId);
        });

        // ─── ArticleViews ─────────────────────────────────
        modelBuilder.Entity<ArticleView>(e =>
        {
            e.ToTable("article_views");
            e.HasKey(v => v.Id);
            e.Property(v => v.ArticleId).IsRequired();
            e.Property(v => v.CreatedAt).IsRequired();
            e.HasOne(v => v.Article).WithMany(a => a.Views).HasForeignKey(v => v.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(v => v.User).WithMany().HasForeignKey(v => v.UserId);
        });

        // ─── SearchQueries ────────────────────────────────
        modelBuilder.Entity<SearchQuery>(e =>
        {
            e.ToTable("search_queries");
            e.HasKey(s => s.Id);
            e.Property(s => s.Query).IsRequired();
            e.Property(s => s.ResultsCount).HasDefaultValue(0);
            e.Property(s => s.SearchType).IsRequired().HasDefaultValue("fulltext");
            e.Property(s => s.CreatedAt).IsRequired();
            e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId);
            e.HasOne(s => s.ClickedArticle).WithMany().HasForeignKey(s => s.ClickedArticleId);
        });
    }
}
