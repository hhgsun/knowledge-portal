using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using KnowledgePortal.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace KnowledgePortal.Api.Tests.Unit;

public class IndexJobQueueTests
{
    [Fact]
    public async Task Enqueue_CoalescesAndIncrementsGeneration()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var article = new Article { Title = "A", Slug = "a", OwnerId = "owner" };
        db.Articles.Add(article);
        await db.SaveChangesAsync();
        var queue = new IndexJobQueue(db, new ConfigurationBuilder().Build());

        await queue.EnqueueAsync(article.Id, 10);
        await queue.EnqueueAsync(article.Id, 100);

        var job = await db.IndexJobs.SingleAsync();
        Assert.Equal("pending", job.Status);
        Assert.Equal(2, job.Generation);
        Assert.Equal(100, job.Priority);
    }

    [Fact]
    public async Task ReconcileDirtyArticles_RepairsOnlyMissingAndCompletedJobs()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var articles = new[]
        {
            DirtyArticle("missing"),
            DirtyArticle("completed"),
            DirtyArticle("pending"),
            DirtyArticle("processing"),
            DirtyArticle("failed"),
            new Article
            {
                Id = "clean", Title = "clean", Slug = "clean", OwnerId = "owner", Status = "published",
                FtsIndexedAt = DateTime.UtcNow, IndexedAt = DateTime.UtcNow,
            },
        };
        db.Articles.AddRange(articles);
        db.IndexJobs.AddRange(
            Job("completed", "completed", generation: 3),
            Job("pending", "pending"),
            Job("processing", "processing"),
            Job("failed", "failed"),
            Job("clean", "completed"));
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ollama:Enabled"] = "true" })
            .Build();
        var reconciled = await new IndexJobQueue(db, config).ReconcileDirtyArticlesAsync(default);

        Assert.Equal(2, reconciled);
        Assert.Equal("pending", (await db.IndexJobs.FindAsync("missing"))!.Status);
        var completed = (await db.IndexJobs.FindAsync("completed"))!;
        Assert.Equal("pending", completed.Status);
        Assert.Equal(4, completed.Generation);
        Assert.Equal("pending", (await db.IndexJobs.FindAsync("pending"))!.Status);
        Assert.Equal("processing", (await db.IndexJobs.FindAsync("processing"))!.Status);
        Assert.Equal("failed", (await db.IndexJobs.FindAsync("failed"))!.Status);
        Assert.Equal("completed", (await db.IndexJobs.FindAsync("clean"))!.Status);

        static Article DirtyArticle(string id) => new()
        {
            Id = id, Title = id, Slug = id, OwnerId = "owner", Status = "published",
        };

        static IndexJob Job(string articleId, string status, int generation = 1) => new()
        {
            ArticleId = articleId,
            Status = status,
            Generation = generation,
        };
    }
}
