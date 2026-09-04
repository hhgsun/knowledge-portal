using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using KnowledgePortal.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace KnowledgePortal.Api.Tests.Unit;

public class RagQueryUnderstandingTests
{
    [Fact]
    public void AgenticPlanner_Parse_RejectsScopeAndCommandSyntax()
    {
        var plan = AgenticRetrievalPlanner.Parse("""{"queries":["API key permissions","#secret roles","SELECT * FROM users","https://example.test"]}""",
            "API key ve rol karşılaştırması", 3);

        Assert.Equal(["API key ve rol karşılaştırması", "API key permissions"], plan);
    }

    [Fact]
    public void AgenticPlanner_Parse_HandlesMarkdownCodeFences()
    {
        var raw = """
            ```json
            {
              "queries": ["VPN kurulumu", "OpenVPN ayarları"]
            }
            ```
            """;
        var plan = AgenticRetrievalPlanner.Parse(raw, "VPN nasıl kurulur?", 3);
        Assert.Equal(["VPN nasıl kurulur?", "VPN kurulumu", "OpenVPN ayarları"], plan);
    }

    [Fact]
    public void AgenticPlanner_Parse_AllowsTechnicalTermsWithSelect()
    {
        var raw = """{"queries":["css selector rehberi"]}""";
        var plan = AgenticRetrievalPlanner.Parse(raw, "Frontend stilleri", 3);
        Assert.Equal(["Frontend stilleri", "css selector rehberi"], plan);
    }

    [Fact]
    public void AgenticPlanner_ParseResearchPlan_ValidatesTaskAndScopeCandidates()
    {
        var plan = AgenticRetrievalPlanner.ParseResearchPlan("""
            {"task":"compare","presentation":"comparison_table","queries":["API key yetkileri","roller"],"scopeCandidates":["Finagoport","#yasak","https://example.test"],"requiresComprehensiveResearch":true}
            """, "API key ile rolleri kıyasla", 4);

        Assert.NotNull(plan);
        Assert.Equal("compare", plan.Task);
        Assert.Equal("comparison_table", plan.Presentation);
        Assert.True(plan.RequiresComprehensiveResearch);
        Assert.Equal(["API key ile rolleri kıyasla", "API key yetkileri", "roller"], plan.Queries);
        Assert.Equal(["Finagoport"], plan.ScopeCandidates);
    }

    [Fact]
    public void AgenticPlanner_ParseResearchPlan_RejectsUnknownTask()
    {
        var plan = AgenticRetrievalPlanner.ParseResearchPlan("""
            {"task":"browse_database","presentation":"auto","queries":["VPN"],"scopeCandidates":[],"requiresComprehensiveResearch":false}
            """, "VPN nedir?", 3);

        Assert.Null(plan);
    }

    [Fact]
    public async Task Understand_ExpandsAcronymExtractsFiltersAndDecomposesCompoundQuestion()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new AppDbContext(options);
        db.Users.Add(new User { Id = "u1", Name = "Ayşe", Email = "ayse@example.test", Slug = "ayse", PasswordHash = "hash" });
        await db.SaveChangesAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ollama:QueryUnderstanding:Synonyms:vpn:0"] = "virtual private network"
        }).Build();
        var service = new RagQueryUnderstandingService(config);

        var plan = await service.UnderstandAsync(db,
            "VPN kurulumu ve sertifika yenileme @ayse #network",
            new ArticleFilter(ContentTypes: ["how-to"]));

        Assert.Contains("virtual private network", plan.RewrittenQuery);
        Assert.True(plan.IsComplex);
        Assert.True(plan.Queries.Count >= 2);
        Assert.Equal(["u1"], plan.EffectiveFilter!.OwnerIds);
        Assert.Contains("network", plan.EffectiveFilter.TagSlugs!);
        Assert.Contains("how-to", plan.EffectiveFilter.ContentTypes!);
    }

    [Fact]
    public async Task Understand_DetectsFoldedTurkishComplexityAndFreshnessSignals()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var db = new AppDbContext(options);
        var service = new RagQueryUnderstandingService(new ConfigurationBuilder().Build());

        var plan = await service.UnderstandAsync(db, "Güncel erişim politikalarını karşılaştır");

        Assert.True(plan.PrefersFreshSources);
        Assert.True(plan.IsComplex);
    }
}
