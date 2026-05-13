import { BookOpen, TrendingUp, AlertTriangle, Search } from "lucide-react";
import { db } from "@/lib/db";
import { articles, articleViews, searchQueries } from "@/lib/db/schema";
import { count, eq, gte, desc, and, lt } from "drizzle-orm";
import Link from "next/link";

export const dynamic = "force-dynamic";

export default async function HomePage() {
  const now = new Date();
  const weekAgo = new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000);
  const dayAgo = new Date(now.getTime() - 24 * 60 * 60 * 1000);
  const staleThreshold = new Date(now.getTime() - 90 * 24 * 60 * 60 * 1000);

  const [totalArticles, viewsThisWeek, staleCount, searchesToday, recentArticles, topSearches] =
    await Promise.all([
      db.select({ count: count() }).from(articles).get(),
      db.select({ count: count() }).from(articleViews).where(gte(articleViews.createdAt, weekAgo)).get(),
      db.select({ count: count() }).from(articles).where(
        and(eq(articles.status, "published"), lt(articles.lastReviewedAt, staleThreshold))
      ).get(),
      db.select({ count: count() }).from(searchQueries).where(gte(searchQueries.createdAt, dayAgo)).get(),
      db.select().from(articles).where(eq(articles.status, "published")).orderBy(desc(articles.updatedAt)).limit(5).all(),
      db.select({ query: searchQueries.query, count: count() }).from(searchQueries)
        .where(gte(searchQueries.createdAt, weekAgo)).groupBy(searchQueries.query).orderBy(desc(count())).limit(5).all(),
    ]);

  return (
    <div className="max-w-6xl mx-auto">
      {/* Welcome */}
      <div className="mb-8">
        <h1 className="text-2xl font-bold text-zinc-900 dark:text-zinc-100">
          Welcome to Knowledge Portal
        </h1>
        <p className="mt-1 text-zinc-500">
          Your enterprise knowledge base — search, browse, and contribute.
        </p>
      </div>

      {/* Stats Grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mb-8">
        <StatCard
          icon={<BookOpen size={20} />}
          label="Total Articles"
          value={String(totalArticles?.count || 0)}
          color="blue"
        />
        <StatCard
          icon={<TrendingUp size={20} />}
          label="Views This Week"
          value={String(viewsThisWeek?.count || 0)}
          color="green"
        />
        <StatCard
          icon={<AlertTriangle size={20} />}
          label="Stale Articles"
          value={String(staleCount?.count || 0)}
          color="amber"
        />
        <StatCard
          icon={<Search size={20} />}
          label="Searches Today"
          value={String(searchesToday?.count || 0)}
          color="purple"
        />
      </div>

      {/* Quick Actions */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <div className="border border-zinc-200 dark:border-zinc-800 rounded-xl p-6">
          <h2 className="font-semibold text-zinc-900 dark:text-zinc-100 mb-4">
            Recent Articles
          </h2>
          {recentArticles.length > 0 ? (
            <ul className="space-y-2">
              {recentArticles.map((a) => (
                <li key={a.id} className="flex items-center justify-between">
                  <Link href={`/articles/${a.slug}`} className="text-sm text-blue-600 hover:underline truncate">
                    {a.title}
                  </Link>
                  <span className="text-xs text-zinc-400 ml-2 shrink-0">{a.contentType}</span>
                </li>
              ))}
            </ul>
          ) : (
            <p className="text-sm text-zinc-500">
              No articles yet. Create your first article to get started.
            </p>
          )}
        </div>
        <div className="border border-zinc-200 dark:border-zinc-800 rounded-xl p-6">
          <h2 className="font-semibold text-zinc-900 dark:text-zinc-100 mb-4">
            Popular Searches
          </h2>
          {topSearches.length > 0 ? (
            <ul className="space-y-2">
              {topSearches.map((s, i) => (
                <li key={i} className="flex items-center justify-between">
                  <Link href={`/search?q=${encodeURIComponent(s.query)}`} className="text-sm text-zinc-700 dark:text-zinc-300 hover:text-blue-600">
                    {s.query}
                  </Link>
                  <span className="text-xs text-zinc-400">{s.count}×</span>
                </li>
              ))}
            </ul>
          ) : (
            <p className="text-sm text-zinc-500">No search data yet.</p>
          )}
        </div>
      </div>
    </div>
  );
}

function StatCard({
  icon,
  label,
  value,
  color,
}: {
  icon: React.ReactNode;
  label: string;
  value: string;
  color: "blue" | "green" | "amber" | "purple";
}) {
  const colorClasses = {
    blue: "bg-blue-50 text-blue-600 dark:bg-blue-950 dark:text-blue-400",
    green: "bg-green-50 text-green-600 dark:bg-green-950 dark:text-green-400",
    amber: "bg-amber-50 text-amber-600 dark:bg-amber-950 dark:text-amber-400",
    purple:
      "bg-purple-50 text-purple-600 dark:bg-purple-950 dark:text-purple-400",
  };

  return (
    <div className="border border-zinc-200 dark:border-zinc-800 rounded-xl p-4">
      <div className="flex items-center gap-3">
        <div className={`p-2 rounded-lg ${colorClasses[color]}`}>{icon}</div>
        <div>
          <p className="text-2xl font-bold text-zinc-900 dark:text-zinc-100">
            {value}
          </p>
          <p className="text-xs text-zinc-500">{label}</p>
        </div>
      </div>
    </div>
  );
}
