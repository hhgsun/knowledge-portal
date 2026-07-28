-- ─────────────────────────────────────────────────────────────────────────────
-- HNSW recall / candidate-window benchmark
--
-- Answers the questions the search settings depend on, at a corpus size you choose:
--   1. What recall does the HNSW index actually give at each hnsw.ef_search?
--   2. How many DISTINCT ARTICLES does a candidate window of N chunks yield?
--      (VectorCandidateMultiplier exists because long documents crowd the window.)
--   3. Does the planner still let the HNSW index drive the scan — unfiltered AND
--      filtered? A filtered query that falls back to a sort over every embedding is
--      the difference between 50 ms and a full-table scan.
--   4. Does hnsw.iterative_scan recover the rows a filter removes?
--
-- Everything lives in TEMP tables: nothing touches application data, and the whole
-- corpus disappears when the session ends. Safe to run against any database that
-- has the vector extension — but see the cost note before pointing it at a shared one.
--
-- Usage:
--   psql "$CONN" -f scripts/hnsw_recall_benchmark.sql
--   psql "$CONN" -v n_articles=50000 -v chunks_per_article=15 -v n_probes=20 \
--                -v m=16 -v ef_construction=200 -f scripts/hnsw_recall_benchmark.sql
--
-- Cost note: generation is O(n_articles x dims) random() calls plus one HNSW build.
-- Measured: 2k articles (~36k chunks, 1024 dims) is well under a minute end to end.
-- 750k articles x 15 chunks (~11M vectors, the production target) is hours, needs tens of
-- GB of temp space, and needs maintenance_work_mem raised until the build stops printing
-- the "graph no longer fits" NOTICE — run that one on a scratch instance, not on shared dev.
-- Ground truth is an exact seq scan per probe, so keep n_probes small at scale.
--
-- Note on heavy_fraction: raising chunks_per_article makes every article longer, which is
-- NOT the production risk. The risk is variance — a few articles with huge attachments.
-- heavy_fraction is the knob that reproduces it; leave it on when sizing the candidate window.
-- ─────────────────────────────────────────────────────────────────────────────

\if :{?n_articles}         \else \set n_articles 5000         \endif
\if :{?chunks_per_article} \else \set chunks_per_article 8     \endif
\if :{?dims}               \else \set dims 1024                \endif
\if :{?n_probes}           \else \set n_probes 10              \endif
\if :{?k}                  \else \set k 20                     \endif
\if :{?m}                  \else \set m 16                     \endif
\if :{?ef_construction}    \else \set ef_construction 200      \endif
-- How far a chunk drifts from its article's centroid. Real chunks of one document are
-- near-duplicates of each other; that clustering is exactly what collapses the window.
\if :{?noise_scale}        \else \set noise_scale 0.35         \endif
-- How far an article drifts from its topic centroid. Smaller = more topically crowded corpus.
\if :{?topic_spread}       \else \set topic_spread 0.55        \endif
\if :{?n_topics}           \else \set n_topics 200             \endif
-- Share of articles that behave like a big attachment: 10-30x the usual chunk count.
\if :{?heavy_fraction}     \else \set heavy_fraction 0.1       \endif
-- Candidate windows to measure article yield for (chunk rows fetched before collapsing).
\if :{?windows}            \else \set windows '100,200,400,1000,2000' \endif
\if :{?ef_searches}        \else \set ef_searches '40,100,200,400,1000,2000' \endif

\timing off
\set ON_ERROR_STOP on

-- psql variables are client-side and invisible to the DO blocks below, so bridge the ones
-- they need into custom GUCs the server can read with current_setting().
SET bench.k = :k;
SET bench.ef_searches = :'ef_searches';
SET bench.windows = :'windows';

\echo ''
\echo '=== corpus ==================================================================='
\echo :n_articles ' articles x ' :chunks_per_article ' chunks, ' :dims ' dims, m=' :m ' ef_construction=' :ef_construction

-- IMPORTANT — random vectors MUST come from this VOLATILE function, never from an inline
-- `(SELECT array_agg(random()...) FROM generate_series(1, dims))`. That subquery does not
-- reference the outer row, so the planner hoists it out and evaluates it ONCE: every row
-- silently receives the SAME vector, every distance ties, and the benchmark reports pure
-- noise (recall 0%, no crowding) while looking perfectly healthy. LATERAL does not fix it
-- either — an uncorrelated LATERAL is still hoisted. A VOLATILE function cannot be.
CREATE FUNCTION pg_temp.rand_unit_vector(dims int) RETURNS vector AS $$
    SELECT l2_normalize(array_agg(random()::real - 0.5)::vector) FROM generate_series(1, dims);
