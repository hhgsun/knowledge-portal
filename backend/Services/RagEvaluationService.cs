using System.Globalization;
using System.Text;
using System.Text.Json;
using KnowledgePortal.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public record RagEvaluationFilters(List<string>? Tag = null, List<string>? AuthorIds = null, List<string>? ContentType = null);
public record RagEvaluationCase(string Id, string Category, string Question, List<string> ExpectedSourceSlugs,
    List<string> ExpectedFacts, List<string> ForbiddenFacts, bool ExpectedRefusal, RagEvaluationFilters? Filters = null);
public record RagEvaluationThresholds(double RecallAtK = .8, double Mrr = .75, double NdcgAtK = .75,
    double FactCoverage = .7, double CitationCoverage = .8, double RefusalAccuracy = .9,
    double ForbiddenFactPassRate = 1, long P95LatencyMs = 30000);
public record RagEvaluationCaseResult(string Id, string Category, double RecallAtK, double Mrr, double NdcgAtK,
    double FactCoverage, double CitationCoverage, bool RefusalCorrect, bool NoForbiddenFacts, long LatencyMs,
    List<string> RetrievedSlugs, List<string> ForbiddenFactHits, string Answer);
public record RagEvaluationMetrics(double RecallAtK, double Mrr, double NdcgAtK, double FactCoverage,
    double CitationCoverage, double RefusalAccuracy, double ForbiddenFactPassRate, long P50LatencyMs,
    long P95LatencyMs, bool Passed, List<string> FailedGates);

