using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Services;

namespace KnowledgePortal.Api.Tests.Unit;

public class AssistantTurnPlanningTests
{
    [Theory]
    [InlineData("sırala", "ordered_list")]
    [InlineData("maddele", "bullet_list")]
    [InlineData("tablo yap", "comparison_table")]
    [InlineData("iki cümlede özetle", "summary")]
    [InlineData("akış şeması yap", "process_flow")]
    [InlineData("infografik yap", "infographic")]
    public void PresentationOnlyCommandsAreRecognized(string message, string expected)
    {
        Assert.True(AssistantTurnPlanningService.IsPresentationOnly(message));
        Assert.Equal(expected, AssistantTurnPlanningService.DetectPresentation(message));
    }

    [Fact]
    public void PresentationServiceBuildsSourceBoundTableWithoutInventingFacts()
    {
        var rag = new AssistantRagDto([], [],
            [new("MCP standart bir entegrasyon protokolüdür.", "summary", ["S1"])],
            [], 1, 1, "lexically_grounded", false, false);

        var result = new AssistantPresentationService().Present("ignored", rag,
            AssistantPresentationModes.Table);

        Assert.Equal("table", Assert.Single(result.Blocks).Type);
        Assert.Contains("MCP standart bir entegrasyon protokolüdür.", result.Answer);
        Assert.Contains("[S1]", result.Answer);
    }

    [Fact]
    public void PresentationServiceOrdersOnlyVerifiedClaims()
    {
        var rag = new AssistantRagDto([], [],
        [
            new("Birinci bilgi.", "summary", ["S1"]),
            new("İkinci bilgi.", "explanation", ["S2"])
        ], [], 1, 1, "lexically_grounded", false, false);

        var result = new AssistantPresentationService().Present("ignored", rag,
            AssistantPresentationModes.OrderedList);

        Assert.Equal("1. Birinci bilgi. [S1]\n2. İkinci bilgi. [S2]", result.Answer);
        Assert.Equal(2, Assert.Single(result.Blocks).Items!.Length);
    }

    [Fact]
    public void EvaluationDatasetAcceptsMultiTurnExpectations()
    {
        const string json = """
            [{
              "id":"conversation-1","category":"conversation","question":"MCP follow-up",
              "expectedSourceSlugs":[],"expectedFacts":[],"forbiddenFacts":[],"expectedRefusal":false,
              "turns":[
                {"message":"MCP nedir?","expectedIntent":"explain","expectedRetrieval":true},
                {"message":"sırala","expectedIntent":"list","expectedPresentation":"ordered_list","expectedRetrieval":false}
              ]
            }]
            """;

        var item = Assert.Single(RagEvaluationService.ParseCases(json));

        Assert.Equal(2, item.Turns!.Count);
        Assert.False(item.Turns[1].ExpectedRetrieval);
        Assert.Equal("ordered_list", item.Turns[1].ExpectedPresentation);
    }

    [Fact]
    public void EvaluationFailsWhenConversationTaskOrRetrievalDecisionRegresses()
    {
        var item = new RagEvaluationCaseResult("conversation-1", "conversation",
            1, 1, 1, 1, 1, 1, true, true, 10, [], [], "answer",
            ConversationTaskAccuracy: .5, RetrievalDecisionAccuracy: .5);

        var metrics = RagEvaluationService.Aggregate([item], new());

        Assert.False(metrics.Passed);
        Assert.Contains(metrics.FailedGates, x => x.StartsWith("Conversation task accuracy"));
        Assert.Contains(metrics.FailedGates, x => x.StartsWith("Retrieval decision accuracy"));
    }
}
