import { NextResponse } from "next/server";
import { db } from "@/lib/db";
import { articles, searchQueries, tags, articleTags } from "@/lib/db/schema";
import { getAuthFromRequest } from "@/lib/auth/api-key";
import { like, eq, or, desc, and, inArray } from "drizzle-orm";
import { nanoid } from "nanoid";
import { ftsSearch, semanticSearch, hybridSearch, fetchArticlesForResults } from "@/lib/search/hybrid";
import { ragQuery } from "@/lib/search/rag";

/**
 * Parse @tag syntax from query.
 * "@docker" → { tag: "docker", searchText: "" }
 * "@docker volume mount" → { tag: "docker", searchText: "volume mount" }
 * "normal search" → { tag: null, searchText: "normal search" }
 */
function parseTagQuery(query: string): { tag: string | null; searchText: string } {
  const match = query.match(/^@(\S+)\s*(.*)?$/);
  if (match) {
    return { tag: match[1], searchText: (match[2] || "").trim() };
  }
  return { tag: null, searchText: query };
}

export async function GET(request: Request) {
  const reqAuth = await getAuthFromRequest(request);
  if (!reqAuth) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const startTime = Date.now();
  const { searchParams } = new URL(request.url);
  const query = searchParams.get("q");
  const type = searchParams.get("type") || "hybrid";
  const limit = Math.min(parseInt(searchParams.get("limit") || "20"), 50);

  if (!query || query.trim().length === 0) {
    return NextResponse.json({ results: [], query: "" });
  }

  const trimmedQuery = query.trim();

  try {
    // Parse @tag syntax
    const { tag, searchText } = parseTagQuery(trimmedQuery);

    if (tag) {
      // Find tag by slug or name (case-insensitive)
      const tagRecord = await db
        .select()
        .from(tags)
        .where(or(eq(tags.slug, tag.toLowerCase()), eq(tags.name, tag)))
        .get();

      if (!tagRecord) {
        return NextResponse.json({ results: [], query: trimmedQuery, error: "Tag not found" });
      }

      // Get article IDs for this tag
      const taggedArticleRows = await db
        .select({ articleId: articleTags.articleId })
        .from(articleTags)
        .where(eq(articleTags.tagId, tagRecord.id))
        .all();

      const articleIds = taggedArticleRows.map((r) => r.articleId);

      if (articleIds.length === 0) {
        return NextResponse.json({ results: [], query: trimmedQuery, type: "tag", total: 0 });
      }

      let results;

      if (!searchText) {
        // "@tag" only → return all articles with this tag
        results = await db
          .select()
          .from(articles)
          .where(
            and(
              inArray(articles.id, articleIds),
              eq(articles.status, "published")
            )
          )
          .orderBy(desc(articles.updatedAt))
          .limit(limit)
          .all();
      } else {
        // "@tag search text" → search within tagged articles
        let searchResults = await hybridSearch(searchText, limit * 3);
        searchResults = searchResults.filter((r) => articleIds.includes(r.articleId));
        results = await fetchArticlesForResults(searchResults.slice(0, limit));
      }

      const responseTimeMs = Date.now() - startTime;

      await db.insert(searchQueries).values({
        id: nanoid(),
        query: trimmedQuery,
        userId: reqAuth.userId,
        resultsCount: results.length,
        searchType: "fulltext",
        responseTimeMs,
      });

      return NextResponse.json({
        results,
        query: trimmedQuery,
        type: searchText ? "tag-search" : "tag",
        tag: tagRecord.name,
        responseTimeMs,
        total: results.length,
      });
    }

    // RAG mode - return AI answer with sources
    if (type === "rag") {
      const ragResult = await ragQuery(trimmedQuery);
      const responseTimeMs = Date.now() - startTime;

      await db.insert(searchQueries).values({
        id: nanoid(),
        query: trimmedQuery,
        userId: reqAuth.userId,
        resultsCount: ragResult.sources.length,
        searchType: "rag",
        responseTimeMs,
      });

      return NextResponse.json({
        answer: ragResult.answer,
        sources: ragResult.sources,
        query: trimmedQuery,
        type: "rag",
        responseTimeMs,
      });
    }

    // Determine search function based on type
    let searchResults: { articleId: string; score: number }[];

    switch (type) {
      case "semantic":
        searchResults = await semanticSearch(trimmedQuery, limit);
        break;
      case "fulltext":
        searchResults = await ftsSearch(trimmedQuery, limit).catch(() => {
          // Fallback to LIKE search if FTS5 table not available
          return [] as { articleId: string; score: number }[];
        });
        break;
      case "hybrid":
      default:
        searchResults = await hybridSearch(trimmedQuery, limit);
        break;
    }

    // If FTS returned empty (table might not exist yet), fallback to LIKE
    let results;
    if (searchResults.length === 0 && (type === "fulltext" || type === "hybrid")) {
      const searchTerm = `%${trimmedQuery}%`;
      results = await db
        .select()
        .from(articles)
        .where(
          and(
            eq(articles.status, "published"),
            or(
              like(articles.title, searchTerm),
              like(articles.excerpt, searchTerm)
            )
          )
        )
        .orderBy(desc(articles.updatedAt))
        .limit(limit)
        .all();
    } else {
      results = await fetchArticlesForResults(searchResults);
    }

    const responseTimeMs = Date.now() - startTime;

    // Record search query for analytics
    await db.insert(searchQueries).values({
      id: nanoid(),
      query: trimmedQuery,
      userId: reqAuth.userId,
      resultsCount: results.length,
      searchType: type as "fulltext" | "semantic" | "hybrid" | "rag",
      responseTimeMs,
    });

    return NextResponse.json({
      results,
      query: trimmedQuery,
      type,
      responseTimeMs,
      total: results.length,
    });
  } catch (error) {
    console.error("Search error:", error);

    // Fallback: basic LIKE search
    const searchTerm = `%${trimmedQuery}%`;
    const results = await db
      .select()
      .from(articles)
      .where(
        and(
          eq(articles.status, "published"),
          or(
            like(articles.title, searchTerm),
            like(articles.excerpt, searchTerm)
          )
        )
      )
      .orderBy(desc(articles.updatedAt))
      .limit(limit)
      .all();

    const responseTimeMs = Date.now() - startTime;

    await db.insert(searchQueries).values({
      id: nanoid(),
      query: trimmedQuery,
      userId: reqAuth.userId,
      resultsCount: results.length,
      searchType: "fulltext",
      responseTimeMs,
    });

    return NextResponse.json({
      results,
      query: trimmedQuery,
      type: "fulltext",
      responseTimeMs,
      total: results.length,
    });
  }
}
