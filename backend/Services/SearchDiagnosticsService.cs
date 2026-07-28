using System.Text;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace KnowledgePortal.Api.Services;

/// <summary>
/// Read-only health report for the two search indexes. Everything here is either a counting
/// query or an EXPLAIN — nothing is executed, nothing is written, so it is safe to run against
/// a live database on demand.
///
/// The report deliberately mixes three kinds of evidence, because no one of them is sufficient:
/// coverage says whether the indexes are complete, the plan probes say whether the indexes are
/// being *used* (the most damaging failure in this stack is silent — search keeps returning
/// correct results, just by scanning everything), and the traffic figures say what users are
/// actually experiencing. A green plan probe over a half-built index still serves bad results.
/// </summary>
public class SearchDiagnosticsService(AppDbContext db, IConfiguration config, IServiceProvider services)
{
    private const string HnswIndexName = "ix_article_embeddings_embedding_hnsw";
    private const string FtsIndexName = "idx_articles_search_vector";
    private const string TsConfig = "turkish";

    /// <summary>Window for the traffic figures. Long enough to smooth a quiet day, short enough
    /// that a regression introduced last week is not averaged away by the month before it.</summary>
    private const int TrafficWindowDays = 7;

    /// <summary>Failing articles listed individually before the rest are just counted.</summary>
    private const int MaxListedFailures = 10;

    public async Task<SearchDiagnosticsDto> CollectAsync(CancellationToken ct = default)
    {
        var published = await db.Articles.CountAsync(a => a.Status == "published", ct);
        var indexed = await db.Articles.CountAsync(a => a.Status == "published" && a.IndexedAt != null, ct);
        var embedding = await CollectEmbeddingHealthAsync(published, indexed, ct);

        if (!db.Database.IsRelational())
            return new SearchDiagnosticsDto(embedding, new FullTextHealthDto(published, 0, published),
                new VectorHealthDto(0, 0, 0, null, false), [], [], [], [],
                ["Teşhis bilgileri yalnızca PostgreSQL üzerinde toplanabilir."]);

        var fullText = await CollectFullTextAsync(published, ct);
        var vector = await CollectVectorAsync(ct);
        var indexes = await CollectIndexesAsync(ct);
        var traffic = await CollectTrafficAsync(ct);
        var settings = await CollectSettingsAsync(vector, indexes, ct);
        var plans = await ProbePlansAsync(vector, ct);

        var warnings = BuildWarnings(embedding, fullText, vector, indexes, plans, traffic, settings);

        return new SearchDiagnosticsDto(embedding, fullText, vector, indexes, plans, traffic, settings, warnings);
    }

