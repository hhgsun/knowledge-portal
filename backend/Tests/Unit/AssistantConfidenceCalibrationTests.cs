using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using KnowledgePortal.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace KnowledgePortal.Api.Tests.Unit;

public sealed class AssistantConfidenceCalibrationTests
{
    [Fact]
    public async Task Calibrate_UsesOnlyRouteCorrectnessFeedbackAndPreservesSampleCount()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        for (var i = 0; i < 10; i++)
            db.AssistantInteractions.Add(new AssistantInteraction
            {
                QueryFingerprint = Guid.NewGuid().ToString("N").PadRight(64, '0')[..64],
                Route = "knowledge_answer", RouteSource = "classifier", ReasonCode = "test",
                Helpful = i < 2, FeedbackReason = i < 2 ? null : "wrong_route",
                FeedbackAt = DateTime.UtcNow, ToolCallsJson = "[]",
                RoutingPromptVersion = "test", ClassifierModel = "test",
                RoutingConfigSnapshotJson = "{}", ApplicationVersion = "test"
            });
        await db.SaveChangesAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AgenticRouting:Calibration:Enabled"] = "true",
            ["AgenticRouting:Calibration:MinimumSamples"] = "3"
        }).Build();

        var result = await new AssistantConfidenceCalibrationService(db, config)
            .CalibrateAsync("knowledge_answer", .9, CancellationToken.None);

        Assert.Equal(10, result.Samples);
        Assert.True(result.Confidence < .9);
    }
}
