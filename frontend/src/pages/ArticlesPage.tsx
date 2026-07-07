import { useEffect, useState, useCallback, useRef } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { PlusCircle, BookOpen, User, Key, Tag as TagIcon, ChevronLeft, ChevronRight, Eye, ThumbsUp, UserLockIcon, X, Filter, Calendar, ChevronDown } from "lucide-react";
import { useApi } from "../hooks/useApi";
import { useAuth } from "../contexts/AuthContext";
import { useLookups } from "../hooks/useLookups";
import { ContentTypeBadge } from "../components/ContentTypeBadge";
import type { ArticleListItem, Tag } from "../types/api";

const LIMIT = 20;

function MultiSelectDropdown({ label, options, selected, onChange, renderOption }: {
  label: string;
  options: { value: string; label: string }[];
  selected: string[];
  onChange: (values: string[]) => void;
  renderOption?: (opt: { value: string; label: string }) => React.ReactNode;
}) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, []);

  const toggle = (value: string) => {
    onChange(selected.includes(value) ? selected.filter(v => v !== value) : [...selected, value]);
  };

  return (
    <div className="relative" ref={ref}>
      <button
        onClick={() => setOpen(!open)}
        className={`flex items-center gap-1 px-3 py-1.5 text-sm rounded-lg transition-colors border ${selected.length > 0
          ? "bg-blue-50 border-blue-300 text-blue-700 dark:bg-blue-950 dark:border-blue-700 dark:text-blue-300"
          : "border-zinc-300 dark:border-zinc-700 text-zinc-600 dark:text-zinc-400 hover:bg-zinc-50 dark:hover:bg-zinc-800"
          }`}
      >
        {label}
        {selected.length > 0 && (
          <span className="ml-1 px-1.5 py-0.5 text-xs bg-blue-200 dark:bg-blue-800 rounded-full">{selected.length}</span>
        )}
        <ChevronDown size={14} />
      </button>
      {open && (
        <div className="absolute z-50 mt-1 w-56 max-h-60 overflow-y-auto bg-white dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-700 rounded-lg shadow-lg">
          {options.map((opt) => (
            <label
              key={opt.value}
              className="flex items-center gap-2 px-3 py-2 hover:bg-zinc-50 dark:hover:bg-zinc-800 cursor-pointer text-sm"
            >
              <input
                type="checkbox"
                checked={selected.includes(opt.value)}
                onChange={() => toggle(opt.value)}
                className="rounded border-zinc-300 dark:border-zinc-600"
              />
              {renderOption ? renderOption(opt) : <span className="text-zinc-700 dark:text-zinc-300">{opt.label}</span>}
            </label>
          ))}
          {options.length === 0 && (
            <div className="px-3 py-2 text-sm text-zinc-400">No options</div>
          )}
        </div>
      )}
    </div>
  );
}

