using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Pgvector;

namespace KnowledgePortal.Api.Services;

public record VectorSearchResult(string ArticleId, double Score, int ChunkIndex);

/// <summary>A single matched chunk with its stored text — used by RAG to build prompt context.</summary>
public record VectorChunkResult(string ArticleId, int ChunkIndex, double Score, string ChunkText);

/// <summary>
/// pgvector-backed semantic retrieval. Abstracted so RAG (and its tests) can depend on
/// the retrieval contract without the pgvector/Docker infrastructure.
/// </summary>
public interface IVectorSearchService
{
    /// <remarks>
    /// Contract: results are chunks of articles that were published when they were indexed, but
    /// the retrieval does not itself re-read <c>articles.Status</c> unless a filter forces it to
    /// (see <see cref="VectorSearchService"/> for why). Callers MUST re-check published when they
    /// resolve article metadata — every current one does.
    /// </remarks>
    /// <summary>Article-level retrieval: best matching chunk per article (list/semantic view).</summary>
    Task<List<VectorSearchResult>> SearchAsync(string queryText, int limit, CancellationToken ct = default, double? minScore = null, ArticleFilter? filter = null);

    /// <summary>
    /// Chunk-level retrieval: up to <paramref name="maxPerArticle"/> best chunks per article,
    /// globally capped at <paramref name="maxChunks"/>, with the chunk text included. Lets RAG
    /// consider several passages of a long document instead of a single window.
    /// </summary>
    Task<List<VectorChunkResult>> SearchChunksAsync(string queryText, int maxChunks, CancellationToken ct = default, double? minScore = null, int maxPerArticle = 3, ArticleFilter? filter = null);
}

