import { NextResponse } from "next/server";
import { db } from "@/lib/db";
import { articles, searchQueries } from "@/lib/db/schema";
import { getAuthFromRequest } from "@/lib/auth/api-key";
import { like, eq, or, desc, and } from "drizzle-orm";
import { nanoid } from "nanoid";
import { ftsSearch, semanticSearch, hybridSearch, fetchArticlesForResults } from "@/lib/search/hybrid";
import { ragQuery } from "@/lib/search/rag";

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
