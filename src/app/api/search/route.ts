import { NextResponse } from "next/server";
import { db } from "@/lib/db";
import { articles, searchQueries } from "@/lib/db/schema";
import { auth } from "@/lib/auth/config";
import { like, eq, or, desc, and } from "drizzle-orm";
import { nanoid } from "nanoid";

export async function GET(request: Request) {
  const session = await auth();
  if (!session?.user) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const startTime = Date.now();
  const { searchParams } = new URL(request.url);
  const query = searchParams.get("q");
  const type = searchParams.get("type") || "fulltext";
  const categoryId = searchParams.get("categoryId");
  const contentType = searchParams.get("contentType");
  const difficulty = searchParams.get("difficulty");
  const limit = Math.min(parseInt(searchParams.get("limit") || "20"), 50);

  if (!query || query.trim().length === 0) {
    return NextResponse.json({ results: [], query: "" });
  }

  const searchTerm = `%${query.trim()}%`;

  const conditions = [
    eq(articles.status, "published"),
    or(
      like(articles.title, searchTerm),
      like(articles.excerpt, searchTerm)
    ),
  ];

  if (categoryId) conditions.push(eq(articles.categoryId, categoryId));
  if (contentType) conditions.push(eq(articles.contentType, contentType as "how-to" | "reference" | "adr" | "runbook" | "faq" | "policy" | "onboarding"));
  if (difficulty) conditions.push(eq(articles.difficulty, difficulty as "beginner" | "intermediate" | "advanced"));

  const results = await db
    .select()
    .from(articles)
    .where(and(...conditions))
    .orderBy(desc(articles.updatedAt))
    .limit(limit)
    .all();

  const responseTimeMs = Date.now() - startTime;

  // Record search query for analytics
  await db.insert(searchQueries).values({
    id: nanoid(),
    query: query.trim(),
    userId: session.user.id,
    resultsCount: results.length,
    searchType: type as "fulltext" | "semantic" | "hybrid" | "rag",
    responseTimeMs,
  });

  return NextResponse.json({
    results,
    query: query.trim(),
    type,
    responseTimeMs,
    total: results.length,
  });
}
