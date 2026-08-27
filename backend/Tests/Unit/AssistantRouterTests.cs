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
                {"route":"analytics","confidence":0.91,"normalizedQuery":"portal trendleri","reasonCode":"usage_trend","includeSearchResults":false}
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
                {"route":"analytics","confidence":0.4,"normalizedQuery":"belirsiz istek","reasonCode":"ambiguous","includeSearchResults":false}
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

    private static AssistantRouterService CreateRouter(FakeChatClient fake,
        bool classifierEnabled = true)
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
        var provider = collection.BuildServiceProvider();
        var metrics = new PortalMetrics(provider.GetRequiredService<IServiceScopeFactory>(), config);
        return new(config, provider, metrics, NullLogger<AssistantRouterService>.Instance);
    }
}