public class RagEvaluationService(AppDbContext db, RagService rag)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Refusals = ["yeterli bilgi bulamadım", "yeterince ilgili bir makale bulunamadı", "henüz indexlenmiş makale bulunamadı", "ai arama şu anda kullanılamıyor"];

    public static List<RagEvaluationCase> ParseCases(string json) =>
        JsonSerializer.Deserialize<List<RagEvaluationCase>>(json, Json) ?? [];
    public static RagEvaluationThresholds ParseThresholds(string json) =>
        JsonSerializer.Deserialize<RagEvaluationThresholds>(json, Json) ?? new();

    public async Task<RagEvaluationCaseResult> ExecuteCaseAsync(RagEvaluationCase item, CancellationToken ct)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var f = item.Filters;
        var filter = new ArticleFilter(f?.AuthorIds, f?.ContentType, TagSlugs: f?.Tag);
        var response = await rag.AskAsync(item.Question, filter, ct);
        watch.Stop();
        var ranked = response.Sources.Select(x => x.Slug).ToList();
        var expected = item.ExpectedSourceSlugs.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hits = ranked.Count(expected.Contains);
        var recall = expected.Count == 0 ? 1 : hits / (double)expected.Count;
        var first = ranked.FindIndex(expected.Contains);
        var mrr = expected.Count == 0 ? 1 : first < 0 ? 0 : 1d / (first + 1);
        var dcg = ranked.Select((slug, i) => expected.Contains(slug) ? 1 / Math.Log2(i + 2) : 0).Sum();
        var ideal = Enumerable.Range(0, Math.Min(expected.Count, ranked.Count)).Sum(i => 1 / Math.Log2(i + 2));
        var answer = Fold(response.Answer);
        var forbidden = item.ForbiddenFacts.Where(x => answer.Contains(Fold(x), StringComparison.Ordinal)).ToList();
        return new(item.Id, item.Category, recall, mrr, expected.Count == 0 ? 1 : ideal == 0 ? 0 : dcg / ideal,
            item.ExpectedFacts.Count == 0 ? 1 : item.ExpectedFacts.Count(x => answer.Contains(Fold(x), StringComparison.Ordinal)) / (double)item.ExpectedFacts.Count,
            response.Sources.Count == 0 ? (item.ExpectedRefusal ? 1 : 0) : response.CitationCoverage,
            Refusals.Any(x => answer.Contains(Fold(x), StringComparison.Ordinal)) == item.ExpectedRefusal,
            forbidden.Count == 0, watch.ElapsedMilliseconds, ranked, forbidden, response.Answer);
    }

    public static RagEvaluationMetrics Aggregate(List<RagEvaluationCaseResult> cases, RagEvaluationThresholds t)
    {
        double Avg(Func<RagEvaluationCaseResult, double> p) => cases.Count == 0 ? 0 : cases.Average(p);
        long P(double q) { if (cases.Count == 0) return 0; var a = cases.Select(x => x.LatencyMs).Order().ToArray(); return a[(int)Math.Ceiling(q * a.Length) - 1]; }
        var values = new { Recall = Avg(x => x.RecallAtK), Mrr = Avg(x => x.Mrr), Ndcg = Avg(x => x.NdcgAtK), Facts = Avg(x => x.FactCoverage), Citations = Avg(x => x.CitationCoverage), Refusal = Avg(x => x.RefusalCorrect ? 1 : 0), Safe = Avg(x => x.NoForbiddenFacts ? 1 : 0), P50 = P(.5), P95 = P(.95) };
        var failed = new List<string>();
        Gate(values.Recall, t.RecallAtK, "Recall@K"); Gate(values.Mrr, t.Mrr, "MRR"); Gate(values.Ndcg, t.NdcgAtK, "NDCG@K");
        Gate(values.Facts, t.FactCoverage, "Fact coverage"); Gate(values.Citations, t.CitationCoverage, "Citation coverage");
        Gate(values.Refusal, t.RefusalAccuracy, "Refusal accuracy"); Gate(values.Safe, t.ForbiddenFactPassRate, "Forbidden-fact pass rate");
        if (values.P95 > t.P95LatencyMs) failed.Add($"p95 latency: {values.P95} > {t.P95LatencyMs} ms");
        return new(values.Recall, values.Mrr, values.Ndcg, values.Facts, values.Citations, values.Refusal, values.Safe, values.P50, values.P95, failed.Count == 0, failed);
        void Gate(double actual, double threshold, string name) { if (actual < threshold) failed.Add($"{name}: {actual:P1} < {threshold:P1}"); }
    }

    public async Task ExecuteRunAsync(string runId, CancellationToken ct)
    {
        var claimed = 0;
        if (db.Database.IsRelational())
            claimed = await db.RagEvaluationRuns.Where(x => x.Id == runId && x.Status == "pending")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "running").SetProperty(x => x.StartedAt, DateTime.UtcNow), ct);
        else
        {
            var pending = await db.RagEvaluationRuns.SingleOrDefaultAsync(x => x.Id == runId && x.Status == "pending", ct);
            if (pending != null) { pending.Status = "running"; pending.StartedAt = DateTime.UtcNow; claimed = 1; await db.SaveChangesAsync(ct); }
        }
        if (claimed == 0) return;
        var run = await db.RagEvaluationRuns.Include(x => x.Dataset).SingleAsync(x => x.Id == runId, ct);
        var cases = ParseCases(run.Dataset.CasesJson);
        var results = new List<RagEvaluationCaseResult>();
        try
        {
            foreach (var item in cases)
            {
                results.Add(await ExecuteCaseAsync(item, ct));
                run.CompletedCases = results.Count; run.ResultsJson = JsonSerializer.Serialize(results, Json); await db.SaveChangesAsync(ct);
            }
            var metrics = Aggregate(results, ParseThresholds(run.Dataset.ThresholdsJson));
            run.MetricsJson = JsonSerializer.Serialize(metrics, Json); run.Status = "completed"; run.CompletedAt = DateTime.UtcNow;
        }
        catch (Exception ex) { run.Status = "failed"; run.Error = ex.Message[..Math.Min(4000, ex.Message.Length)]; run.CompletedAt = DateTime.UtcNow; }
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static string Fold(string value)
    {
        var n = value.ToLowerInvariant().Normalize(NormalizationForm.FormD); var b = new StringBuilder(n.Length);
        foreach (var c in n) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) b.Append(c == 'ı' ? 'i' : c);
        return string.Join(' ', b.ToString().Normalize(NormalizationForm.FormC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
