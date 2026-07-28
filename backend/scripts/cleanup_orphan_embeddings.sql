-- ─────────────────────────────────────────────────────────────────────────────
-- Orphan embedding cleanup — manual form
--
-- The application already does this on a timer: EmbeddingBackgroundService runs
-- EmbeddingService.CleanupOrphanEmbeddingsAsync every Ollama:OrphanCleanupIntervalHours
-- (default 24). This file is the same operation by hand, for when you do not want to wait
-- for the next sweep or the background service is not running.
--
-- What an orphan is: a chunk whose article no longer exists or is no longer published.
-- Embeddings are supposed to exist only for published articles — EmbedArticleAsync commits
-- the chunks and the published claim in one transaction, and unpublishing deletes them — but
-- a narrow race remains, and older builds could leak rows. That invariant is not cosmetic:
-- VectorSearchService deliberately omits a published check from the unfiltered scan because
-- it relies on it, so orphans cost candidate slots in every semantic search until swept.
--
-- Usage:
--   psql "$CONN" -f scripts/cleanup_orphan_embeddings.sql          -- report + delete
--   psql "$CONN" -v dry_run=1 -f scripts/cleanup_orphan_embeddings.sql   -- report only
-- ─────────────────────────────────────────────────────────────────────────────

\if :{?dry_run} \else \set dry_run 0 \endif
\set ON_ERROR_STOP on

\echo ''
\echo '=== orphan chunks, by reason ================================================='
SELECT CASE WHEN a."Id" IS NULL THEN 'article deleted'
            ELSE 'article no longer published (' || a."Status" || ')'
       END                              AS reason,
       count(*)                         AS chunks,
       count(DISTINCT e."ArticleId")    AS articles
FROM article_embeddings e
LEFT JOIN articles a ON a."Id" = e."ArticleId"
WHERE a."Id" IS NULL OR a."Status" <> 'published'
GROUP BY 1
ORDER BY chunks DESC;

\if :dry_run
\echo ''
\echo 'dry_run=1 — nothing deleted.'
\else

\echo ''
\echo '=== deleting ================================================================='
-- Mirrors CleanupOrphanEmbeddingsAsync exactly: NOT EXISTS against published articles, so
-- "deleted" and "unpublished" are handled by the same predicate.
DELETE FROM article_embeddings e
WHERE NOT EXISTS (
    SELECT 1 FROM articles a
    WHERE a."Id" = e."ArticleId" AND a."Status" = 'published'
);

\echo ''
\echo '=== remaining orphans (expect 0) ============================================='
SELECT count(*) AS remaining
FROM article_embeddings e
WHERE NOT EXISTS (
    SELECT 1 FROM articles a
    WHERE a."Id" = e."ArticleId" AND a."Status" = 'published'
);

\echo ''
\echo 'On a large table this deletion leaves dead tuples behind; autovacuum will reclaim'
\echo 'them, or run VACUUM (ANALYZE) article_embeddings if you need the space back now.'
\endif