export default function ArticlesPage() {
  const { fetchWithAuth } = useApi();
  const { user } = useAuth();
  const { contentTypes } = useLookups();
  const [searchParams, setSearchParams] = useSearchParams();
  const [articles, setArticles] = useState<ArticleListItem[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(() => Number(searchParams.get("page")) || 1);
  const [loading, setLoading] = useState(true);
  const [statusFilter, setStatusFilter] = useState<string[]>(() => searchParams.get("status")?.split(",").filter(Boolean) || []);
  const [contentTypeFilter, setContentTypeFilter] = useState<string[]>(() => searchParams.get("contentType")?.split(",").filter(Boolean) || []);
  const [tagFilter, setTagFilter] = useState<string[]>(() => searchParams.get("tag")?.split(",").filter(Boolean) || []);
  const [mineFilter, setMineFilter] = useState(() => searchParams.get("mine") === "true");
  const [sortBy, setSortBy] = useState<string>(() => searchParams.get("sort") || "updatedAt");
  const [dateFrom, setDateFrom] = useState<string>(() => searchParams.get("dateFrom") || "");
  const [dateTo, setDateTo] = useState<string>(() => searchParams.get("dateTo") || "");
  const [allTags, setAllTags] = useState<Tag[]>([]);

  // Load tags
  useEffect(() => {
    fetchWithAuth("/api/tags")
      .then((res) => res.json())
      .then((data) => { if (Array.isArray(data)) setAllTags(data); })
      .catch(() => {});
  }, [fetchWithAuth]);

  const syncSearchParams = useCallback((p: number, status: string[], ct: string[], tags: string[], mine: boolean, sort: string, df: string, dt: string) => {
    const params = new URLSearchParams();
    if (p > 1) params.set("page", String(p));
    if (status.length) params.set("status", status.join(","));
    if (ct.length) params.set("contentType", ct.join(","));
    if (tags.length) params.set("tag", tags.join(","));
    if (mine) params.set("mine", "true");
    if (sort && sort !== "updatedAt") params.set("sort", sort);
    if (df) params.set("dateFrom", df);
    if (dt) params.set("dateTo", dt);
    setSearchParams(params, { replace: true });
  }, [setSearchParams]);

  const isApprover = user?.role === "admin" || user?.role === "editor";
  const totalPages = Math.ceil(total / LIMIT);

  useEffect(() => {
    syncSearchParams(page, statusFilter, contentTypeFilter, tagFilter, mineFilter, sortBy, dateFrom, dateTo);
  }, [page, statusFilter, contentTypeFilter, tagFilter, mineFilter, sortBy, dateFrom, dateTo, syncSearchParams]);

  useEffect(() => {
    setLoading(true);
    const params = new URLSearchParams();
    params.set("page", String(page));
    params.set("limit", String(LIMIT));
    if (statusFilter.length) params.set("status", statusFilter.join(","));
    if (contentTypeFilter.length) params.set("contentType", contentTypeFilter.join(","));
    tagFilter.forEach(t => params.append("tag", t));
    if (mineFilter) params.set("mine", "true");
    if (dateFrom) params.set("dateFrom", dateFrom);
    if (dateTo) params.set("dateTo", dateTo);

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
  }, [fetchWithAuth, statusFilter, contentTypeFilter, tagFilter, mineFilter, page, sortBy, dateFrom, dateTo]);

  const statusColors: Record<string, string> = {
    draft: "bg-zinc-100 text-zinc-600",
    pending: "bg-amber-100 text-amber-700",
    published: "bg-green-100 text-green-700",
    archived: "bg-red-100 text-red-700",
  };

  const hasActiveFilters = statusFilter.length > 0 || contentTypeFilter.length > 0 || tagFilter.length > 0 || dateFrom || dateTo || mineFilter;

  const clearAllFilters = () => {
    setPage(1);
    setStatusFilter([]);
    setContentTypeFilter([]);
    setTagFilter([]);
    setMineFilter(false);
    setDateFrom("");
    setDateTo("");
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

      <div className="flex flex-wrap items-center gap-2 mb-4">
        <Filter size={16} className="text-zinc-400" />

        {isApprover && (
          <MultiSelectDropdown
            label="Status"
            options={[
              { value: "draft", label: "Draft" },
              { value: "pending", label: "Pending" },
              { value: "published", label: "Published" },
              { value: "archived", label: "Archived" },
            ]}
            selected={statusFilter}
            onChange={(v) => { setPage(1); setStatusFilter(v); }}
          />
        )}

        <MultiSelectDropdown
          label="Content Type"
          options={contentTypes.map(ct => ({ value: ct.value, label: ct.label }))}
          selected={contentTypeFilter}
          onChange={(v) => { setPage(1); setContentTypeFilter(v); }}
        />

        <MultiSelectDropdown
          label="Tags"
          options={allTags.map(t => ({ value: t.slug, label: t.name }))}
          selected={tagFilter}
          onChange={(v) => { setPage(1); setTagFilter(v); }}
        />

        <div className="flex items-center gap-1">
          <Calendar size={14} className="text-zinc-400" />
          <input
            type="date"
            value={dateFrom}
            onChange={(e) => { setPage(1); setDateFrom(e.target.value); }}
            className="text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg px-2 py-1.5 bg-white dark:bg-zinc-900 text-zinc-700 dark:text-zinc-300 w-[130px]"
            placeholder="From"
          />
          <span className="text-zinc-400 text-xs">–</span>
          <input
            type="date"
            value={dateTo}
            onChange={(e) => { setPage(1); setDateTo(e.target.value); }}
            className="text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg px-2 py-1.5 bg-white dark:bg-zinc-900 text-zinc-700 dark:text-zinc-300 w-[130px]"
            placeholder="To"
          />
        </div>

        <button
          onClick={() => { setPage(1); setMineFilter(!mineFilter); }}
          className={`flex items-center gap-1 px-3 py-1.5 text-sm rounded-lg transition-colors border ${mineFilter
            ? "bg-blue-50 border-blue-300 text-blue-700 dark:bg-blue-950 dark:border-blue-700 dark:text-blue-300"
            : "border-zinc-300 dark:border-zinc-700 text-zinc-600 dark:text-zinc-400 hover:bg-zinc-50 dark:hover:bg-zinc-800"
            }`}
        >
          <UserLockIcon size={14} />
          My Articles
        </button>

        <select
          value={sortBy}
          onChange={(e) => { setPage(1); setSortBy(e.target.value); }}
          className="text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg px-2 py-1.5 bg-white dark:bg-zinc-900 text-zinc-700 dark:text-zinc-300"
        >
          <option value="updatedAt">Son Güncellenen</option>
          <option value="wilsonScore">En Faydalı</option>
          <option value="viewCount">En Çok Görüntülenen</option>
        </select>

        {hasActiveFilters && (
          <button
            onClick={clearAllFilters}
            className="flex items-center gap-1 px-2 py-1.5 text-xs text-red-600 hover:bg-red-50 dark:hover:bg-red-950 rounded-lg transition-colors"
          >
            <X size={12} />
            Clear All
          </button>
        )}
      </div>

      {/* Active filter badges */}
      {hasActiveFilters && (
        <div className="flex flex-wrap items-center gap-1.5 mb-4">
          {statusFilter.map(s => (
            <span key={s} className="inline-flex items-center gap-1 px-2 py-0.5 text-xs rounded-full bg-amber-50 text-amber-700 dark:bg-amber-950 dark:text-amber-300">
              {s}
              <button onClick={() => { setPage(1); setStatusFilter(statusFilter.filter(v => v !== s)); }}><X size={10} /></button>
            </span>
          ))}
          {contentTypeFilter.map(ct => (
            <span key={ct} className="inline-flex items-center gap-1 px-2 py-0.5 text-xs rounded-full bg-purple-50 text-purple-700 dark:bg-purple-950 dark:text-purple-300">
              {contentTypes.find(c => c.value === ct)?.label || ct}
              <button onClick={() => { setPage(1); setContentTypeFilter(contentTypeFilter.filter(v => v !== ct)); }}><X size={10} /></button>
            </span>
          ))}
          {tagFilter.map(t => (
            <span key={t} className="inline-flex items-center gap-1 px-2 py-0.5 text-xs rounded-full bg-indigo-50 text-indigo-700 dark:bg-indigo-950 dark:text-indigo-300">
              {allTags.find(tag => tag.slug === t)?.name || t}
              <button onClick={() => { setPage(1); setTagFilter(tagFilter.filter(v => v !== t)); }}><X size={10} /></button>
            </span>
          ))}
          {dateFrom && (
            <span className="inline-flex items-center gap-1 px-2 py-0.5 text-xs rounded-full bg-green-50 text-green-700 dark:bg-green-950 dark:text-green-300">
              From: {dateFrom}
              <button onClick={() => { setPage(1); setDateFrom(""); }}><X size={10} /></button>
            </span>
          )}
          {dateTo && (
            <span className="inline-flex items-center gap-1 px-2 py-0.5 text-xs rounded-full bg-green-50 text-green-700 dark:bg-green-950 dark:text-green-300">
              To: {dateTo}
              <button onClick={() => { setPage(1); setDateTo(""); }}><X size={10} /></button>
            </span>
          )}
        </div>
      )}

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
                    <ContentTypeBadge contentType={article.contentType} />
                    {article.status !== "published" && (
                      <span className={`text-xs px-2 py-0.5 rounded-full ${statusColors[article.status] || ""}`}>
                        {article.status}
                      </span>
                    )}
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
                        <TagIcon size={12} className="text-zinc-400" />
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
