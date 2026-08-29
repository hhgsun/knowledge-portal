using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using KnowledgePortal.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Tests.Unit;

public sealed class ClassificationServiceTests
{
    [Fact]
    public async Task ResolveAndApply_SupportsCanonicalMultipleValues()
    {
        await using var db = CreateDb();
        db.LookupCategories.AddRange(
            new LookupCategory { Id = "content-category", Key = "content_type", Label = "Content Type", IsRequired = true },
            new LookupCategory { Id = "department-category", Key = "department", Label = "Department", Cardinality = "multiple" });
        db.LookupValues.AddRange(
            new LookupValue { Id = "reference-value", Category = "content_type", Value = "reference", Label = "Reference" },
            new LookupValue { Id = "hr-value", Category = "department", Value = "human-resources", Label = "Human Resources" },
            new LookupValue { Id = "it-value", Category = "department", Value = "it", Label = "IT" });
        await db.SaveChangesAsync();
        var service = new ClassificationService(db);

        var result = await service.ResolveAsync(null,
            new Dictionary<string, string[]> { ["department"] = ["human-resources", "it"] }, true);

        Assert.Null(result.Error);
        Assert.Equal("reference", result.Resolution!.ContentType);
        Assert.Equal(["human-resources", "it"],
            result.Resolution.Values["department"].Select(value => value.Value));

        db.Articles.Add(new Article
        {
            Id = "article", Title = "Classification", Slug = "classification",
            OwnerId = "owner", ContentType = result.Resolution.ContentType
        });
        await service.ApplyAsync("article", result.Resolution);
        await db.SaveChangesAsync();

        var assignments = await service.GetAssignmentsAsync(["article"]);
        Assert.Equal(["human-resources", "it"], assignments["article"]["department"]);
        Assert.Equal(["reference"], assignments["article"]["content_type"]);
    }

    [Fact]
    public async Task Resolve_RejectsMultipleValuesForSingleCategory()
    {
        await using var db = CreateDb();
        db.LookupCategories.Add(new LookupCategory
            { Id = "department-category", Key = "department", Label = "Department", Cardinality = "single" });
        db.LookupValues.AddRange(
            new LookupValue { Id = "hr-value", Category = "department", Value = "hr", Label = "HR" },
            new LookupValue { Id = "it-value", Category = "department", Value = "it", Label = "IT" });
        await db.SaveChangesAsync();

        var result = await new ClassificationService(db).ResolveAsync(null,
            new Dictionary<string, string[]> { ["department"] = ["hr", "it"] }, false);

        Assert.NotNull(result.Error);
        Assert.Contains("single value", result.Error.Message);
    }

    [Fact]
    public async Task Resolve_RejectsNonCanonicalValue()
    {
        await using var db = CreateDb();
        db.LookupCategories.Add(new LookupCategory
            { Id = "department-category", Key = "department", Label = "Department" });
        db.LookupValues.Add(new LookupValue
            { Id = "hr-value", Category = "department", Value = "human-resources", Label = "Human Resources" });
        await db.SaveChangesAsync();

        var result = await new ClassificationService(db).ResolveAsync(null,
            new Dictionary<string, string[]> { ["department"] = ["ik"] }, false);

        Assert.NotNull(result.Error);
        Assert.Contains("Unknown or inactive value 'ik'", result.Error.Message);
    }

    [Fact]
    public async Task ArticleFilter_CombinesCategoriesWithAndAndValuesWithOr()
    {
        await using var db = CreateDb();
        db.LookupCategories.AddRange(
            new LookupCategory { Id = "department-category", Key = "department", Label = "Department" },
            new LookupCategory { Id = "environment-category", Key = "environment", Label = "Environment" });
        db.LookupValues.AddRange(
            new LookupValue { Id = "it-value", Category = "department", Value = "it", Label = "IT" },
            new LookupValue { Id = "finance-value", Category = "department", Value = "finance", Label = "Finance" },
            new LookupValue { Id = "prod-value", Category = "environment", Value = "production", Label = "Production" });
        db.Articles.AddRange(Article("matching"), Article("wrong-environment"));
        db.ArticleLookupValues.AddRange(
            new ArticleLookupValue { ArticleId = "matching", LookupValueId = "it-value" },
            new ArticleLookupValue { ArticleId = "matching", LookupValueId = "prod-value" },
            new ArticleLookupValue { ArticleId = "wrong-environment", LookupValueId = "finance-value" });
        await db.SaveChangesAsync();

        var filter = new ArticleFilter(Facets: new Dictionary<string, string[]>
        {
            ["department"] = ["it", "finance"],
            ["environment"] = ["production"]
        });
        var ids = await ArticleService.ApplyFilter(db.Articles, filter).Select(article => article.Id).ToListAsync();

        Assert.Equal(["matching"], ids);
    }

    private static AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static Article Article(string id) => new()
    {
        Id = id, Title = id, Slug = id, OwnerId = "owner", ContentType = "reference"
    };
}
