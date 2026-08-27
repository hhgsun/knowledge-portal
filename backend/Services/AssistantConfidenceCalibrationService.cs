using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public sealed record AssistantCalibrationResult(double Confidence, int Samples);

public sealed class AssistantConfidenceCalibrationService(AppDbContext db, IConfiguration config)
{
    public async Task<AssistantCalibrationResult> CalibrateAsync(string route, double raw,
        CancellationToken ct)
    {
        if (!config.GetValue("AgenticRouting:Calibration:Enabled", true))
            return new(raw, 0);
        var since = DateTime.UtcNow.AddDays(-Math.Clamp(
            config.GetValue("AgenticRouting:Calibration:WindowDays", 90), 7, 365));
        var signals = await db.AssistantInteractions.AsNoTracking()
            .Where(x => x.Route == route && x.FeedbackAt >= since
                && (x.Helpful == true || x.FeedbackReason == "wrong_route"))
            .Select(x => new { x.Helpful, x.FeedbackReason }).Take(500).ToListAsync(ct);
        var minimum = Math.Clamp(config.GetValue("AgenticRouting:Calibration:MinimumSamples", 10), 3, 100);
        if (signals.Count < minimum) return new(raw, signals.Count);
        var correct = signals.Count(x => x.Helpful == true && x.FeedbackReason != "wrong_route");
        var empirical = (correct + 1d) / (signals.Count + 2d); // beta(1,1) smoothing
        var weight = Math.Min(.7, signals.Count / 50d);
        return new(Math.Clamp(raw * (1 - weight) + empirical * weight, 0, 1), signals.Count);
    }
}