$$ LANGUAGE sql VOLATILE;

-- pgvector has element-wise vector * vector but no vector * scalar, so scales are vectors.
CREATE TEMP TABLE bench_scale AS
SELECT (SELECT array_agg((:noise_scale)::real) FROM generate_series(1, :dims))::vector AS noise,
       (SELECT array_agg((:topic_spread)::real) FROM generate_series(1, :dims))::vector AS spread;

-- Topics, then articles inside a topic, then chunks inside an article. Real corpora are
-- clustered at every level, and that is precisely what makes a candidate window collapse:
-- the neighbours of a hit are its own chunks and its topic-mates, not the whole corpus.
-- Uniformly random vectors would sit at near-identical distance from everything (the
-- concentration of measure that high dimensions produce) and would report no crowding at all.
CREATE TEMP TABLE bench_topic AS
SELECT g AS topic_no, pg_temp.rand_unit_vector(:dims) AS v
FROM generate_series(1, :n_topics) g;

CREATE TEMP TABLE bench_centroid AS
SELECT g AS article_no,
       l2_normalize(t.v + (pg_temp.rand_unit_vector(:dims) * s.spread)) AS v,
       -- Chunk counts are the other half of the crowding story: most articles are short,
       -- a few carry big attachments and produce an order of magnitude more chunks. A
       -- uniform chunks-per-article corpus cannot reproduce the failure being measured.
       CASE WHEN random() < :heavy_fraction
            THEN (:chunks_per_article * (10 + floor(random() * 20)))::int
            ELSE greatest(1, (:chunks_per_article * (0.5 + random()))::int)
       END AS n_chunks
FROM generate_series(1, :n_articles) g
JOIN bench_topic t ON t.topic_no = 1 + (g % :n_topics)
CROSS JOIN bench_scale s;

-- A shared pool of perturbation directions: one vector add per chunk instead of dims
-- random() calls, which is what keeps generation feasible at millions of chunks. Chunks
-- from different articles that draw the same direction are slightly more alike than they
-- would be in real data — a second-order artifact, and the pool is large enough to dilute it.
CREATE TEMP TABLE bench_noise AS
SELECT g AS noise_no, pg_temp.rand_unit_vector(:dims) AS v
FROM generate_series(1, 256) g;

-- The cast to vector(:dims) is required: hnsw cannot index a vector column with no
-- declared dimension, and CREATE TABLE AS has nowhere else to put the typmod.
CREATE TEMP TABLE bench_embeddings AS
SELECT 'a' || c.article_no AS "ArticleId",
       k                   AS "ChunkIndex",
       l2_normalize(c.v + (n.v * s.noise))::vector(:dims) AS "Embedding"
FROM bench_centroid c
CROSS JOIN LATERAL generate_series(0, c.n_chunks - 1) AS k
CROSS JOIN bench_scale s
JOIN bench_noise n ON n.noise_no = 1 + (abs(hashtext(c.article_no::text || ':' || k::text)) % 256);

-- Mirror of the columns ArticleFilterSql filters on, so the filtered-plan check is honest.
CREATE TEMP TABLE bench_articles AS
SELECT 'a' || g                                                        AS "Id",
       'published'                                                     AS "Status",
       'owner' || (g % 50)                                             AS "OwnerId",
       (ARRAY['reference','tutorial','howto','faq'])[1 + (g % 4)]      AS "ContentType"
FROM generate_series(1, :n_articles) g;
CREATE INDEX ON bench_articles ("Id");
CREATE INDEX ON bench_articles ("Status");

ANALYZE bench_embeddings;
ANALYZE bench_articles;

-- Watch for "hnsw graph no longer fits into maintenance_work_mem after N tuples" below.
-- It is only a NOTICE, but it is the single most dangerous message in this whole file:
-- past that point the index is built in passes and its recall degrades permanently, with
-- no error and no sign at query time other than results that are quietly wrong.
SHOW maintenance_work_mem;
\echo '--- building HNSW index (this is the slow part) ---'
\timing on
CREATE INDEX bench_hnsw ON bench_embeddings
USING hnsw ("Embedding" vector_cosine_ops) WITH (m = :m, ef_construction = :ef_construction);
\timing off
ANALYZE bench_embeddings;

