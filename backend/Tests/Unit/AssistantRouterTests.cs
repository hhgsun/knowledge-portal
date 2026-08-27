using KnowledgePortal.Api.Services;
using KnowledgePortal.Api.Tests.Integration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgePortal.Api.Tests.Unit;

public sealed class AssistantRouterTests
{
    [Fact]
    public async Task AmbiguousRequest_UsesStructuredClassifierDecision()
    {
        var fake = new FakeChatClient
        {
            ResponseOverride = """
                {"route":"analytics","confidence":0.91,"reasonCode":"usage_trend","includeSearchResults":false}
                """
        };
        var router = CreateRouter(fake);

        var decision = await router.RouteAsync("portal trendleri", "auto");

        Assert.Equal(AssistantRoute.Analytics, decision.Route);
        Assert.Equal("classifier", decision.Source);
        Assert.IsType<ChatResponseFormatJson>(fake.LastOptions?.ResponseFormat);
    }

    [Fact]
    public async Task LowConfidenceClassifier_UsesReadOnlySearchFallback()
    {
        var fake = new FakeChatClient
        {
            ResponseOverride = """
                {"route":"analytics","confidence":0.4,"reasonCode":"ambiguous","includeSearchResults":false}
                """
        };
        var router = CreateRouter(fake);

        var decision = await router.RouteAsync("belirsiz istek", "auto");

        Assert.Equal(AssistantRoute.KnowledgeSearch, decision.Route);
        Assert.Equal("fallback", decision.Source);
        Assert.Equal("low_confidence_safe_fallback", decision.ReasonCode);
    }

    [Fact]
    public async Task ExplicitMode_BypassesClassifier()
    {
        var fake = new FakeChatClient();
        var router = CreateRouter(fake);

        var decision = await router.RouteAsync("herhangi bir istek", "search");

        Assert.Equal(AssistantRoute.KnowledgeSearch, decision.Route);
        Assert.Equal("manual", decision.Source);
        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task ClassifierUnavailable_UsesReadOnlySearchFallback()
    {
        var fake = new FakeChatClient();
        var router = CreateRouter(fake, classifierEnabled: false);

        var decision = await router.RouteAsync("belirsiz kurumsal istek", "auto");

        Assert.Equal(AssistantRoute.KnowledgeSearch, decision.Route);
        Assert.Equal("fallback", decision.Source);
        Assert.Equal("classifier_unavailable_safe_fallback", decision.ReasonCode);
        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task Classifier_CannotRewriteTheSearchQuery()
    {
        var fake = new FakeChatClient
        {
            ResponseOverride = """
                {"route":"knowledge_search","confidence":0.92,"reasonCode":"document_lookup","includeSearchResults":false,"normalizedQuery":"model attempted rewrite"}
                """
        };
        var router = CreateRouter(fake);

        var decision = await router.RouteAsync("Özgün ve belirsiz kurumsal terim", "auto");

        Assert.Equal("Özgün ve belirsiz kurumsal terim", decision.NormalizedQuery);
    }

    [Fact]
    public async Task RepeatedAmbiguousQuery_UsesPrivacySafeClassifierCache()
    {
        var fake = new FakeChatClient
        {
            ResponseOverride = """
                {"route":"knowledge_search","confidence":0.92,"reasonCode":"document_lookup","includeSearchResults":false}
                """
        };
        var router = CreateRouter(fake);

        await router.RouteAsync("benzersiz belirsiz terim", "auto");
        var cached = await router.RouteAsync("benzersiz belirsiz terim", "auto");

        Assert.Equal("classifier_cache", cached.Source);
        Assert.Equal(1, fake.CallCount);
    }

    [Fact]
    public async Task AmbiguousRequest_PrefersDedicatedRoutingModelClient()
    {
        var main = new FakeChatClient { ResponseOverride = """
            {"route":"knowledge_search","confidence":0.9,"reasonCode":"main","includeSearchResults":false}
            """ };
        var routing = new FakeChatClient { ResponseOverride = """
            {"route":"analytics","confidence":0.9,"reasonCode":"small_router","includeSearchResults":false}
            """ };
        var router = CreateRouter(main, routingFake: routing);

        var decision = await router.RouteAsync("kurumsal eğilim görünümü", "auto");

        Assert.Equal(AssistantRoute.Analytics, decision.Route);
        Assert.Equal(1, routing.CallCount);
        Assert.Equal(0, main.CallCount);
    }

    private static AssistantRouterService CreateRouter(FakeChatClient fake,
        bool classifierEnabled = true, FakeChatClient? routingFake = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AgenticRouting:Enabled"] = "true",
            ["AgenticRouting:ClassifierEnabled"] = classifierEnabled.ToString(),
            ["AgenticRouting:MinConfidence"] = "0.78"
        }).Build();
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddSingleton<IChatClient>(fake);
        if (routingFake != null)
            collection.AddKeyedSingleton<IChatClient>("assistant-router", routingFake);
        var provider = collection.BuildServiceProvider();
        var metrics = new PortalMetrics(provider.GetRequiredService<IServiceScopeFactory>(), config);
        var resilience = new AssistantClassifierResilienceService(config, metrics,
            NullLogger<AssistantClassifierResilienceService>.Instance);
        return new(config, provider, resilience, metrics, NullLogger<AssistantRouterService>.Instance);
    }
}
