import { NextResponse } from "next/server";
import { db } from "@/lib/db";
import { articles, articleViews, searchQueries } from "@/lib/db/schema";
import { auth } from "@/lib/auth/config";
import { hasPermission, type Role } from "@/lib/auth/rbac";
import { count, eq, sql, desc, gte, and, lt } from "drizzle-orm";

export async function GET() {
  const session = await auth();
  if (!session?.user) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const role = (session.user as { role: Role }).role;
  if (!hasPermission(role, "analytics:view")) {
    return NextResponse.json({ error: "Forbidden" }, { status: 403 });
  }

  const now = new Date();
  const weekAgo = new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000);
  const dayAgo = new Date(now.getTime() - 24 * 60 * 60 * 1000);

  // Total articles by status
  const articlesByStatus = await db
    .select({
      status: articles.status,
      count: count(),
    })
    .from(articles)
    .groupBy(articles.status)
    .all();

  // Views this week
  const viewsThisWeek = await db
    .select({ count: count() })
    .from(articleViews)
    .where(gte(articleViews.createdAt, weekAgo))
    .get();

  // Searches today
  const searchesToday = await db
    .select({ count: count() })
    .from(searchQueries)
    .where(gte(searchQueries.createdAt, dayAgo))
    .get();

  // Top searched queries (last 7 days)
  const topSearches = await db
    .select({
      query: searchQueries.query,
      count: count(),
    })
    .from(searchQueries)
    .where(gte(searchQueries.createdAt, weekAgo))
    .groupBy(searchQueries.query)
    .orderBy(desc(count()))
    .limit(10)
    .all();

  // Searches with no results (content gaps)
  const failedSearches = await db
    .select({
      query: searchQueries.query,
      count: count(),
    })
    .from(searchQueries)
    .where(
      and(
        gte(searchQueries.createdAt, weekAgo),
        eq(searchQueries.resultsCount, 0)
      )
    )
    .groupBy(searchQueries.query)
    .orderBy(desc(count()))
    .limit(10)
    .all();

  // Stale articles (lastReviewedAt > reviewIntervalDays ago)
  const staleArticles = await db
    .select({ count: count() })
    .from(articles)
    .where(
      and(
        eq(articles.status, "published"),
        lt(
          articles.lastReviewedAt,
          new Date(now.getTime() - 90 * 24 * 60 * 60 * 1000)
        )
      )
    )
    .get();

  // Most viewed articles this week
  const topArticles = await db
    .select({
      articleId: articleViews.articleId,
      title: articles.title,
      slug: articles.slug,
      views: count(),
    })
    .from(articleViews)
    .innerJoin(articles, eq(articleViews.articleId, articles.id))
    .where(gte(articleViews.createdAt, weekAgo))
    .groupBy(articleViews.articleId)
    .orderBy(desc(count()))
    .limit(10)
    .all();

  return NextResponse.json({
    overview: {
      totalArticles: articlesByStatus.reduce((sum, s) => sum + s.count, 0),
      articlesByStatus: Object.fromEntries(
        articlesByStatus.map((s) => [s.status, s.count])
      ),
      viewsThisWeek: viewsThisWeek?.count || 0,
      searchesToday: searchesToday?.count || 0,
      staleArticles: staleArticles?.count || 0,
    },
    topSearches,
    failedSearches,
    topArticles,
  });
}
