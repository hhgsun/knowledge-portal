import { db } from "../lib/db";
import { articles } from "../lib/db/schema";
import { eq, and, lt, isNotNull, isNull, or, gt } from "drizzle-orm";
import { chunkDocument, extractPlainText } from "../lib/search/chunker";
import { generateEmbedding } from "../lib/search/embeddings";
import { ensureCollection, upsertChunks, deleteArticleChunks } from "../lib/search/qdrant";
import { updateFtsIndex } from "../lib/search/hybrid";
import { nanoid } from "nanoid";

/**
 * Worker entry point — runs embedding pipeline and staleness checker on a schedule.
 */
async function main() {
  console.log("🔧 Worker starting...");

  // Ensure Qdrant collection exists
  try {
    await ensureCollection();
    console.log("  ✓ Qdrant collection ready");
  } catch (err) {
    console.warn("  ⚠ Qdrant not available (will retry):", (err as Error).message);
  }

  // Run initial indexing
  await indexAllArticles();

  // Run staleness check
  await checkStaleness();

  // Schedule periodic runs
  setInterval(async () => {
    await indexAllArticles();
  }, 5 * 60 * 1000); // Every 5 minutes

  setInterval(async () => {
    await checkStaleness();
  }, 24 * 60 * 60 * 1000); // Daily

  console.log("✅ Worker running. Press Ctrl+C to stop.");
}

/**
 * Index only articles that have changed since last indexing (incremental).
 * Articles are eligible if: published, has content, and (never indexed OR updatedAt > indexedAt).
 */
async function indexAllArticles() {
  console.log("📄 Checking for articles to index...");

  const articlesToIndex = await db
    .select()
    .from(articles)
    .where(
      and(
        eq(articles.status, "published"),
        isNotNull(articles.content),
        or(
          isNull(articles.indexedAt),
          gt(articles.updatedAt, articles.indexedAt)
        )
      )
    )
    .all();

  if (articlesToIndex.length === 0) {
    console.log("  ✓ All articles up to date, nothing to index");
    return;
  }

  console.log(`  → ${articlesToIndex.length} article(s) need indexing`);

  let indexed = 0;
  for (const article of articlesToIndex) {
    try {
      if (!article.content) continue;

      // Chunk the document
      const chunks = chunkDocument(
        article.content as Record<string, unknown>,
        article.title
      );

      // Delete old chunks
      await deleteArticleChunks(article.id);

      // Generate embeddings and upsert
      const vectorChunks = [];
      for (const chunk of chunks) {
        const vector = await generateEmbedding(chunk.text);
        vectorChunks.push({
          id: nanoid(),
          text: chunk.text,
          vector,
          metadata: {
            heading: chunk.heading,
            chunkIndex: chunk.index,
            title: article.title,
            slug: article.slug,
          },
        });
      }

      await upsertChunks(article.id, vectorChunks);

      // Update FTS5 index
      const plainText = extractPlainText(article.content as Record<string, unknown>);
      await updateFtsIndex(article.id, article.title, article.excerpt, plainText).catch(() => {});

      // Mark article as indexed
      await db
        .update(articles)
        .set({ indexedAt: new Date() })
        .where(eq(articles.id, article.id));

      indexed++;
    } catch (err) {
      console.warn(`  ⚠ Failed to index article ${article.id}:`, (err as Error).message);
    }
  }

  console.log(`  ✓ Indexed ${indexed}/${articlesToIndex.length} articles`);
}

/**
 * Check for stale articles and log warnings.
 */
async function checkStaleness() {
  console.log("🕐 Checking article staleness...");

  const now = new Date();
  const staleThreshold = new Date(now.getTime() - 90 * 24 * 60 * 60 * 1000); // 90 days

  const staleArticles = await db
    .select({ id: articles.id, title: articles.title, lastReviewedAt: articles.lastReviewedAt })
    .from(articles)
    .where(
      and(
        eq(articles.status, "published"),
        lt(articles.lastReviewedAt, staleThreshold)
      )
    )
    .all();

  if (staleArticles.length > 0) {
    console.log(`  ⚠ ${staleArticles.length} stale article(s):`);
    for (const a of staleArticles.slice(0, 5)) {
      console.log(`    - "${a.title}" (last reviewed: ${a.lastReviewedAt})`);
    }
  } else {
    console.log("  ✓ All articles are fresh");
  }
}

main().catch(console.error);
