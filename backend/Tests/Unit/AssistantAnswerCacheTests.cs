using System.Security.Claims;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
using KnowledgePortal.Api.Services;
using KnowledgePortal.Api.Tests.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgePortal.Api.Tests.Unit;

public sealed class AssistantAnswerCacheTests
{
    [Fact]
    public async Task Cache_IsSemanticUserScopedAndInvalidatedByCorpusVersion()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(x => x.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new FakeEmbeddingGenerator());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User { Id = "cache-user", Name = "Cache User", Slug = "cache-user", Email = "cache@example.com",
            PasswordHash = "x", Role = "viewer" };
        var article = new Article { Id = "cache-article", Title = "VPN Cache", Slug = "vpn-cache",
            Content = "VPN sertifika yenileme adımları", Status = "published", ContentType = "how-to",
            OwnerId = user.Id, Owner = user };
        db.AddRange(user, article); await db.SaveChangesAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Assistant:SemanticCache:Enabled"] = "true",
            ["Assistant:SemanticCache:SimilarityThreshold"] = ".8",
            ["Assistant:SemanticCache:MinimumCitationCoverage"] = ".9",
            ["Assistant:SemanticCache:TtlMinutes"] = "60",
            ["Ollama:ChatModel"] = "test-chat", ["Ollama:EmbeddingModel"] = "test-embed"
        }).Build();
        var metrics = new PortalMetrics(provider.GetRequiredService<IServiceScopeFactory>(), config);
        var cache = new AssistantAnswerCacheService(db, scope.ServiceProvider, config, metrics);
        var principal = Principal(user.Id);
        var rag = new AssistantRagDto([], [], [], [], 1, 1, "grounded", false, false);
        await cache.StoreAsync("VPN sertifika yenileme", principal,
            new("Sertifika yenileme adımları doğrulanmıştır. [S1]", rag), CancellationToken.None);

        var similar = await cache.TryGetAsync("VPN sertifika yenileme", principal, CancellationToken.None);
        var otherUser = await cache.TryGetAsync("VPN sertifika yenileme", Principal("other"), CancellationToken.None);
        article.UpdatedAt = article.UpdatedAt.AddMinutes(1); await db.SaveChangesAsync();
        var stale = await cache.TryGetAsync("VPN sertifika yenileme", principal, CancellationToken.None);

        Assert.NotNull(similar);
        Assert.Null(otherUser);
        Assert.Null(stale);
    }

    private static ClaimsPrincipal Principal(string id) => new(new ClaimsIdentity([
        new Claim("id", id), new Claim("role", "viewer"), new Claim("source", "session")], "test"));
}