SELECT count(*)                                             AS vectors,
       pg_size_pretty(pg_total_relation_size('bench_embeddings')) AS table_size,
       pg_size_pretty(pg_relation_size('bench_hnsw'))       AS index_size
FROM bench_embeddings;

\echo '--- chunks per article (the skew that crowds the candidate window) ---'
SELECT min(n_chunks) AS min, round(avg(n_chunks), 1) AS avg,
       percentile_disc(0.95) WITHIN GROUP (ORDER BY n_chunks) AS p95,
       max(n_chunks) AS max
FROM bench_centroid;

-- Probe vectors: perturbed copies of real chunks, i.e. "a query about that document"
-- rather than a vector that happens to already be in the index.
CREATE TEMP TABLE bench_probe AS
SELECT row_number() OVER () AS probe_no,
       l2_normalize(e."Embedding" + (pg_temp.rand_unit_vector(:dims) * s.noise))::vector(:dims) AS v
FROM (SELECT "Embedding" FROM bench_embeddings ORDER BY random() LIMIT :n_probes) e
CROSS JOIN bench_scale s;

\echo ''
\echo '=== 1. ground truth (exact search, index disabled) ==========================='
SET enable_indexscan = off;
SET enable_bitmapscan = off;
\timing on
CREATE TEMP TABLE bench_exact AS
SELECT p.probe_no, e."ArticleId", e."ChunkIndex"
FROM bench_probe p
CROSS JOIN LATERAL (
    SELECT b."ArticleId", b."ChunkIndex"
    FROM bench_embeddings b
    ORDER BY b."Embedding" <=> p.v
    LIMIT :k
) e;
\timing off
RESET enable_indexscan;
RESET enable_bitmapscan;

\echo ''
\echo '=== 2. recall@k by ef_search ================================================='
CREATE TEMP TABLE bench_recall(ef int, probe_no int, hits int, ms numeric);

DO $bench$
DECLARE
    ef       int;
    p        record;
    t0       timestamptz;
    n_hits   int;
    k_target int := current_setting('bench.k')::int;
BEGIN
    FOREACH ef IN ARRAY string_to_array(current_setting('bench.ef_searches'), ',')::int[] LOOP
        PERFORM set_config('hnsw.ef_search', ef::text, false);
        FOR p IN SELECT probe_no, v FROM bench_probe ORDER BY probe_no LOOP
            t0 := clock_timestamp();
            SELECT count(*) INTO n_hits
            FROM (SELECT b."ArticleId", b."ChunkIndex"
                  FROM bench_embeddings b
                  ORDER BY b."Embedding" <=> p.v
                  LIMIT k_target) h
            JOIN bench_exact x USING ("ArticleId", "ChunkIndex")
            WHERE x.probe_no = p.probe_no;

            INSERT INTO bench_recall
            VALUES (ef, p.probe_no, n_hits,
                    round(extract(epoch FROM clock_timestamp() - t0)::numeric * 1000, 2));
        END LOOP;
    END LOOP;
END
$bench$;

SELECT ef                                              AS ef_search,
       round(100.0 * avg(hits) / :k, 1)                AS "recall@k_pct",
       round(100.0 * min(hits) / :k, 1)                AS worst_probe_pct,
       round(avg(ms), 2)                               AS avg_ms
FROM bench_recall
GROUP BY ef
ORDER BY ef;

\echo ''
\echo '=== 3. distinct-article yield of the candidate window ========================'
\echo '(how many articles a window of N chunk rows collapses to — sizes'
\echo ' VectorCandidateMultiplier: window / requested_articles)'
CREATE TEMP TABLE bench_yield(window_size int, probe_no int, articles int);

DO $bench$
DECLARE
    w        int;
    p        record;
    n        int;
    windows  int[] := string_to_array(current_setting('bench.windows'), ',')::int[];
BEGIN
    -- ef_search below the window would truncate it before the collapse is measured,
    -- which would report the truncation as if it were article crowding.
    PERFORM set_config('hnsw.ef_search',
                       greatest((SELECT max(x) FROM unnest(windows) x), 200)::text, false);
    FOREACH w IN ARRAY windows LOOP
        FOR p IN SELECT probe_no, v FROM bench_probe ORDER BY probe_no LOOP
            SELECT count(DISTINCT c."ArticleId") INTO n
            FROM (SELECT b."ArticleId" FROM bench_embeddings b
                  ORDER BY b."Embedding" <=> p.v LIMIT w) c;
            INSERT INTO bench_yield VALUES (w, p.probe_no, n);
        END LOOP;
    END LOOP;
