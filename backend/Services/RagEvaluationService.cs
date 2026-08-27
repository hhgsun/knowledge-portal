using System.Globalization;
using System.Security.Cryptography;
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
    double ForbiddenFactPassRate = 1, long P95LatencyMs = 30000, double GroundingCoverage = .8);
public record RagEvaluationCaseResult(string Id, string Category, double RecallAtK, double Mrr, double NdcgAtK,
    double FactCoverage, double CitationCoverage, double GroundingCoverage, bool RefusalCorrect, bool NoForbiddenFacts, long LatencyMs,
    List<string> RetrievedSlugs, List<string> ForbiddenFactHits, string Answer);
public record RagEvaluationMetrics(double RecallAtK, double Mrr, double NdcgAtK, double FactCoverage,
    double CitationCoverage, double GroundingCoverage, double RefusalAccuracy, double ForbiddenFactPassRate, long P50LatencyMs,
    long P95LatencyMs, bool Passed, List<string> FailedGates);

public class RagEvaluationService(AppDbContext db, RagService rag, IConfiguration config)
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
        var forbidden = item.ForbiddenFacts.Where(x => IsFactCovered(x, answer)).ToList();
        return new(item.Id, item.Category, recall, mrr, expected.Count == 0 ? 1 : ideal == 0 ? 0 : dcg / ideal,
            item.ExpectedFacts.Count == 0 ? 1 : item.ExpectedFacts.Count(x => IsFactCovered(x, answer)) / (double)item.ExpectedFacts.Count,
            response.Sources.Count == 0 ? (item.ExpectedRefusal ? 1 : 0) : response.CitationCoverage,
            response.ClaimSupportCoverage,
            Refusals.Any(x => answer.Contains(Fold(x), StringComparison.Ordinal)) == item.ExpectedRefusal,
            forbidden.Count == 0, watch.ElapsedMilliseconds, ranked, forbidden, response.Answer);
    }

    public static RagEvaluationMetrics Aggregate(List<RagEvaluationCaseResult> cases, RagEvaluationThresholds t)
    {
        double Avg(Func<RagEvaluationCaseResult, double> p) => cases.Count == 0 ? 0 : cases.Average(p);
        long P(double q) { if (cases.Count == 0) return 0; var a = cases.Select(x => x.LatencyMs).Order().ToArray(); return a[(int)Math.Ceiling(q * a.Length) - 1]; }
        var values = new { Recall = Avg(x => x.RecallAtK), Mrr = Avg(x => x.Mrr), Ndcg = Avg(x => x.NdcgAtK), Facts = Avg(x => x.FactCoverage), Citations = Avg(x => x.CitationCoverage), Grounding = Avg(x => x.GroundingCoverage), Refusal = Avg(x => x.RefusalCorrect ? 1 : 0), Safe = Avg(x => x.NoForbiddenFacts ? 1 : 0), P50 = P(.5), P95 = P(.95) };
        var failed = new List<string>();
        Gate(values.Recall, t.RecallAtK, "Recall@K"); Gate(values.Mrr, t.Mrr, "MRR"); Gate(values.Ndcg, t.NdcgAtK, "NDCG@K");
        Gate(values.Facts, t.FactCoverage, "Fact coverage"); Gate(values.Citations, t.CitationCoverage, "Citation coverage");
        Gate(values.Grounding, t.GroundingCoverage, "Grounding coverage");
        Gate(values.Refusal, t.RefusalAccuracy, "Refusal accuracy"); Gate(values.Safe, t.ForbiddenFactPassRate, "Forbidden-fact pass rate");
        if (values.P95 > t.P95LatencyMs) failed.Add($"p95 latency: {values.P95} > {t.P95LatencyMs} ms");
        return new(values.Recall, values.Mrr, values.Ndcg, values.Facts, values.Citations, values.Grounding, values.Refusal, values.Safe, values.P50, values.P95, failed.Count == 0, failed);
        void Gate(double actual, double threshold, string name) { if (actual < threshold) failed.Add($"{name}: {actual:P1} < {threshold:P1}"); }
    }

    public async Task<string?> ClaimNextAsync(string workerId, TimeSpan lease, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            var now = DateTime.UtcNow;
            var claimedIds = await db.Database.SqlQueryRaw<string>(
                """
                WITH picked AS (
                    SELECT id FROM rag_evaluation_runs
                    WHERE status = 'pending'
                       OR (status = 'running' AND (lease_expires_at IS NULL OR lease_expires_at < {0}))
                    ORDER BY created_at
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1
                )
                UPDATE rag_evaluation_runs r SET
                    status = 'running', started_at = COALESCE(r.started_at, {0}),
                    worker_id = {1}, lease_expires_at = {2}, attempt_count = r.attempt_count + 1,
                    completed_cases = 0, results_json = NULL, metrics_json = NULL,
                    error = NULL, completed_at = NULL
                FROM picked WHERE r.id = picked.id
                RETURNING r.id AS "Value"
                """, now, workerId, now.Add(lease)).ToListAsync(ct);
            return claimedIds.FirstOrDefault();
        }

        var nowMemory = DateTime.UtcNow;
        var run = await db.RagEvaluationRuns.OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(x =>
            x.Status == "pending" || (x.Status == "running" && (x.LeaseExpiresAt == null || x.LeaseExpiresAt < nowMemory)), ct);
        if (run == null) return null;
        run.Status = "running"; run.StartedAt ??= nowMemory; run.WorkerId = workerId;
        run.LeaseExpiresAt = nowMemory.Add(lease); run.AttemptCount++; run.CompletedCases = 0;
        run.ResultsJson = null; run.MetricsJson = null; run.Error = null; run.CompletedAt = null;
        await db.SaveChangesAsync(ct);
        return run.Id;
    }

    public async Task ExecuteRunAsync(string runId, string workerId, TimeSpan lease, CancellationToken ct)
    {
        var run = await db.RagEvaluationRuns.Include(x => x.Dataset).SingleAsync(x => x.Id == runId, ct);
        if (run.Status != "running" || run.WorkerId != workerId) return;
        var cases = ParseCases(run.CasesSnapshotJson);
        var results = new List<RagEvaluationCaseResult>();
        try
        {
            foreach (var item in cases)
            {
                results.Add(await ExecuteCaseAsync(item, ct));
                run.CompletedCases = results.Count; run.ResultsJson = JsonSerializer.Serialize(results, Json);
                run.LeaseExpiresAt = DateTime.UtcNow.Add(lease); await db.SaveChangesAsync(ct);
            }
            var metrics = Aggregate(results, ParseThresholds(run.ThresholdsSnapshotJson));
            run.MetricsJson = JsonSerializer.Serialize(metrics, Json); run.Status = "completed"; run.CompletedAt = DateTime.UtcNow;
        }
        catch (Exception ex) { run.Status = "failed"; run.Error = ex.Message[..Math.Min(4000, ex.Message.Length)]; run.CompletedAt = DateTime.UtcNow; }
        run.LeaseExpiresAt = null; run.WorkerId = null;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    public async Task<string> BuildRuntimeSnapshotAsync(CancellationToken ct)
    {
        var corpus = await db.Articles.Where(x => x.Status == "published").OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.UpdatedAt, x.IndexedAt, x.FtsIndexedAt }).ToListAsync(ct);
        var fingerprintInput = string.Join('\n', corpus.Select(x =>
            $"{x.Id}|{x.UpdatedAt:o}|{x.IndexedAt:o}|{x.FtsIndexedAt:o}"));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput))).ToLowerInvariant();

        return JsonSerializer.Serialize(new SortedDictionary<string, object?>
        {
            ["applicationVersion"] = typeof(RagService).Assembly.GetName().Version?.ToString(),
            ["promptVersion"] = RagService.PromptVersion,
            ["retrievalVersion"] = RagService.RetrievalVersion,
            ["chatModel"] = config["Ollama:ChatModel"],
            ["embeddingModel"] = config["Ollama:EmbeddingModel"],
            ["embeddingDimensions"] = config.GetValue<int>("Ollama:EmbeddingDimensions"),
            ["parentChunkTargetWords"] = config.GetValue<int>("Ollama:ParentChunkTargetWords",
                KnowledgeChunker.DefaultParentTargetWords),
            ["childChunkTargetWords"] = config.GetValue<int>("Ollama:ChildChunkTargetWords",
                KnowledgeChunker.DefaultChildTargetWords),
            ["childChunkOverlapWords"] = config.GetValue<int>("Ollama:ChildChunkOverlapWords",
                KnowledgeChunker.DefaultChildOverlapWords),
            ["chunkingVersion"] = config["Ollama:ChunkingVersion"] ?? "hierarchical-parent-child-v2",
            ["semanticIndexProfile"] = EmbeddingService.ComputeIndexProfile(config),
            ["ragCandidateLimit"] = config.GetValue<int>("Ollama:RagCandidateLimit"),
            ["ragBroadCandidateLimit"] = config.GetValue<int>("Ollama:RagBroadCandidateLimit"),
            ["ragMinSimilarityScore"] = config.GetValue<double>("Ollama:RagMinSimilarityScore"),
            ["ragLexicalWeight"] = config.GetValue<double>("Ollama:RagLexicalWeight"),
            ["ragSemanticWeight"] = config.GetValue<double>("Ollama:RagSemanticWeight"),
            ["ragMaxContextTokens"] = config.GetValue<int>("Ollama:RagMaxContextTokens", 12000),
            ["ragModelContextTokens"] = config.GetValue<int>("Ollama:RagModelContextTokens", 32768),
            ["ragPromptReserveTokens"] = config.GetValue<int>("Ollama:RagPromptReserveTokens", 2500),
            ["ragTokenizerStrategy"] = "qwen_unicode_estimator_calibrated_v1",
            ["ragSourceLimit"] = config.GetValue<int>("Ollama:RagSourceLimit", 10),
            ["ragMinimumSourceLimit"] = config.GetValue<int>("Ollama:RagMinimumSourceLimit", 3),
            ["ragSourceRelativeScoreFloor"] = config.GetValue<double>("Ollama:RagSourceRelativeScoreFloor", .55),
            ["ragMaxOutputTokens"] = config.GetValue<int>("Ollama:RagMaxOutputTokens"),
            ["queryRewriteEnabled"] = config.GetValue("Ollama:QueryUnderstanding:RewriteEnabled", true),
            ["queryDecompositionEnabled"] = config.GetValue("Ollama:QueryUnderstanding:DecompositionEnabled", true),
            ["queryMaxQueries"] = config.GetValue("Ollama:QueryUnderstanding:MaxQueries", 3),
            ["contextExpansionEnabled"] = config.GetValue("Ollama:ContextExpansion:Enabled", true),
            ["contextNeighborCount"] = config.GetValue("Ollama:ContextExpansion:NeighborCount", 1),
            ["freshnessWeight"] = config.GetValue("Ollama:Ranking:FreshnessWeight", .05),
            ["authorityWeight"] = config.GetValue("Ollama:Ranking:AuthorityWeight", .05),
            ["authoritySource"] = "lookup_values.authority_weight",
            ["externalRerankerEnabled"] = config.GetValue("Reranking:External:Enabled", false),
            ["externalRerankerModel"] = config["Reranking:External:Model"],
            ["requestBudgetSeconds"] = config.GetValue<int>("RagResilience:RequestBudgetSeconds"),
            ["publishedArticleCount"] = corpus.Count,
            ["semanticallyIndexedArticleCount"] = corpus.Count(x => x.IndexedAt != null),
            ["lexicallyIndexedArticleCount"] = corpus.Count(x => x.FtsIndexedAt != null),
            ["corpusFingerprint"] = fingerprint
        }, Json);
    }

    private static bool IsFactCovered(string expected, string foldedAnswer)
    {
        var fact = Fold(expected);
        if (foldedAnswer.Contains(fact, StringComparison.Ordinal)) return true;
        var expectedTokens = SignificantTokens(fact);
        if (expectedTokens.Count == 0) return false;
        var answerTokens = SignificantTokens(foldedAnswer);
        if (expectedTokens.Count(answerTokens.Contains) / (double)expectedTokens.Count < .8) return false;
        var expectedNumbers = System.Text.RegularExpressions.Regex.Matches(fact, @"\b\d+(?:[.,]\d+)?\b").Select(x => x.Value).ToHashSet();
        var answerNumbers = System.Text.RegularExpressions.Regex.Matches(foldedAnswer, @"\b\d+(?:[.,]\d+)?\b").Select(x => x.Value).ToHashSet();
        return expectedNumbers.IsSubsetOf(answerNumbers);
    }

    private static HashSet<string> SignificantTokens(string value) => value
        .Split(new[] { ' ', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries)
        .Where(x => x.Length >= 3 && x is not ("bir" or "ve" or "ile" or "icin" or "the" or "and" or "for"))
        .ToHashSet(StringComparer.Ordinal);

    private static string Fold(string value)
    {
        var n = value.ToLowerInvariant().Normalize(NormalizationForm.FormD); var b = new StringBuilder(n.Length);
        foreach (var c in n) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) b.Append(c == 'ı' ? 'i' : c);
        return string.Join(' ', b.ToString().Normalize(NormalizationForm.FormC).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
