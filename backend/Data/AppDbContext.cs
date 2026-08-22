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
    public DbSet<ArticleVote> ArticleVotes => Set<ArticleVote>();
    public DbSet<ArticleComment> ArticleComments => Set<ArticleComment>();
    public DbSet<ArticleView> ArticleViews => Set<ArticleView>();
    public DbSet<SearchQuery> SearchQueries => Set<SearchQuery>();
    public DbSet<ArticleAttachment> ArticleAttachments => Set<ArticleAttachment>();
    public DbSet<LookupValue> LookupValues => Set<LookupValue>();
    public DbSet<FeaturedLink> FeaturedLinks => Set<FeaturedLink>();
    public DbSet<ArticleEmbedding> ArticleEmbeddings => Set<ArticleEmbedding>();
    public DbSet<IndexJob> IndexJobs => Set<IndexJob>();
    public DbSet<UsageEvent> UsageEvents => Set<UsageEvent>();
    public DbSet<RagEvaluationDataset> RagEvaluationDatasets => Set<RagEvaluationDataset>();
    public DbSet<RagEvaluationRun> RagEvaluationRuns => Set<RagEvaluationRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ─── pgvector extension ─────────────────────────────
        modelBuilder.HasPostgresExtension("vector");

        // ─── Users ─────────────────────────────────────────
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(u => u.Id);
            e.Property(u => u.Name).IsRequired();
            e.Property(u => u.Slug).IsRequired();
            e.Property(u => u.Email).IsRequired();
            e.Property(u => u.PasswordHash).IsRequired();
            e.Property(u => u.Role).IsRequired().HasDefaultValue("viewer");
            e.Property(u => u.CreatedAt).IsRequired();
            e.Property(u => u.UpdatedAt).IsRequired();
            e.HasIndex(u => u.Email).IsUnique();
            e.HasIndex(u => u.Slug).IsUnique();
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
            e.Property(a => a.ExternalId).HasMaxLength(200);
            e.Property(a => a.ReviewIntervalDays).HasDefaultValue(90);
            e.Property(a => a.VersionCounter).HasDefaultValue(0);
            e.Property(a => a.CreatedAt).IsRequired();
            e.Property(a => a.UpdatedAt).IsRequired();
            e.HasIndex(a => a.Slug).IsUnique();
            e.HasIndex(a => a.ExternalId).IsUnique();
            // Nearly every read path filters WHERE Status='published' (search, listings, counts,
            // MCP tools), while the durable queue scans separate lexical and semantic dirty flags.
            // Both pairs keep the queue selective at large corpus sizes.
            e.HasIndex(a => new { a.Status, a.IndexedAt });
            e.HasIndex(a => new { a.Status, a.FtsIndexedAt });
            e.HasOne(a => a.Owner).WithMany(u => u.Articles).HasForeignKey(a => a.OwnerId);
            e.HasOne(a => a.ApprovedBy).WithMany().HasForeignKey(a => a.ApprovedById).OnDelete(DeleteBehavior.SetNull);
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
            e.HasIndex(v => new { v.ArticleId, v.Version }).IsUnique();
        });

        // ─── ArticleTags (composite PK) ───────────────────
        modelBuilder.Entity<ArticleTag>(e =>
        {
            e.ToTable("article_tags");
            e.HasKey(at => new { at.ArticleId, at.TagId });
            e.HasOne(at => at.Article).WithMany(a => a.ArticleTags).HasForeignKey(at => at.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(at => at.Tag).WithMany(t => t.ArticleTags).HasForeignKey(at => at.TagId).OnDelete(DeleteBehavior.Cascade);
        });

        // ─── ArticleVotes ─────────────────────────────────────────
        modelBuilder.Entity<ArticleVote>(e =>
        {
            e.ToTable("article_votes");
            e.HasKey(v => v.Id);
            e.Property(v => v.ArticleId).IsRequired();
            e.Property(v => v.UserId).IsRequired();
            e.Property(v => v.IsHelpful).IsRequired();
            e.Property(v => v.CreatedAt).IsRequired();
            e.Property(v => v.UpdatedAt).IsRequired();
            e.HasIndex(v => new { v.ArticleId, v.UserId }).IsUnique();
            e.HasOne(v => v.Article).WithMany(a => a.Votes).HasForeignKey(v => v.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(v => v.User).WithMany().HasForeignKey(v => v.UserId);
        });

        // ─── ArticleComments ──────────────────────────────────────
        modelBuilder.Entity<ArticleComment>(e =>
        {
            e.ToTable("article_comments");
            e.HasKey(c => c.Id);
            e.Property(c => c.ArticleId).IsRequired();
            e.Property(c => c.UserId).IsRequired();
            e.Property(c => c.Comment).IsRequired();
            e.Property(c => c.CreatedAt).IsRequired();
            e.HasOne(c => c.Article).WithMany(a => a.Comments).HasForeignKey(c => c.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.User).WithMany().HasForeignKey(c => c.UserId);
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

        // ─── ArticleAttachments ─────────────────────────────
        modelBuilder.Entity<ArticleAttachment>(e =>
        {
            e.ToTable("article_attachments");
            e.HasKey(a => a.Id);
            e.Property(a => a.ArticleId).IsRequired();
            e.Property(a => a.FileName).IsRequired();
            e.Property(a => a.StoredFileName).IsRequired();
            e.Property(a => a.ContentType).IsRequired();
            e.Property(a => a.SizeBytes).IsRequired();
            e.Property(a => a.Sha256).IsRequired().HasMaxLength(64);
            e.Property(a => a.ExtractionStatus).IsRequired().HasMaxLength(20);
            e.Property(a => a.ExtractionError).HasMaxLength(2000);
            e.Property(a => a.ExtractedText).HasColumnType("text");
            e.Property(a => a.ExtractedSegmentsJson).HasColumnType("text");
            e.Property(a => a.ExtractionTruncated).HasDefaultValue(false);
            e.Property(a => a.ExtractedCharacters).HasDefaultValue(0);
            e.Property(a => a.ExtractionCharacterLimit).HasDefaultValue(50_000);
            e.Property(a => a.UploadedById).IsRequired();
            e.Property(a => a.CreatedAt).IsRequired();
            e.HasOne(a => a.Article).WithMany(ar => ar.Attachments).HasForeignKey(a => a.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.UploadedBy).WithMany().HasForeignKey(a => a.UploadedById);
        });

        // ─── LookupValues ─────────────────────────────────
        modelBuilder.Entity<LookupValue>(e =>
        {
            e.ToTable("lookup_values");
            e.HasKey(l => l.Id);
            e.Property(l => l.Category).IsRequired();
            e.Property(l => l.Value).IsRequired();
            e.Property(l => l.Label).IsRequired();
            e.Property(l => l.SortOrder).HasDefaultValue(0);
            e.Property(l => l.AuthorityWeight).HasDefaultValue(50);
            e.Property(l => l.IsActive).HasDefaultValue(true);
            e.Property(l => l.CreatedAt).IsRequired();
            e.HasIndex(l => new { l.Category, l.Value }).IsUnique();
        });

        // ─── FeaturedLinks ────────────────────────────────
        modelBuilder.Entity<FeaturedLink>(e =>
        {
            e.ToTable("featured_links");
            e.HasKey(f => f.Id);
            e.Property(f => f.Label).IsRequired();
            e.Property(f => f.LinkType).IsRequired();
            e.Property(f => f.Target).IsRequired();
            e.Property(f => f.SortOrder).HasDefaultValue(0);
            e.Property(f => f.IsActive).HasDefaultValue(true);
            e.Property(f => f.CreatedAt).IsRequired();
        });

        // ─── SearchQueries ────────────────────────────────
        modelBuilder.Entity<SearchQuery>(e =>
        {
            e.ToTable("search_queries");
            e.HasKey(s => s.Id);
            e.Property(s => s.Query).IsRequired();
            e.Property(s => s.ResultsCount).HasDefaultValue(0);
            e.Property(s => s.SearchType).IsRequired().HasDefaultValue("fulltext");
            e.Property(s => s.RagTraceId).HasMaxLength(64);
            e.Property(s => s.RagPromptVersion).HasMaxLength(100);
            e.Property(s => s.RagIndexProfile).HasMaxLength(64);
            e.Property(s => s.RagGroundingStatus).HasMaxLength(40);
            e.Property(s => s.RagAnswerHash).HasMaxLength(64);
            e.Property(s => s.RagFeedback).HasMaxLength(20);
            e.Property(s => s.RagFeedbackReason).HasMaxLength(40);
            e.Property(s => s.CreatedAt).IsRequired();
            e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId);
            e.HasOne(s => s.ClickedArticle).WithMany().HasForeignKey(s => s.ClickedArticleId).OnDelete(DeleteBehavior.SetNull);
        });

        // ─── ArticleEmbeddings ────────────────────────────
        // The non-relational InMemory provider (used by Docker-free RAG unit tests)
        // can't map the pgvector Vector type; ignore the column there. Vector queries
        // always run through VectorSearchService (Npgsql only), never InMemory.
        var isInMemory = Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory";
        modelBuilder.Entity<ArticleEmbedding>(e =>
        {
            e.ToTable("article_embeddings");
            e.HasKey(ae => ae.Id);
            e.Property(ae => ae.ArticleId).IsRequired();
            e.Property(ae => ae.ChunkIndex).IsRequired().HasDefaultValue(0);
            e.Property(ae => ae.SourceType).IsRequired().HasMaxLength(20);
            e.Property(ae => ae.SourceName).HasMaxLength(500);
            e.Property(ae => ae.SourceLocation).HasMaxLength(200);
            if (isInMemory)
                e.Ignore(ae => ae.Embedding);
            else
                e.Property(ae => ae.Embedding).IsRequired().HasColumnType("vector(1024)");
            e.Property(ae => ae.ModelName).IsRequired();
            e.Property(ae => ae.TextHash).IsRequired();
            e.Property(ae => ae.Content).HasColumnType("text");
            e.Property(ae => ae.Dimensions).IsRequired();
            e.Property(ae => ae.CreatedAt).IsRequired();
            e.HasIndex(ae => new { ae.ArticleId, ae.ChunkIndex }).IsUnique();
            e.HasOne(ae => ae.Article).WithMany(a => a.ArticleEmbeddings).HasForeignKey(ae => ae.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ae => ae.Attachment).WithMany().HasForeignKey(ae => ae.AttachmentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UsageEvent>(e =>
        {
            e.ToTable("usage_events");
            e.HasKey(u => u.Id);
            e.Property(u => u.OccurredAt).IsRequired();
            e.Property(u => u.AuthSource).IsRequired().HasMaxLength(20);
            e.Property(u => u.Channel).IsRequired().HasMaxLength(20);
            e.Property(u => u.Operation).IsRequired().HasMaxLength(200);
            e.Property(u => u.HttpMethod).IsRequired().HasMaxLength(10);
            e.Property(u => u.Outcome).IsRequired().HasMaxLength(20);
            e.HasIndex(u => u.OccurredAt);
            e.HasIndex(u => new { u.UserId, u.OccurredAt });
            e.HasIndex(u => new { u.ApiKeyId, u.OccurredAt });
            e.HasIndex(u => new { u.Operation, u.OccurredAt });
            e.HasOne(u => u.User).WithMany().HasForeignKey(u => u.UserId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(u => u.ApiKey).WithMany().HasForeignKey(u => u.ApiKeyId).OnDelete(DeleteBehavior.SetNull);
        });

        // ─── Durable search-index queue ───────────────────
        modelBuilder.Entity<IndexJob>(e =>
        {
            e.ToTable("index_jobs");
            e.HasKey(j => j.ArticleId); // coalesces repeated edits into one job
            e.Property(j => j.Status).IsRequired().HasMaxLength(20);
            e.Property(j => j.Generation).IsRequired();
            e.Property(j => j.Priority).IsRequired();
            e.Property(j => j.AttemptCount).IsRequired();
            e.Property(j => j.AvailableAt).IsRequired();
            e.Property(j => j.LockedBy).HasMaxLength(100);
            e.Property(j => j.LastError).HasMaxLength(4000);
            e.Property(j => j.CreatedAt).IsRequired();
            e.Property(j => j.UpdatedAt).IsRequired();
            e.HasIndex(j => new { j.Status, j.AvailableAt, j.Priority });
            e.HasOne(j => j.Article).WithOne().HasForeignKey<IndexJob>(j => j.ArticleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RagEvaluationDataset>(e =>
        {
            e.ToTable("rag_evaluation_datasets");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Version).IsRequired().HasMaxLength(30);
            e.Property(x => x.CasesJson).IsRequired().HasColumnType("jsonb");
            e.Property(x => x.ThresholdsJson).IsRequired().HasColumnType("jsonb");
            e.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<RagEvaluationRun>(e =>
        {
            e.ToTable("rag_evaluation_runs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).IsRequired().HasMaxLength(20);
            e.Property(x => x.WorkerId).HasMaxLength(100);
            e.Property(x => x.DatasetVersion).IsRequired().HasMaxLength(30);
            e.Property(x => x.CasesSnapshotJson).IsRequired().HasColumnType("jsonb");
            e.Property(x => x.ThresholdsSnapshotJson).IsRequired().HasColumnType("jsonb");
            e.Property(x => x.RuntimeSnapshotJson).IsRequired().HasColumnType("jsonb");
            e.Property(x => x.MetricsJson).HasColumnType("jsonb");
            e.Property(x => x.ResultsJson).HasColumnType("jsonb");
            e.Property(x => x.Error).HasMaxLength(4000);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasOne(x => x.Dataset).WithMany(x => x.Runs).HasForeignKey(x => x.DatasetId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.RequestedBy).WithMany().HasForeignKey(x => x.RequestedById).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
