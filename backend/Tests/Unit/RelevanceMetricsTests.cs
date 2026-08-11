using KnowledgePortal.Api.Helpers;

namespace KnowledgePortal.Api.Tests.Unit;

public class RelevanceMetricsTests
{
    [Fact]
    public void Calculate_ProducesRecallMrrAndNdcg()
    {
        var metrics = RelevanceMetrics.Calculate(["noise", "best", "good"],
            new Dictionary<string, int> { ["best"] = 3, ["good"] = 1, ["missing"] = 1 }, 3);

        Assert.Equal(2d / 3, metrics.RecallAtK, 10);
        Assert.Equal(0.5, metrics.Mrr, 10);
        Assert.InRange(metrics.NdcgAtK, 0, 1);
    }
}
