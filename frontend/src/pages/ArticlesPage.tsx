import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { PlusCircle, BookOpen, User, Key, Tag, ChevronLeft, ChevronRight, Eye, ThumbsUp } from "lucide-react";
import { useApi } from "../hooks/useApi";
import { useAuth } from "../contexts/AuthContext";
import type { ArticleListItem } from "../types/api";

const LIMIT = 20;

export default function ArticlesPage() {
  const { fetchWithAuth } = useApi();
  const { user } = useAuth();
  const [articles, setArticles] = useState<ArticleListItem[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [statusFilter, setStatusFilter] = useState<string>("");
  const [contentTypeFilter, setContentTypeFilter] = useState<string>("");
  const [difficultyFilter, setDifficultyFilter] = useState<string>("");
  const [mineFilter, setMineFilter] = useState(false);
  const [sortBy, setSortBy] = useState<string>("updatedAt");

  const isApprover = user?.role === "admin" || user?.role === "editor";
  const totalPages = Math.ceil(total / LIMIT);

  useEffect(() => {
    setLoading(true);
    const params = new URLSearchParams();
    params.set("page", String(page));
    params.set("limit", String(LIMIT));
    if (statusFilter) params.set("status", statusFilter);
    if (contentTypeFilter) params.set("contentType", contentTypeFilter);
    if (difficultyFilter) params.set("difficulty", difficultyFilter);
    if (mineFilter) params.set("mine", "true");

    fetchWithAuth(`/api/articles?${params}`)
      .then((res) => res.json())
      .then((data) => {
        let items: ArticleListItem[] = data.articles || [];
        if (sortBy === "wilsonScore") {
          items = [...items].sort((a, b) => b.wilsonScore - a.wilsonScore);
        } else if (sortBy === "viewCount") {
          items = [...items].sort((a, b) => b.viewCount - a.viewCount);
        }
        setArticles(items);
        setTotal(data.total || 0);
        setLoading(false);
      })
      .catch(() => setLoading(false));
  }, [fetchWithAuth, statusFilter, contentTypeFilter, difficultyFilter, mineFilter, page, sortBy]);

  const statusColors: Record<string, string> = {
    draft: "bg-zinc-100 text-zinc-600",
    pending: "bg-amber-100 text-amber-700",
    published: "bg-green-100 text-green-700",
    archived: "bg-red-100 text-red-700",
  };

  const difficultyColors: Record<string, string> = {
    beginner: "bg-blue-100 text-blue-700",
    intermediate: "bg-orange-100 text-orange-700",
    advanced: "bg-red-100 text-red-700",
  };

  return (
    <div className="max-w-5xl mx-auto">
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-bold text-zinc-900 dark:text-zinc-100">Articles</h1>
          <p className="text-sm text-zinc-500 mt-1">Browse and manage knowledge articles</p>
        </div>
        <Link
          to="/articles/new"
          className="flex items-center gap-2 px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium rounded-lg transition-colors"
        >
          <PlusCircle size={16} />
          New Article
        </Link>
      </div>

      <div className="flex flex-wrap items-center gap-3 mb-4">
        {isApprover && (
          <div className="flex gap-2">
            {[
              { label: "All", value: "" },
              { label: "Pending Approval", value: "pending" },
              { label: "Draft", value: "draft" },
              { label: "Published", value: "published" },
            ].map((tab) => (
              <button
                key={tab.value}
                onClick={() => { setPage(1); setStatusFilter(tab.value); }}
                className={`px-3 py-1.5 text-sm rounded-lg transition-colors ${statusFilter === tab.value
                  ? "bg-blue-100 text-blue-700 font-medium dark:bg-blue-950 dark:text-blue-300"
                  : "text-zinc-500 hover:bg-zinc-100 dark:hover:bg-zinc-800"
                  }`}
              >
                {tab.label}
              </button>
            ))}
          </div>
        )}

        <div className="flex items-center gap-2 ml-auto">
          <button
            onClick={() => { setPage(1); setMineFilter(!mineFilter); }}
            className={`px-3 py-1.5 text-sm rounded-lg transition-colors ${mineFilter
              ? "bg-blue-100 text-blue-700 font-medium dark:bg-blue-950 dark:text-blue-300"
              : "text-zinc-500 hover:bg-zinc-100 dark:hover:bg-zinc-800 border border-zinc-300 dark:border-zinc-700"
              }`}
          >
            <User size={14} className="inline-block mr-1" />
            My Articles
          </button>
          <select
            value={contentTypeFilter}
            onChange={(e) => { setPage(1); setContentTypeFilter(e.target.value); }}
            className="text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg px-2 py-1.5 bg-white dark:bg-zinc-900 text-zinc-700 dark:text-zinc-300"
          >
            <option value="">All Types</option>
            <option value="reference">Reference</option>
            <option value="how-to">How-To</option>
            <option value="adr">ADR</option>
            <option value="runbook">Runbook</option>
            <option value="faq">FAQ</option>
            <option value="policy">Policy</option>
            <option value="onboarding">Onboarding</option>
          </select>
          <select
            value={difficultyFilter}
            onChange={(e) => { setPage(1); setDifficultyFilter(e.target.value); }}
            className="text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg px-2 py-1.5 bg-white dark:bg-zinc-900 text-zinc-700 dark:text-zinc-300"
          >
            <option value="">All Levels</option>
            <option value="beginner">Beginner</option>
            <option value="intermediate">Intermediate</option>
            <option value="advanced">Advanced</option>
          </select>
          <select
            value={sortBy}
            onChange={(e) => { setPage(1); setSortBy(e.target.value); }}
            className="text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg px-2 py-1.5 bg-white dark:bg-zinc-900 text-zinc-700 dark:text-zinc-300"
          >
            <option value="updatedAt">Son Güncellenen</option>
            <option value="wilsonScore">En Faydalı</option>
            <option value="viewCount">En Çok Görüntülenen</option>
          </select>
        </div>
      </div>

      {loading ? (
        <div className="text-center py-12 text-zinc-500">Loading...</div>
      ) : articles.length === 0 ? (
        <div className="text-center py-12 border border-dashed border-zinc-300 dark:border-zinc-700 rounded-xl">
          <BookOpen size={40} className="mx-auto text-zinc-300 mb-3" />
          <p className="text-zinc-500">No articles yet</p>
          <Link to="/articles/new" className="text-blue-600 hover:underline text-sm mt-1 inline-block">
            Create your first article
          </Link>
        </div>
      ) : (
        <div className="space-y-3">
          {articles.map((article) => (
            <Link
              key={article.id}
              to={`/articles/${article.slug}`}
              className="block border border-zinc-200 dark:border-zinc-800 rounded-xl p-4 hover:border-blue-300 dark:hover:border-blue-700 transition-colors"
            >
              <div className="flex items-start justify-between">
                <div className="flex-1">
                  <h3 className="font-medium text-zinc-900 dark:text-zinc-100">{article.title}</h3>
                  {article.excerpt && (
                    <p className="text-sm text-zinc-500 mt-1 line-clamp-2">{article.excerpt}</p>
                  )}
                  <div className="flex items-center gap-2 mt-2">
                    <span className={`text-xs px-2 py-0.5 rounded-full ${statusColors[article.status] || ""}`}>
                      {article.status}
                    </span>
                    <span className={`text-xs px-2 py-0.5 rounded-full ${difficultyColors[article.difficulty] || ""}`}>
                      {article.difficulty}
                    </span>
                    <span className="text-xs text-zinc-400">{article.contentType}</span>
                    <span className="flex items-center gap-0.5 text-xs text-zinc-400">
                      <Eye size={12} />
                      {article.viewCount}
                    </span>
                    {article.wilsonScore > 0 && (
                      <span className="flex items-center gap-0.5 text-xs text-blue-600 dark:text-blue-400">
                        <ThumbsUp size={12} />
                        {(article.wilsonScore * 100).toFixed(0)}%
                      </span>
                    )}
                    {article.tags?.length > 0 && (
                      <span className="flex items-center gap-1 flex-wrap">
                        <Tag size={12} className="text-zinc-400" />
                        {article.tags.map((tag) => (
                          <span
                            key={tag.id}
                            className="text-xs px-2 py-0.5 rounded-full bg-indigo-50 text-indigo-600 dark:bg-indigo-950 dark:text-indigo-400"
                          >
                            {tag.name}
                          </span>
                        ))}
                      </span>
                    )}
                    {article.apiKeyName ? (
                      <span className="flex items-center gap-1 text-xs text-purple-600 dark:text-purple-400">
                        <Key size={12} />
                        {article.apiKeyName}
                      </span>
                    ) : article.ownerName ? (
                      <span className="flex items-center gap-1 text-xs text-zinc-500">
                        <User size={12} />
                        {article.ownerName}
                      </span>
                    ) : null}
                  </div>
                </div>
                <span className="text-xs text-zinc-400 ml-4 whitespace-nowrap">
                  {new Date(article.updatedAt).toLocaleDateString()}
                </span>
              </div>
            </Link>
          ))}
        </div>
      )}

      {totalPages > 1 && (
        <div className="flex items-center justify-between mt-6 pt-4 border-t border-zinc-200 dark:border-zinc-800">
          <span className="text-sm text-zinc-500">
            {total} article{total !== 1 ? "s" : ""} · Page {page} of {totalPages}
          </span>
          <div className="flex items-center gap-2">
            <button
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              disabled={page <= 1}
              className="flex items-center gap-1 px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg disabled:opacity-40 disabled:cursor-not-allowed hover:bg-zinc-50 dark:hover:bg-zinc-800 transition-colors"
            >
              <ChevronLeft size={14} />
              Previous
            </button>
            <button
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
              disabled={page >= totalPages}
              className="flex items-center gap-1 px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg disabled:opacity-40 disabled:cursor-not-allowed hover:bg-zinc-50 dark:hover:bg-zinc-800 transition-colors"
            >
              Next
              <ChevronRight size={14} />
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
