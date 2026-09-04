using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using KnowledgePortal.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Tests.Unit;

public class ResearchScopeResolverTests
{
    [Fact]
    public async Task ResolveAsync_PrefersTagOverLookupForTheSameNaturalLanguageCandidate()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new AppDbContext(options);
        db.Tags.Add(new Tag { Id = "tag", Name = "Finagoport", Slug = "finagoport" });
        db.LookupCategories.Add(new LookupCategory { Id = "category", Key = "product", Label = "Product" });
        db.LookupValues.Add(new LookupValue { Id = "value", Category = "product", Value = "finagoport", Label = "Finagoport" });
        await db.SaveChangesAsync();

        var result = await new ResearchScopeResolver(db).ResolveAsync(["Finagoport", "unknown"]);

        Assert.Equal(["finagoport"], result.Tags);
        Assert.Empty(result.Facets);
        Assert.Equal(["unknown"], result.IgnoredCandidates);
    }
}