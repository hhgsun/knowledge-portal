using System.Text.Json;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Models.Entities;
using KnowledgePortal.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace KnowledgePortal.Api.Tests.Unit;

public class AttachmentIndexInclusionTests
{
    [Fact]
    public async Task AttachmentTextAggregation_OmitsExcludedAttachmentAndKeepsIncludedAttachment()
    {
        const string excluded = "# Gövdede zaten bulunan içerik";
        const string included = "# Yalnız ekte bulunan bilgi";
        var config = new ConfigurationBuilder().Build();
        var profile = AttachmentProcessingService.ComputeProfile(config);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

        db.ArticleAttachments.AddRange(
            CachedAttachment("excluded", "source.md", excluded, profile, includeInIndex: false),
            CachedAttachment("included", "notes.md", included, profile, includeInIndex: true));
        await db.SaveChangesAsync();

        var attachmentText = await AttachmentHelper.GetAttachmentTextAsync(db, config, "article-1");

        Assert.DoesNotContain("Gövdede zaten bulunan", attachmentText);
        Assert.Contains("Yalnız ekte bulunan", attachmentText);
    }

    private static ArticleAttachment CachedAttachment(string id, string fileName, string text,
        string profile, bool includeInIndex)
    {
        var segment = new AttachmentTextSegment(text, "file");
        return new ArticleAttachment
        {
            Id = id,
            ArticleId = "article-1",
            FileName = fileName,
            StoredFileName = id + ".md",
            ContentType = "text/markdown",
            SizeBytes = text.Length,
            Sha256 = "hash-" + id,
            IncludeInIndex = includeInIndex,
            UploadedById = "user-1",
            ExtractionStatus = "completed",
            ExtractedText = text,
            ExtractedSegmentsJson = JsonSerializer.Serialize(new[] { segment }),
            ExtractedCharacters = text.Length,
            ExtractionCharacterLimit = AttachmentTextExtractor.DefaultMaxCharacters,
            ExtractionProfile = profile,
            ExtractedAt = DateTime.UtcNow
        };
    }
}