END
$bench$;

SELECT window_size                        AS chunk_window,
       round(avg(articles), 1)            AS avg_distinct_articles,
       min(articles)                      AS worst_case,
       round(avg(articles) / window_size, 3) AS articles_per_chunk_row
FROM bench_yield
GROUP BY window_size
ORDER BY window_size;

\echo ''
\echo '=== 4. plan check: does the HNSW index drive the scan? ======================='
\echo '(UNFILTERED must show "Index Scan using bench_hnsw"; FILTERED is the one at risk)'

SET hnsw.ef_search = 400;

\echo '--- unfiltered (the shape VectorSearchService uses when no filter is set) ---'
EXPLAIN (COSTS OFF, SUMMARY OFF)
SELECT b."ArticleId" FROM bench_embeddings b
ORDER BY b."Embedding" <=> (SELECT v FROM bench_probe WHERE probe_no = 1)
LIMIT 400;

\echo '--- filtered (EXISTS against articles, the shape a tag/author search uses) ---'
EXPLAIN (COSTS OFF, SUMMARY OFF)
SELECT b."ArticleId" FROM bench_embeddings b
WHERE EXISTS (SELECT 1 FROM bench_articles a
              WHERE a."Id" = b."ArticleId" AND a."Status" = 'published'
                AND a."ContentType" = 'reference')
ORDER BY b."Embedding" <=> (SELECT v FROM bench_probe WHERE probe_no = 1)
LIMIT 400;

\echo ''
\echo '=== 5. iterative_scan: does a filter still fill the window? =================='
\echo '(rows returned for a filter matching ~25% of articles; want close to the 400 asked for)'
SET hnsw.iterative_scan = off;
SELECT count(*) AS rows_without_iterative_scan FROM (
    SELECT b."ArticleId" FROM bench_embeddings b
    WHERE EXISTS (SELECT 1 FROM bench_articles a
                  WHERE a."Id" = b."ArticleId" AND a."Status" = 'published'
                    AND a."ContentType" = 'reference')
    ORDER BY b."Embedding" <=> (SELECT v FROM bench_probe WHERE probe_no = 1)
    LIMIT 400
) s;

SET hnsw.iterative_scan = relaxed_order;
SELECT count(*) AS rows_with_iterative_scan FROM (
    SELECT b."ArticleId" FROM bench_embeddings b
    WHERE EXISTS (SELECT 1 FROM bench_articles a
                  WHERE a."Id" = b."ArticleId" AND a."Status" = 'published'
                    AND a."ContentType" = 'reference')
    ORDER BY b."Embedding" <=> (SELECT v FROM bench_probe WHERE probe_no = 1)
    LIMIT 400
) s;
RESET hnsw.iterative_scan;

\echo ''
\echo '=== how to read this ========================================================='
\echo 'Ollama:HnswEfSearch*     -> lowest ef_search in table 2 whose recall you accept.'
\echo '                            ef_search must be >= the candidate window (table 3),'
\echo '                            or the window is truncated before it is collapsed.'
\echo 'VectorCandidateMultiplier-> from table 3: the window whose avg_distinct_articles'
\echo '                            comfortably exceeds the result count you serve,'
\echo '                            divided by that result count. Judge it on worst_case,'
\echo '                            not the average — that column is the bad query.'
\echo 'Section 4                -> if UNFILTERED is not an Index Scan on bench_hnsw at'
\echo '                            production scale, nothing else in this file matters.'
\echo '                            If only FILTERED falls back, denormalize the filter'
\echo '                            columns onto article_embeddings so no join is needed.'
\echo 'Section 5                -> a large gap means filtered search silently under-fills'
\echo '                            without iterative_scan (Ollama:HnswIterativeScan).'
\echo '                            READ SECTION 4 FIRST: iterative_scan only applies to an'
\echo '                            HNSW index scan, so if the filtered plan fell back to a'
\echo '                            sort, both numbers here will read 400 and mean nothing.'
\echo 'maintenance_work_mem     -> if the build printed the "no longer fits" NOTICE, every'
\echo '                            recall number above is for a degraded index. Raise it and'
\echo '                            rerun before believing anything else.'
\echo ''
\echo 'Temp tables vanish when this session ends; nothing was written to app data.'
