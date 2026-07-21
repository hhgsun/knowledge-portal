using KnowledgePortal.Api.Helpers;

namespace KnowledgePortal.Api.Tests.Unit;

public class RrfHelperTests
{
    [Fact]
    public void Merge_FulltextOnly_UsesFulltextAlphaAndMatchType()
    {
        var scores = RrfHelper.Merge(["a", "b"], null);

        Assert.Equal(2, scores.Count);
        Assert.Equal(0.4 / 61, scores["a"].Score, precision: 10);
        Assert.Equal(0.4 / 62, scores["b"].Score, precision: 10);
        Assert.All(scores.Values, v => Assert.Equal("fulltext", v.MatchType));
    }

    [Fact]
    public void Merge_SemanticOnly_UsesSemanticAlphaAndMatchType()
    {
        var scores = RrfHelper.Merge([], ["x"]);

        Assert.Equal(0.6 / 61, scores["x"].Score, precision: 10);
        Assert.Equal("semantic", scores["x"].MatchType);
    }

    [Fact]
    public void Merge_OverlappingItem_SumsScoresAndMarksBoth()
    {
        var scores = RrfHelper.Merge(["a", "shared"], ["shared", "b"]);

        Assert.Equal(3, scores.Count);
        Assert.Equal("both", scores["shared"].MatchType);
        Assert.Equal(0.4 / 62 + 0.6 / 61, scores["shared"].Score, precision: 10);
        Assert.Equal("fulltext", scores["a"].MatchType);
        Assert.Equal("semantic", scores["b"].MatchType);
    }

    [Fact]
    public void Merge_RankOrderDeterminesScoreOrder()
    {
        var scores = RrfHelper.Merge(["first", "second", "third"], null);

        Assert.True(scores["first"].Score > scores["second"].Score);
        Assert.True(scores["second"].Score > scores["third"].Score);
    }
}
