using KnowledgePortal.Api.Services;
using KnowledgePortal.Api.Tests.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnowledgePortal.Api.Tests.Unit;

public class AssistantQueryContextualizerTests
{
    [Fact]
    public async Task ContextualizeAsync_ProducesStandaloneQueryAndHydeWhilePreservingScope()
    {
        var chat = new FakeChatClient
        {
            ResponseOverride = """
                {"standaloneQuery":"VPN politikasının istisnaları nelerdir?","hypotheticalDocument":"VPN politikası istisnaları, kapsam dışı durumlar ve onay koşulları açıklanır."}
                """
        };
        var service = Create(chat);

        var result = await service.ContextualizeAsync("Peki bunun istisnası var mı? #network",
        [
            new("user", "VPN politikası nedir?"),
            new("assistant", "VPN politikası uzaktan erişimi düzenler.")
        ]);

        Assert.Equal("llm_hyde", result.Strategy);
        Assert.Contains("VPN politikasının istisnaları", result.StandaloneQuery);
        Assert.Contains("#network", result.StandaloneQuery);
        Assert.Contains("kapsam dışı", result.HypotheticalDocument);
        Assert.NotNull(chat.LastOptions?.ResponseFormat);
    }

    [Fact]
    public async Task ContextualizeAsync_FailsOpenToDeterministicStandaloneRewrite()
    {
        var chat = new FakeChatClient { ResponseOverride = "not-json" };
        var service = Create(chat);

        var result = await service.ContextualizeAsync("Peki bunun istisnası var mı?",
            [new("user", "VPN politikası nedir?")]);

        Assert.Equal("deterministic_fallback", result.Strategy);
        Assert.Equal("VPN politikası hakkında: Peki bunun istisnası var mı?", result.StandaloneQuery);
        Assert.Null(result.HypotheticalDocument);
    }

    [Fact]
    public async Task ContextualizeAsync_DoesNotCallModelForIndependentQuestion()
    {
        var chat = new FakeChatClient();
        var service = Create(chat);

        var result = await service.ContextualizeAsync(
            "MFA nedir?",
            [new("user", "Başka bir konu")]);

        Assert.Equal("none", result.Strategy);
        Assert.Equal(0, chat.CallCount);
    }

    [Fact]
    public async Task ContextualizeAsync_RecognizesSubjectlessHowToAndPreservesPreviousTopic()
    {
        var chat = new FakeChatClient
        {
            ResponseOverride = """
                {"standaloneQuery":"Arama nasıl kullanılır?","hypotheticalDocument":"Arama özelliğini kullanma adımları açıklanır."}
                """
        };
        var service = Create(chat);

        var result = await service.ContextualizeAsync("nasıl kullanılır?",
        [
            new("user", "MCP nedir?"),
            new("assistant", "MCP, istemcilerin araç çağırmasını sağlayan bir protokoldür.")
        ]);

        Assert.Equal("deterministic_topic_guard", result.Strategy);
        Assert.Equal("MCP hakkında: nasıl kullanılır?", result.StandaloneQuery);
        Assert.Null(result.HypotheticalDocument);
        Assert.Equal(1, chat.CallCount);
    }

    [Theory]
    [InlineData("nasıl kullanılır?")]
    [InlineData("nasıl çalışır?")]
    [InlineData("örnek ver")]
    [InlineData("avantajları nelerdir?")]
    public void LooksLikeFollowUp_RecognizesEllipticalQuestions(string message)
        => Assert.True(AssistantQueryContextualizer.LooksLikeFollowUp(message));

    [Fact]
    public void LooksLikeFollowUp_DoesNotTreatExplicitHowToAsElliptical()
        => Assert.False(AssistantQueryContextualizer.LooksLikeFollowUp("MCP nasıl kullanılır?"));

    private static AssistantQueryContextualizer Create(FakeChatClient chat)
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection().BuildServiceProvider();
        var metrics = new PortalMetrics(services.GetRequiredService<IServiceScopeFactory>(), config);
        return new(chat, config, metrics, NullLogger<AssistantQueryContextualizer>.Instance);
    }
}
