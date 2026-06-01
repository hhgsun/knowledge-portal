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
            e.Property(u => u.Id).HasColumnName("id");
            e.Property(u => u.Name).HasColumnName("name").IsRequired();
            e.Property(u => u.Email).HasColumnName("email").IsRequired();
            e.Property(u => u.PasswordHash).HasColumnName("password_hash").IsRequired();
            e.Property(u => u.Avatar).HasColumnName("avatar");
            e.Property(u => u.Role).HasColumnName("role").IsRequired().HasDefaultValue("viewer");
            e.Property(u => u.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(u => u.UpdatedAt).HasColumnName("updated_at").IsRequired();
            e.HasIndex(u => u.Email).IsUnique();
        });

        // ─── ApiKeys ──────────────────────────────────────
        modelBuilder.Entity<ApiKey>(e =>
        {
            e.ToTable("api_keys");
            e.HasKey(k => k.Id);
            e.Property(k => k.Id).HasColumnName("id");
            e.Property(k => k.UserId).HasColumnName("user_id").IsRequired();
            e.Property(k => k.KeyHash).HasColumnName("key_hash").IsRequired();
            e.Property(k => k.KeyPrefix).HasColumnName("key_prefix").IsRequired().HasMaxLength(8);
            e.Property(k => k.Name).HasColumnName("name").IsRequired();
            e.Property(k => k.LastUsedAt).HasColumnName("last_used_at");
            e.Property(k => k.ExpiresAt).HasColumnName("expires_at");
            e.Property(k => k.CreatedAt).HasColumnName("created_at").IsRequired();
            e.HasIndex(k => k.KeyPrefix);
            e.HasOne(k => k.User).WithMany(u => u.ApiKeys).HasForeignKey(k => k.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // ─── Tags ──────────────────────────────────────────
        modelBuilder.Entity<Tag>(e =>
        {
            e.ToTable("tags");
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).HasColumnName("id");
            e.Property(t => t.Name).HasColumnName("name").IsRequired();
            e.Property(t => t.Slug).HasColumnName("slug").IsRequired();
            e.HasIndex(t => t.Slug).IsUnique();
        });

        // ─── Articles ─────────────────────────────────────
        modelBuilder.Entity<Article>(e =>
        {
            e.ToTable("articles");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasColumnName("id");
            e.Property(a => a.Title).HasColumnName("title").IsRequired();
            e.Property(a => a.Slug).HasColumnName("slug").IsRequired();
            e.Property(a => a.Content).HasColumnName("content");
            e.Property(a => a.Excerpt).HasColumnName("excerpt");
            e.Property(a => a.Status).HasColumnName("status").IsRequired().HasDefaultValue("draft");
            e.Property(a => a.OwnerId).HasColumnName("owner_id").IsRequired();
            e.Property(a => a.ContentType).HasColumnName("content_type").IsRequired().HasDefaultValue("reference");
            e.Property(a => a.Difficulty).HasColumnName("difficulty").IsRequired().HasDefaultValue("beginner");
            e.Property(a => a.Audience).HasColumnName("audience");
            e.Property(a => a.CreatedViaApiKeyId).HasColumnName("created_via_api_key_id");
            e.Property(a => a.ReadTimeMinutes).HasColumnName("read_time_minutes");
            e.Property(a => a.PublishedAt).HasColumnName("published_at");
            e.Property(a => a.LastReviewedAt).HasColumnName("last_reviewed_at");
            e.Property(a => a.ReviewIntervalDays).HasColumnName("review_interval_days").HasDefaultValue(90);
            e.Property(a => a.IndexedAt).HasColumnName("indexed_at");
            e.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(a => a.UpdatedAt).HasColumnName("updated_at").IsRequired();
            e.HasIndex(a => a.Slug).IsUnique();
            e.HasOne(a => a.Owner).WithMany(u => u.Articles).HasForeignKey(a => a.OwnerId);
            e.HasOne(a => a.CreatedViaApiKey).WithMany().HasForeignKey(a => a.CreatedViaApiKeyId).OnDelete(DeleteBehavior.SetNull);
        });

        // ─── ArticleVersions ──────────────────────────────
        modelBuilder.Entity<ArticleVersion>(e =>
        {
            e.ToTable("article_versions");
            e.HasKey(v => v.Id);
            e.Property(v => v.Id).HasColumnName("id");
            e.Property(v => v.ArticleId).HasColumnName("article_id").IsRequired();
            e.Property(v => v.Title).HasColumnName("title").IsRequired();
            e.Property(v => v.Content).HasColumnName("content");
            e.Property(v => v.ChangedBy).HasColumnName("changed_by").IsRequired();
            e.Property(v => v.ChangeSummary).HasColumnName("change_summary");
            e.Property(v => v.Version).HasColumnName("version").IsRequired();
            e.Property(v => v.CreatedAt).HasColumnName("created_at").IsRequired();
            e.HasOne(v => v.Article).WithMany(a => a.Versions).HasForeignKey(v => v.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(v => v.ChangedByUser).WithMany().HasForeignKey(v => v.ChangedBy);
        });

        // ─── ArticleTags (composite PK) ───────────────────
        modelBuilder.Entity<ArticleTag>(e =>
        {
            e.ToTable("article_tags");
            e.HasKey(at => new { at.ArticleId, at.TagId });
            e.Property(at => at.ArticleId).HasColumnName("article_id");
            e.Property(at => at.TagId).HasColumnName("tag_id");
            e.HasOne(at => at.Article).WithMany(a => a.ArticleTags).HasForeignKey(at => at.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(at => at.Tag).WithMany(t => t.ArticleTags).HasForeignKey(at => at.TagId).OnDelete(DeleteBehavior.Cascade);
        });

        // ─── ArticleFeedback ──────────────────────────────
        modelBuilder.Entity<ArticleFeedback>(e =>
        {
            e.ToTable("article_feedback");
            e.HasKey(f => f.Id);
            e.Property(f => f.Id).HasColumnName("id");
            e.Property(f => f.ArticleId).HasColumnName("article_id").IsRequired();
            e.Property(f => f.UserId).HasColumnName("user_id");
            e.Property(f => f.Helpful).HasColumnName("helpful").IsRequired();
            e.Property(f => f.Comment).HasColumnName("comment");
            e.Property(f => f.CreatedAt).HasColumnName("created_at").IsRequired();
            e.HasOne(f => f.Article).WithMany(a => a.Feedback).HasForeignKey(f => f.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(f => f.User).WithMany().HasForeignKey(f => f.UserId);
        });

        // ─── ArticleViews ─────────────────────────────────
        modelBuilder.Entity<ArticleView>(e =>
        {
            e.ToTable("article_views");
            e.HasKey(v => v.Id);
            e.Property(v => v.Id).HasColumnName("id");
            e.Property(v => v.ArticleId).HasColumnName("article_id").IsRequired();
            e.Property(v => v.UserId).HasColumnName("user_id");
            e.Property(v => v.CreatedAt).HasColumnName("created_at").IsRequired();
            e.HasOne(v => v.Article).WithMany(a => a.Views).HasForeignKey(v => v.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(v => v.User).WithMany().HasForeignKey(v => v.UserId);
        });

        // ─── SearchQueries ────────────────────────────────
        modelBuilder.Entity<SearchQuery>(e =>
        {
            e.ToTable("search_queries");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasColumnName("id");
            e.Property(s => s.Query).HasColumnName("query").IsRequired();
            e.Property(s => s.UserId).HasColumnName("user_id");
            e.Property(s => s.ResultsCount).HasColumnName("results_count").HasDefaultValue(0);
            e.Property(s => s.ClickedArticleId).HasColumnName("clicked_article_id");
            e.Property(s => s.SearchType).HasColumnName("search_type").IsRequired().HasDefaultValue("fulltext");
            e.Property(s => s.ResponseTimeMs).HasColumnName("response_time_ms");
            e.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
            e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId);
            e.HasOne(s => s.ClickedArticle).WithMany().HasForeignKey(s => s.ClickedArticleId);
        });
    }
}