    /// <summary>
    /// Queue depth alone cannot distinguish "working through it" from "permanently stuck", and
    /// those need opposite responses. The failure tracker knows which articles keep throwing, so
    /// the two are reported separately.
    /// </summary>
    private async Task<IndexingHealthDto> CollectEmbeddingHealthAsync(int published, int indexed, CancellationToken ct)
    {
        // Registered only when Ollama is enabled, so resolve it optionally.
        var failures = services.GetService<EmbeddingFailureTracker>()?.Snapshot();
        if (failures is not { Count: > 0 })
            return new IndexingHealthDto(published, indexed, published - indexed, 0, []);

        var worst = failures
            .OrderByDescending(f => f.Value.Count)
            .Take(MaxListedFailures)
            .ToList();

        var ids = worst.Select(f => f.Key).ToList();
        var titles = await db.Articles
            .Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, a.Title })
            .ToDictionaryAsync(a => a.Id, a => a.Title, ct);

        var listed = worst
            .Select(f => new FailingArticleDto(
                f.Key,
                titles.GetValueOrDefault(f.Key),
                f.Value.Count,
                f.Value.NextAttemptUtc.ToString("o")))
            .ToArray();

        return new IndexingHealthDto(published, indexed, published - indexed, failures.Count, listed);
    }

    private async Task<FullTextHealthDto> CollectFullTextAsync(int published, CancellationToken ct)
    {
        var withVector = await db.Database
            .SqlQueryRaw<int>("""
                SELECT count(*)::int AS "Value" FROM articles
                WHERE "Status" = 'published' AND search_vector IS NOT NULL
                """)
            .SingleAsync(ct);

        return new FullTextHealthDto(published, withVector, published - withVector);
    }

    private async Task<VectorHealthDto> CollectVectorAsync(CancellationToken ct)
    {
        var chunks = await db.ArticleEmbeddings.LongCountAsync(ct);
        var articles = await db.ArticleEmbeddings.Select(e => e.ArticleId).Distinct().LongCountAsync(ct);
        var orphans = await db.ArticleEmbeddings
            .Where(e => !db.Articles.Any(a => a.Id == e.ArticleId && a.Status == "published"))
            .LongCountAsync(ct);

        // pg_relation_size covers the heap only; the vectors themselves are TOASTed, which is
        // why the "table" can look tiny next to its index without pg_total_relation_size.
        var tableSize = await db.Database
            .SqlQueryRaw<string>("""SELECT pg_size_pretty(pg_total_relation_size('article_embeddings')) AS "Value" """)
            .SingleAsync(ct);

        // The filter columns only exist once DenormalizeEmbeddingFilterColumns has run. Without
        // them a filtered vector search has to join articles, which is what costs the index scan.
        var denormalized = await db.Database
            .SqlQueryRaw<bool>("""
                SELECT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_name = 'article_embeddings' AND column_name = 'TagSlugs'
                ) AS "Value"
                """)
            .SingleAsync(ct);

        return new VectorHealthDto(chunks, articles, orphans, tableSize, denormalized);
    }

    /// <summary>
    /// Existence, validity and size of both search indexes. indisvalid matters on its own: an
    /// interrupted build leaves an index that Postgres silently refuses to use, so the plan
    /// probes would come back red with no explanation.
    /// </summary>
    private async Task<IndexHealthDto[]> CollectIndexesAsync(CancellationToken ct)
    {
        var rows = await db.Database
            .SqlQueryRaw<IndexRow>($"""
                SELECT c.relname                            AS "Name",
                       t.relname                            AS "Table",
                       i.indisvalid                         AS "Valid",
                       pg_size_pretty(pg_relation_size(c.oid)) AS "Size",
                       pg_get_indexdef(c.oid)               AS "Definition"
                FROM pg_class c
                JOIN pg_index i ON i.indexrelid = c.oid
                JOIN pg_class t ON t.oid = i.indrelid
                WHERE c.relname IN ('{HnswIndexName}', '{FtsIndexName}')
                """)
            .ToListAsync(ct);

        return
        [
            BuildIndex(HnswIndexName, "article_embeddings"),
            BuildIndex(FtsIndexName, "articles"),
        ];

        IndexHealthDto BuildIndex(string name, string table)
        {
            var row = rows.FirstOrDefault(r => r.Name == name);
            return row == null
                ? new IndexHealthDto(name, table, false, false, null, null)
                : new IndexHealthDto(name, row.Table, true, row.Valid, row.Size, row.Definition);
        }
    }

    /// <summary>
    /// What users actually experienced, from the search log the API already writes on every
    /// query. Plans describe the shape of a query; this is the only evidence of whether it is
    /// fast, and whether people are finding anything.
    /// </summary>
    private async Task<SearchTrafficDto[]> CollectTrafficAsync(CancellationToken ct)
    {
        // Materialized through a row class with settable properties, the shape SqlQueryRaw
        // handles everywhere else in this codebase.
        var rows = await db.Database
            .SqlQueryRaw<TrafficRow>($"""
                SELECT "SearchType"                                                                AS "SearchType",
                       count(*)::int                                                               AS "Total",
                       count(*) FILTER (WHERE "ResultsCount" = 0)::int                             AS "ZeroResults",
                       COALESCE(percentile_disc(0.5) WITHIN GROUP (ORDER BY "ResponseTimeMs"), 0)  AS "P50Ms",
                       COALESCE(percentile_disc(0.95) WITHIN GROUP (ORDER BY "ResponseTimeMs"), 0) AS "P95Ms"
                FROM search_queries
                WHERE "CreatedAt" >= now() - interval '{TrafficWindowDays} days'
                GROUP BY "SearchType"
                ORDER BY count(*) DESC
                """)
            .ToListAsync(ct);

        return [.. rows.Select(r => new SearchTrafficDto(r.SearchType, r.Total, r.ZeroResults, r.P50Ms, r.P95Ms))];
    }

    private async Task<SearchSettingDto[]> CollectSettingsAsync(
        VectorHealthDto vector, IndexHealthDto[] indexes, CancellationToken ct)
    {
        var serverSettings = await db.Database
            .SqlQueryRaw<SettingRow>("""
                SELECT name AS "Name", setting || COALESCE(unit, '') AS "Value"
                FROM pg_settings
                WHERE name IN ('maintenance_work_mem', 'work_mem', 'shared_buffers', 'effective_cache_size')
                ORDER BY name
                """)
            .ToListAsync(ct);

        var settings = new List<SearchSettingDto>();
        foreach (var s in serverSettings)
            settings.Add(new SearchSettingDto(s.Name, s.Value, "PostgreSQL"));

        settings.Add(Setting("Ollama:HnswEfSearch", 200));
        settings.Add(Setting("Ollama:HnswEfSearchMultiplier", 4));
        settings.Add(Setting("Ollama:HnswEfSearchMax", 1000));
        settings.Add(new SearchSettingDto("Ollama:HnswIterativeScan",
            config["Ollama:HnswIterativeScan"] ?? "relaxed_order", "Uygulama"));
        settings.Add(Setting("Ollama:VectorCandidateMultiplier", 30));
        settings.Add(Setting("Ollama:VectorCandidateMax", 2000));
        settings.Add(Setting("Ollama:MaxIndexChunksPerArticle", 100));
        settings.Add(Setting("Ollama:ChunkBatchSize", 16));
        settings.Add(new SearchSettingDto("Ollama:MinSimilarityScore",
            config.GetValue("Ollama:MinSimilarityScore", 0.5).ToString("0.##"), "Uygulama"));

        // What the HNSW build needs to keep the whole graph in memory. Past this point pgvector
        // builds in passes and the resulting index loses recall permanently, with no error.
        var estimate = EstimateBuildMemoryBytes(vector, indexes);
        if (estimate > 0)
            settings.Add(new SearchSettingDto("HNSW build belleği (tahmini gereken)",
                FormatBytes(estimate), "Hesaplanan"));

        return [.. settings];

        SearchSettingDto Setting(string key, object fallback) =>
            new(key, config[key] ?? fallback.ToString() ?? "", "Uygulama");
    }

    /// <summary>
    /// Runs EXPLAIN over the query shapes the application actually emits — both search types,
    /// because full-text is the default one and suffers the same silent index-abandonment as the
    /// vector path. Uses the same per-transaction GUCs as a real query and rolls back; EXPLAIN
    /// without ANALYZE never executes the plan.
    /// </summary>
    private async Task<PlanProbeDto[]> ProbePlansAsync(VectorHealthDto vector, CancellationToken ct)
    {
        var probes = new List<PlanProbeDto>();
        var efSearch = config.GetValue("Ollama:HnswEfSearch", 200);
        var iterativeScan = config["Ollama:HnswIterativeScan"] ?? "relaxed_order";

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // Full-text first: it is the default search type, so it is the one most users hit.
            probes.Add(await ExplainAsync("Tam metin araması (varsayılan)", FtsIndexName, $"""
                SELECT a."Id" FROM articles a
                WHERE a."Status" = 'published'
                  AND a.search_vector @@ to_tsquery('{TsConfig}', 'kpdiagnosticsprobe')
                ORDER BY ts_rank_cd(a.search_vector, to_tsquery('{TsConfig}', 'kpdiagnosticsprobe')) DESC, a."Id"
                LIMIT 20
                """, ct));

            if (vector.Chunks == 0)
            {
                probes.Add(new PlanProbeDto("Filtresiz vektör araması", false, "",
                    "Henüz hiç embedding yok — plan ölçülemez."));
                return [.. probes];
            }

#pragma warning disable EF1002 // validated int / allow-listed literal, see VectorSearchService
            await db.Database.ExecuteSqlRawAsync($"SET LOCAL hnsw.ef_search = {Math.Max(1, efSearch)}", ct);
            if (iterativeScan is "off" or "relaxed_order" or "strict_order")
                await db.Database.ExecuteSqlRawAsync($"SET LOCAL hnsw.iterative_scan = '{iterativeScan}'", ct);
#pragma warning restore EF1002

            probes.Add(await ExplainAsync("Filtresiz vektör araması", HnswIndexName, """
                SELECT e."ArticleId" FROM article_embeddings e
                ORDER BY e."Embedding" <=> (SELECT "Embedding" FROM article_embeddings LIMIT 1)
                LIMIT 100
                """, ct));

            probes.Add(vector.DenormalizedFilterColumns
                ? await ExplainAsync("Filtreli vektör araması (etiket + içerik tipi)", HnswIndexName, """
                    SELECT e."ArticleId" FROM article_embeddings e
                    WHERE true AND e."ContentType" = ANY(ARRAY['reference'])
                            AND e."TagSlugs" @> ARRAY['__diagnostics__']
                    ORDER BY e."Embedding" <=> (SELECT "Embedding" FROM article_embeddings LIMIT 1)
                    LIMIT 100
                    """, ct)
                : new PlanProbeDto("Filtreli vektör araması (etiket + içerik tipi)", false, "",
                    "Denormalize filtre kolonları yok — DenormalizeEmbeddingFilterColumns migration'ı uygulanmamış."));
        }
        finally
        {
            await tx.RollbackAsync(ct);
        }

        return [.. probes];
    }

    private async Task<PlanProbeDto> ExplainAsync(string name, string expectedIndex, string sql, CancellationToken ct)
    {
        try
        {
            var conn = db.Database.GetDbConnection();
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            cmd.CommandText = $"EXPLAIN (COSTS OFF) {sql}";

            var plan = new StringBuilder();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                plan.AppendLine(reader.GetString(0));

            var text = plan.ToString();
            return new PlanProbeDto(name, text.Contains(expectedIndex, StringComparison.Ordinal), text.TrimEnd(), null);
        }
        catch (Exception ex)
        {
            return new PlanProbeDto(name, false, "", ex.Message);
        }
    }

    private string[] BuildWarnings(IndexingHealthDto embedding, FullTextHealthDto fullText, VectorHealthDto vector,
        IndexHealthDto[] indexes, PlanProbeDto[] plans, SearchTrafficDto[] traffic, SearchSettingDto[] settings)
    {
        var warnings = new List<string>();

        foreach (var index in indexes.Where(i => !i.Exists))
            warnings.Add($"{index.Name} indeksi yok — {index.Table} üzerindeki aramalar tam tarama yapar.");
        foreach (var index in indexes.Where(i => i.Exists && !i.Valid))
            warnings.Add($"{index.Name} indeksi geçersiz durumda (yarıda kalmış bir kurulum). " +
                         "PostgreSQL onu sessizce yok sayar; REINDEX gerekir.");

        foreach (var probe in plans.Where(p => p.Error == null && !p.UsesVectorIndex))
            warnings.Add($"{probe.Name}: indeks kullanılmıyor, sorgu tabloyu baştan sona tarıyor. " +
                         "Küçük korpusta bu normaldir (tam tarama daha ucuzdur); büyük korpusta ciddi bir sorundur.");

        if (embedding.Failing > 0)
            warnings.Add($"{embedding.Failing} makale embedding sırasında sürekli hata alıyor — " +
                         "bunlar kuyrukta ilerlemiyor, kendiliğinden düzelmez.");

        if (!vector.DenormalizedFilterColumns)
            warnings.Add("Denormalize filtre kolonları eksik: filtreli semantik arama articles tablosuna " +
                         "join yapmak zorunda kalır ve HNSW indeksini kullanamaz.");

        if (embedding.Pending > 0)
            warnings.Add($"{embedding.Pending} makale embedding sırasında bekliyor — semantik arama eksik sonuç döndürür.");

        if (fullText.Missing > 0)
            warnings.Add($"{fullText.Missing} yayınlanmış makalenin tam metin indeksi yok — bu makaleler aramada çıkmaz.");

        if (vector.OrphanChunks > 0)
            warnings.Add($"{vector.OrphanChunks} embedding artık yayında olmayan makalelere ait (temizlik görevi bunları toplar).");

        // Traffic-derived warnings. Thresholds are deliberately loose — these are prompts to look,
        // not alarms, and a small sample says nothing either way.
        var totalSearches = traffic.Sum(t => t.Total);
        var totalZero = traffic.Sum(t => t.ZeroResults);
        if (totalSearches >= 20 && totalZero * 100 / totalSearches >= 25)
            warnings.Add($"Son {TrafficWindowDays} günde aramaların %{totalZero * 100 / totalSearches}'i sonuçsuz. " +
                         "Bu içerik boşluğu olabilir, ama eksik indeks de aynı şekilde görünür — " +
                         "önce yukarıdaki kapsama oranlarına bakın.");

        // Two ways to be worth reporting: slow with enough samples to trust, or so slow that a
        // handful of samples already settles it. A fixed minimum sample size would have hidden
        // the worst case — a few RAG queries taking minutes each say more than twenty fast ones.
        foreach (var slow in traffic.Where(t => t.P95Ms >= 2000 && (t.Total >= 10 || t.P95Ms >= 15000)))
        {
            var note = slow.SearchType is "semantic" or "hybrid" or "rag"
                ? " Bu süreye Ollama model çağrıları dahildir, yani sorgu planlarından bağımsız olabilir."
                : "";
            warnings.Add($"'{slow.SearchType}' aramalarının p95 yanıt süresi {slow.P95Ms:N0} ms " +
                         $"({slow.Total} arama, son {TrafficWindowDays} gün).{note}");
        }

        var required = EstimateBuildMemoryBytes(vector, indexes);
        var configured = ParseMemorySetting(settings.FirstOrDefault(s => s.Name == "maintenance_work_mem")?.Value);
        if (required > 0 && configured > 0 && configured < required)
            warnings.Add($"maintenance_work_mem ({FormatBytes(configured)}) HNSW grafiği için yetersiz " +
                         $"(tahmini {FormatBytes(required)} gerekir). İndeks kademeli kurulur ve recall kalıcı olarak düşer — " +
                         "hata vermez, sonuçlar sessizce kötüleşir.");

        return [.. warnings];
    }

    /// <summary>
    /// Rough bytes the HNSW build wants resident: the vector itself plus the graph's neighbour
    /// lists. Order-of-magnitude only, which is all the warning needs.
    /// </summary>
    private long EstimateBuildMemoryBytes(VectorHealthDto vector, IndexHealthDto[] indexes)
    {
        if (vector.Chunks == 0) return 0;
        var dims = config.GetValue("Ollama:EmbeddingDimensions", 1024);
        var definition = indexes.FirstOrDefault(i => i.Name == HnswIndexName)?.Definition;
        var m = ParseIndexOption(definition, "m") ?? 16;
        return vector.Chunks * (dims * 4L + m * 2L * 8L);
    }

    private static int? ParseIndexOption(string? indexDef, string option)
    {
        if (string.IsNullOrEmpty(indexDef)) return null;
        var marker = option + "=";
        var at = indexDef.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) return null;
        var digits = new string(indexDef[(at + marker.Length)..].SkipWhile(c => c == '\'').TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var value) ? value : null;
    }

    /// <summary>Parses a pg_settings memory value such as "65536kB" into bytes.</summary>
    private static long ParseMemorySetting(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var digits = new string(value.TakeWhile(char.IsDigit).ToArray());
        if (!long.TryParse(digits, out var amount)) return 0;
        var unit = value[digits.Length..].Trim().ToLowerInvariant();
        return unit switch
        {
            "kb" => amount * 1024,
            "mb" => amount * 1024 * 1024,
            "gb" => amount * 1024L * 1024 * 1024,
            "8kb" => amount * 8 * 1024,
            _ => amount
        };
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "kB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.#} {units[unit]}";
    }

    private class IndexRow
    {
        public string Name { get; set; } = "";
        public string Table { get; set; } = "";
        public bool Valid { get; set; }
        public string? Size { get; set; }
        public string? Definition { get; set; }
    }

    private class TrafficRow
    {
        public string SearchType { get; set; } = "";
        public int Total { get; set; }
        public int ZeroResults { get; set; }
        public int P50Ms { get; set; }
        public int P95Ms { get; set; }
    }

    private class SettingRow
    {
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
    }
}