public sealed class VectorSearchService(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IServiceScopeFactory scopeFactory,
    IConfiguration config) : IVectorSearchService
{
    private const int HnswEfSearchUpperBound = 1000;
    private readonly double _minScore = config.GetValue("Ollama:MinSimilarityScore", 0.5);
    // HNSW candidate-list size at query time. HNSW recall depends on ef_search being several times
    // the number of rows requested (rowLimit); when ef_search ≈ rowLimit recall degrades sharply at
    // millions of chunks. So ef_search = clamp(rowLimit * multiplier, floor, max):
    //   floor (HnswEfSearch)      — minimum candidate list even for tiny rowLimits (default 200)
    //   multiplier (…Multiplier)  — how many times rowLimit to scan; the main recall/latency knob
    //   max (…Max)                — latency safety rail so a large rowLimit can't explode ef_search
    // Tune the multiplier with scripts/hnsw_recall_benchmark.sql before changing the default.
    private readonly int _efSearch = config.GetValue("Ollama:HnswEfSearch", 200);
    private readonly int _efSearchMultiplier = config.GetValue("Ollama:HnswEfSearchMultiplier", 4);
    private readonly int _efSearchMax = config.GetValue("Ollama:HnswEfSearchMax", 1000);

    // Chunk-level over-fetch. The scan returns CHUNKS but callers want ARTICLES, and chunks per
    // article vary enormously — a long attachment can produce hundreds of near-identical chunks
    // that monopolise the candidate window and collapse it to a handful of distinct articles.
    // So the window is sized well above the requested article count; the collapsing happens in
    // SQL (DISTINCT ON / ROW_NUMBER), so the surplus rows never cross the wire.
    // Default of 30 comes from scripts/hnsw_recall_benchmark.sql section 3: on a clustered
    // corpus with a realistic spread of chunk counts, a window of N chunk rows collapsed to
    // roughly 0.08N distinct articles, and the worst-case probe to far fewer. Re-measure on
    // the real corpus before trusting this number — that is what the script is for.
    private readonly int _candidateMultiplier = config.GetValue("Ollama:VectorCandidateMultiplier", 30);
    private readonly int _candidateMax = config.GetValue("Ollama:VectorCandidateMax", 2000);

    // pgvector ≥ 0.8 iterative index scan. Without it a filtered vector query returns whatever
    // survives the filter out of one ef_search pass — so a narrow filter (one tag, one author)
    // over a large corpus yields almost nothing. With it, pgvector keeps walking the graph until
    // LIMIT filtered rows are found. Results are re-sorted by distance here, so relaxed_order
    // costs nothing. Set Ollama:HnswIterativeScan to "" on pgvector < 0.8, which has no such GUC.
    private readonly string _iterativeScan = NormalizeIterativeScan(config.GetValue("Ollama:HnswIterativeScan", "relaxed_order"));
    private readonly int _maxScanTuples = config.GetValue("Ollama:HnswMaxScanTuples", 0);

    /// <param name="minScore">Overrides Ollama:MinSimilarityScore when set — RAG uses a lower
    /// threshold than list-style semantic search (the LLM judges relevance itself).</param>
    /// <param name="filter">Applied inside the vector query, not to its output: filtering
    /// afterwards would discard most of an already-truncated candidate window.</param>
    public async Task<List<VectorSearchResult>> SearchAsync(string queryText, int limit, CancellationToken ct = default, double? minScore = null, ArticleFilter? filter = null)
    {
        var queryVector = await EmbedQueryAsync(queryText, ct);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var args = new List<object> { queryVector.ToString() };
        var scanWhere = ScanPredicate(filter, args);
        var rowLimit = CandidateRowLimit(limit);
        var rowLimitArg = ArticleFilterSql.Placeholder(args, rowLimit);
        var maxDistanceArg = ArticleFilterSql.Placeholder(args, MaxDistance(minScore));
        var limitArg = ArticleFilterSql.Placeholder(args, limit);

        // pgvector cosine distance: 1 - cosine_similarity, so score = 1 - distance.
        // The inner scan is a single ORDER BY distance LIMIT so the HNSW index can drive it;
        // DISTINCT ON then keeps each article's closest chunk, and only then is the score
        // threshold and the caller's limit applied — so `limit` counts articles, as promised.
        // EF1002: the interpolated fragments are SQL skeletons built from constants and {n}
        // placeholders only — the query vector, filter values and limits are all bound parameters.
#pragma warning disable EF1002
        var rows = await QueryWithEfSearchAsync(db, rowLimit, () => db.Database
            .SqlQueryRaw<PgvectorResult>(
                $$"""
                SELECT d."ArticleId", d."ChunkIndex", d."Distance"
                FROM (
                    SELECT DISTINCT ON (c."ArticleId") c."ArticleId", c."ChunkIndex", c."Distance"
                    FROM (
                        SELECT e."ArticleId", e."ChunkIndex", e."Embedding" <=> {0}::vector AS "Distance"
                        FROM article_embeddings e
                        {{scanWhere}}
                        ORDER BY e."Embedding" <=> {0}::vector
                        LIMIT {{rowLimitArg}}
                    ) c
                    ORDER BY c."ArticleId", c."Distance"
                ) d
                WHERE d."Distance" <= {{maxDistanceArg}}
                ORDER BY d."Distance"
                LIMIT {{limitArg}}
                """,
                args.ToArray())
            .ToListAsync(ct), ct);
#pragma warning restore EF1002

        return rows
            .Select(r => new VectorSearchResult(r.ArticleId, 1.0 - r.Distance, r.ChunkIndex))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<List<VectorChunkResult>> SearchChunksAsync(string queryText, int maxChunks, CancellationToken ct = default, double? minScore = null, int maxPerArticle = 3, ArticleFilter? filter = null)
    {
        var queryVector = await EmbedQueryAsync(queryText, ct);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var args = new List<object> { queryVector.ToString() };
        var scanWhere = ScanPredicate(filter, args);
        var rowLimit = CandidateRowLimit(maxChunks);
        var rowLimitArg = ArticleFilterSql.Placeholder(args, rowLimit);
        var perArticleArg = ArticleFilterSql.Placeholder(args, Math.Max(1, maxPerArticle));
        var maxDistanceArg = ArticleFilterSql.Placeholder(args, MaxDistance(minScore));
        var limitArg = ArticleFilterSql.Placeholder(args, maxChunks);

        // Same shape as SearchAsync, but keeping the best maxPerArticle chunks of each article
        // instead of just the closest one — ROW_NUMBER is the per-article ranking DISTINCT ON
        // cannot express.
        // EF1002: the interpolated fragments are SQL skeletons built from constants and {n}
        // placeholders only — the query vector, filter values and limits are all bound parameters.
#pragma warning disable EF1002
        var rows = await QueryWithEfSearchAsync(db, rowLimit, () => db.Database
            .SqlQueryRaw<PgvectorChunkRow>(
                $$"""
                SELECT r."ArticleId", r."ChunkIndex", r."Content", r."Distance"
                FROM (
                    SELECT c.*, ROW_NUMBER() OVER (PARTITION BY c."ArticleId" ORDER BY c."Distance") AS rn
                    FROM (
                        SELECT e."ArticleId", e."ChunkIndex", e."Content", e."Embedding" <=> {0}::vector AS "Distance"
                        FROM article_embeddings e
                        {{scanWhere}}
                        ORDER BY e."Embedding" <=> {0}::vector
                        LIMIT {{rowLimitArg}}
                    ) c
                ) r
                WHERE r.rn <= {{perArticleArg}} AND r."Distance" <= {{maxDistanceArg}}
                ORDER BY r."Distance"
                LIMIT {{limitArg}}
                """,
                args.ToArray())
            .ToListAsync(ct), ct);
#pragma warning restore EF1002

        return rows
            .Select(r => new VectorChunkResult(r.ArticleId, r.ChunkIndex, 1.0 - r.Distance, r.Content ?? ""))
            .ToList();
    }

    /// <summary>
    /// The WHERE clause of the vector scan. Critically, it never joins: the filter columns are
    /// denormalized onto article_embeddings (migration DenormalizeEmbeddingFilterColumns, kept
    /// current by triggers), so a filtered search is still a single-table
    /// "ORDER BY distance LIMIT n" with row filters — the one shape the HNSW index can drive.
    /// Joining articles in here instead lets the planner join first and sort afterwards, which
    /// discards the index and scans every embedding; it also makes hnsw.iterative_scan inert,
    /// since that only applies to a real index scan.
    /// There is no published check because embeddings only ever exist for published articles:
    /// <see cref="EmbeddingService.EmbedArticleAsync"/> commits chunks and the published claim in
    /// one transaction, unpublishing deletes them, and
    /// <see cref="EmbeddingService.CleanupOrphanEmbeddingsAsync"/> sweeps the residue. A chunk
    /// left over from that narrow race only costs a candidate slot — the over-fetch absorbs it and
    /// every caller re-checks published when resolving article metadata.
    /// If you change the shape emitted here, update section 4 of
    /// scripts/hnsw_recall_benchmark.sql to match — it probes these exact shapes, and a probe of
    /// a shape the application no longer emits raises false alarms about the behaviour it guards.
    /// </summary>
    private static string ScanPredicate(ArticleFilter? filter, List<object> args)
    {
        var filterSql = ArticleFilterSql.Build(filter, args, "e", idColumn: "ArticleId", tagArrayColumn: "TagSlugs");
        // Build emits " AND ..." fragments; "WHERE true" saves trimming the first one, and the
        // planner folds the constant away.
        return filterSql.Length == 0 ? "" : $"WHERE true{filterSql}";
    }

    /// <summary>Cosine distance ceiling equivalent to the effective minimum similarity score.</summary>
    private double MaxDistance(double? minScore) => 1.0 - (minScore ?? _minScore);

    /// <summary>How many chunk rows the index scan should consider to yield the requested
    /// number of articles/chunks after per-article collapsing.</summary>
    private int CandidateRowLimit(int limit)
        => Math.Clamp(limit * _candidateMultiplier, 50, Math.Max(50, _candidateMax));

    private async Task<Vector> EmbedQueryAsync(string queryText, CancellationToken ct)
    {
        var queryResults = await embeddingGenerator.GenerateAsync([queryText], cancellationToken: ct);
        return new Vector(queryResults[0].Vector.ToArray());
    }

    private static readonly string[] IterativeScanModes = ["off", "relaxed_order", "strict_order"];

    /// <summary>Guards the GUC value: it is interpolated into SQL, and an unknown mode would
    /// abort the query anyway. Empty/unset means "don't touch the setting".</summary>
    private static string NormalizeIterativeScan(string? configured)
    {
        var mode = configured?.Trim().ToLowerInvariant() ?? "";
        return IterativeScanModes.Contains(mode) ? mode : "";
    }

    /// <summary>
    /// Runs a vector query with per-transaction HNSW settings: an ef_search large enough to
    /// actually surface <paramref name="rowLimit"/> candidates, plus iterative scanning so
    /// filtered queries keep looking instead of returning a short list. SET LOCAL requires a
    /// transaction and GUC values must be literals (not bind parameters); all interpolated
    /// values here are validated ints or an allow-listed mode string, so this is injection-safe.
    /// </summary>
    private async Task<List<T>> QueryWithEfSearchAsync<T>(AppDbContext db, int rowLimit, Func<Task<List<T>>> runQuery, CancellationToken ct)
    {
        // pgvector accepts hnsw.ef_search only in the range 1..1000. The SQL LIMIT may be larger
        // (RAG defaults to 40 * 30 = 1200); iterative_scan can continue walking the graph beyond
        // the initial ef_search candidate list, so do not force ef_search up to rowLimit.
        var floor = Math.Clamp(_efSearch, 1, HnswEfSearchUpperBound);
        var ceiling = Math.Clamp(Math.Max(_efSearch, _efSearchMax), floor, HnswEfSearchUpperBound);
        var scaled = Math.Clamp((long)Math.Max(1, rowLimit) * Math.Max(1, _efSearchMultiplier), 1, HnswEfSearchUpperBound);
        var efSearch = (int)Math.Clamp(scaled, floor, ceiling);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
#pragma warning disable EF1002
        await db.Database.ExecuteSqlRawAsync($"SET LOCAL hnsw.ef_search = {efSearch}", ct);
        if (_iterativeScan.Length > 0)
            await db.Database.ExecuteSqlRawAsync($"SET LOCAL hnsw.iterative_scan = '{_iterativeScan}'", ct);
        if (_maxScanTuples > 0)
            await db.Database.ExecuteSqlRawAsync($"SET LOCAL hnsw.max_scan_tuples = {_maxScanTuples}", ct);
#pragma warning restore EF1002
        var rows = await runQuery();
        await tx.CommitAsync(ct);
        return rows;
    }

    private class PgvectorResult
    {
        public string ArticleId { get; set; } = null!;
        public int ChunkIndex { get; set; }
        public double Distance { get; set; }
    }

    private class PgvectorChunkRow
    {
        public string ArticleId { get; set; } = null!;
        public int ChunkIndex { get; set; }
        public string? Content { get; set; }
        public double Distance { get; set; }
    }
}
