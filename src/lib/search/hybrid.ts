import { db } from "../db";
import { articles } from "../db/schema";
import { eq, and, inArray } from "drizzle-orm";
import { generateEmbedding } from "./embeddings";
import { searchSimilar } from "./qdrant";

/**
 * Full-text search using SQLite FTS5.
 * Returns article IDs with BM25 rank scores.
 */
export async function ftsSearch(
  query: string,
  limit: number = 20
): Promise<{ articleId: string; score: number }[]> {
  const client = (db as unknown as { $client: { execute: (sql: string, args?: unknown[]) => Promise<{ rows: unknown[][] }> } }).$client;

  const result = await client.execute(
    `SELECT a.id, rank
     FROM articles_fts fts
     JOIN articles a ON a.rowid = fts.rowid
     WHERE articles_fts MATCH ?
       AND a.status = 'published'
     ORDER BY rank
     LIMIT ?`,
    [query, limit]
  );

  return result.rows.map((row) => ({
    articleId: row[0] as string,
    score: Math.abs(row[1] as number), // FTS5 rank is negative (lower = better)
  }));
}

/**
 * Semantic search using Qdrant vector DB.
 */
export async function semanticSearch(
  query: string,
  limit: number = 20
): Promise<{ articleId: string; score: number }[]> {
  const queryVector = await generateEmbedding(query);
  const results = await searchSimilar(queryVector, limit);

  // Deduplicate by articleId, keeping highest score
  const articleScores = new Map<string, number>();
  for (const r of results) {
    const articleId = r.payload?.articleId as string;
    if (!articleId) continue;
    const existing = articleScores.get(articleId) || 0;
    if (r.score > existing) {
      articleScores.set(articleId, r.score);
    }
  }

  return Array.from(articleScores.entries()).map(([articleId, score]) => ({
    articleId,
    score,
  }));
}

/**
 * Hybrid search combining FTS5 + Qdrant results using Reciprocal Rank Fusion (RRF).
 * RRF score = sum(1 / (k + rank_i)) for each result system.
 */
export async function hybridSearch(
  query: string,
  limit: number = 20
): Promise<{ articleId: string; score: number }[]> {
  const K = 60; // RRF constant

  // Run both searches in parallel
  const [ftsResults, semanticResults] = await Promise.all([
    ftsSearch(query, limit).catch(() => [] as { articleId: string; score: number }[]),
    semanticSearch(query, limit).catch(() => [] as { articleId: string; score: number }[]),
  ]);

  // Calculate RRF scores
  const rrfScores = new Map<string, number>();

  // FTS results are already sorted by rank
  ftsResults.forEach((r, rank) => {
    const current = rrfScores.get(r.articleId) || 0;
    rrfScores.set(r.articleId, current + 1 / (K + rank + 1));
  });

  // Semantic results sorted by score descending
  semanticResults
    .sort((a, b) => b.score - a.score)
    .forEach((r, rank) => {
      const current = rrfScores.get(r.articleId) || 0;
      rrfScores.set(r.articleId, current + 1 / (K + rank + 1));
    });

  // Sort by RRF score descending
  return Array.from(rrfScores.entries())
    .map(([articleId, score]) => ({ articleId, score }))
    .sort((a, b) => b.score - a.score)
    .slice(0, limit);
}

/**
 * Fetch full article objects for a list of scored results.
 */
export async function fetchArticlesForResults(
  results: { articleId: string; score: number }[]
): Promise<(typeof articles.$inferSelect & { _score: number })[]> {
  if (results.length === 0) return [];

  const ids = results.map((r) => r.articleId);
  const scoreMap = new Map(results.map((r) => [r.articleId, r.score]));

  const rows = await db
    .select()
    .from(articles)
    .where(and(inArray(articles.id, ids), eq(articles.status, "published")))
    .all();

  // Maintain score-based ordering
  return rows
    .map((row) => ({ ...row, _score: scoreMap.get(row.id) || 0 }))
    .sort((a, b) => b._score - a._score);
}

/**
 * Update FTS index for a single article (call after content change).
 */
export async function updateFtsIndex(
  articleId: string,
  title: string,
  excerpt: string | null,
  plainText: string
): Promise<void> {
  const client = (db as unknown as { $client: { execute: (sql: string, args?: unknown[]) => Promise<unknown> } }).$client;

  // Get the rowid for this article
  const result = await client.execute(
    `SELECT rowid FROM articles WHERE id = ?`,
    [articleId]
  ) as { rows: unknown[][] };

  if (result.rows.length === 0) return;
  const rowid = result.rows[0][0] as number;

  // Delete old entry then insert new one
  await client.execute(
    `INSERT INTO articles_fts(articles_fts, rowid, title, excerpt, plain_text) VALUES ('delete', ?, ?, ?, ?)`,
    [rowid, title, excerpt || "", ""]
  );
  await client.execute(
    `INSERT INTO articles_fts(rowid, title, excerpt, plain_text) VALUES (?, ?, ?, ?)`,
    [rowid, title, excerpt || "", plainText]
  );
}
