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
}
