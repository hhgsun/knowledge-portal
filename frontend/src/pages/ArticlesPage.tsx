import { useEffect, useState, useCallback, useRef } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { PlusCircle, BookOpen, User, Key, Tag as TagIcon, ChevronLeft, ChevronRight, Eye, ThumbsUp, UserLockIcon, X, Filter, Calendar, ChevronDown, ArrowUpDown, RotateCcw, FilePenLine, CircleCheck, Archive } from "lucide-react";
import { useApi } from "../hooks/useApi";
import { useAuth } from "../contexts/AuthContext";
import { useLookups } from "../hooks/useLookups";
import { ContentTypeBadge } from "../components/ContentTypeBadge";
import { ArticleIndexStatusBadge } from "../components/ArticleIndexStatusBadge";
import { ArticleListSkeleton } from "../components/ui/skeleton";
import { TagSelector } from "../components/editor/tag-selector";
import { getColorClasses, getIconComponent } from "../lib/lookup-utils";
import type { ArticleListItem, Tag } from "../types/api";

const LIMIT = 20;
const STATUS_OPTIONS = [
  { value: "draft", label: "Taslak", icon: FilePenLine, color: "text-zinc-600 dark:text-zinc-300", bg: "bg-zinc-100 dark:bg-zinc-800" },
  { value: "published", label: "Yayında", icon: CircleCheck, color: "text-emerald-700 dark:text-emerald-300", bg: "bg-emerald-50 dark:bg-emerald-950/50" },
  { value: "archived", label: "Arşivlenmiş", icon: Archive, color: "text-rose-700 dark:text-rose-300", bg: "bg-rose-50 dark:bg-rose-950/50" },
];

function readFacetFilters(params: URLSearchParams): Record<string, string[]> {
  const result: Record<string, string[]> = {};
  for (const raw of params.getAll("facet")) {
    const separator = raw.indexOf(":");
    if (separator <= 0 || separator === raw.length - 1) continue;
    const category = raw.slice(0, separator);
    const value = raw.slice(separator + 1);
    result[category] = Array.from(new Set([...(result[category] ?? []), value]));
  }
  // Preserve old links while emitting only the generic facet contract going forward.
  const legacyContentTypes = params.get("contentType")?.split(",").filter(Boolean) ?? [];
  if (legacyContentTypes.length)
    result.content_type = Array.from(new Set([...(result.content_type ?? []), ...legacyContentTypes]));
  return result;
}

function facetFiltersEqual(left: Record<string, string[]>, right: Record<string, string[]>) {
  const keys = Array.from(new Set([...Object.keys(left), ...Object.keys(right)]));
  return keys.every(key => (left[key] ?? []).join("\u0000") === (right[key] ?? []).join("\u0000"));
}

function MultiSelectDropdown({ label, icon, options, selected, onChange, renderOption }: {
  label: string;
  icon?: React.ReactNode;
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
        type="button"
        onClick={() => setOpen(!open)}
        className={`flex h-8 items-center gap-1.5 rounded-lg border px-2.5 text-xs font-medium transition-colors ${selected.length > 0
          ? "bg-blue-50 border-blue-300 text-blue-700 dark:bg-blue-950 dark:border-blue-700 dark:text-blue-300"
          : "border-zinc-300 dark:border-zinc-700 text-zinc-600 dark:text-zinc-400 hover:bg-zinc-50 dark:hover:bg-zinc-800"
          }`}
      >
        {icon}
        {label}
        {selected.length > 0 && (
          <span className="ml-1 px-1.5 py-0.5 text-xs bg-blue-200 dark:bg-blue-800 rounded-full">{selected.length}</span>
        )}
        <ChevronDown size={14} />
      </button>
      {open && (
        <div className="absolute z-50 mt-1 w-60 max-h-64 overflow-y-auto bg-white dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-700 rounded-xl p-1 shadow-xl shadow-zinc-950/10">
          {options.map((opt) => (
            <label
              key={opt.value}
              className="flex cursor-pointer items-center gap-2 rounded-lg px-2.5 py-2 text-xs hover:bg-zinc-50 dark:hover:bg-zinc-800"
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

function DateRangeDropdown({ from, to, onFromChange, onToChange }: {
  from: string;
  to: string;
  onFromChange: (value: string) => void;
  onToChange: (value: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const selectedCount = Number(Boolean(from)) + Number(Boolean(to));

  useEffect(() => {
    const handler = (event: MouseEvent) => {
      if (ref.current && !ref.current.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, []);

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        onClick={() => setOpen(current => !current)}
        className={`flex h-8 items-center gap-1.5 rounded-lg border px-2.5 text-xs font-medium transition-colors ${selectedCount
          ? "border-blue-300 bg-blue-50 text-blue-700 dark:border-blue-700 dark:bg-blue-950 dark:text-blue-300"
          : "border-zinc-300 text-zinc-600 hover:bg-zinc-50 dark:border-zinc-700 dark:text-zinc-400 dark:hover:bg-zinc-800"}`}
      >
        <Calendar size={13} /> Tarih
        {selectedCount > 0 && <span className="rounded-full bg-blue-200 px-1.5 py-0.5 text-[10px] dark:bg-blue-800">{selectedCount}</span>}
        <ChevronDown size={13} className={`transition-transform ${open ? "rotate-180" : ""}`} />
      </button>
      {open && (
        <div className="absolute left-0 z-50 mt-1 w-64 rounded-xl border border-zinc-200 bg-white p-3 shadow-xl shadow-zinc-950/10 dark:border-zinc-700 dark:bg-zinc-900">
          <div className="grid grid-cols-2 gap-2">
            <label className="text-[11px] font-medium text-zinc-500">Başlangıç
              <input type="date" value={from} onChange={event => onFromChange(event.target.value)} className="mt-1 w-full rounded-lg border border-zinc-300 bg-white px-2 py-1.5 text-xs text-zinc-700 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-200" />
            </label>
            <label className="text-[11px] font-medium text-zinc-500">Bitiş
              <input type="date" value={to} onChange={event => onToChange(event.target.value)} className="mt-1 w-full rounded-lg border border-zinc-300 bg-white px-2 py-1.5 text-xs text-zinc-700 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-200" />
            </label>
          </div>
          {selectedCount > 0 && (
            <button type="button" onClick={() => { onFromChange(""); onToChange(""); }} className="mt-2 inline-flex items-center gap-1 text-[11px] font-medium text-rose-600 hover:underline">
              <RotateCcw size={11} /> Tarihi temizle
            </button>
          )}
        </div>
      )}
    </div>
  );
}

export default function ArticlesPage() {
  const { fetchWithAuth } = useApi();
  const { user } = useAuth();
  const { categories, lookups } = useLookups();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const [articles, setArticles] = useState<ArticleListItem[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(() => Number(searchParams.get("page")) || 1);
  const [loading, setLoading] = useState(true);
  const [statusFilter, setStatusFilter] = useState<string[]>(() => searchParams.get("status")?.split(",").filter(Boolean) || []);
  const [facetFilters, setFacetFilters] = useState<Record<string, string[]>>(() => readFacetFilters(searchParams));
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
      .catch(() => { });
  }, [fetchWithAuth]);

  const syncSearchParams = useCallback((p: number, status: string[], facets: Record<string, string[]>, tags: string[], mine: boolean, sort: string, df: string, dt: string) => {
    const params = new URLSearchParams();
    if (p > 1) params.set("page", String(p));
    if (status.length) params.set("status", status.join(","));
    Object.entries(facets).forEach(([category, values]) =>
      values.forEach(value => params.append("facet", `${category}:${value}`)));
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
    syncSearchParams(page, statusFilter, facetFilters, tagFilter, mineFilter, sortBy, dateFrom, dateTo);
  }, [page, statusFilter, facetFilters, tagFilter, mineFilter, sortBy, dateFrom, dateTo, syncSearchParams]);

  // Re-sync filter state when the URL changes while already on this page
  // (e.g. clicking a tag/content-type badge on a card, or browser back/forward)
  useEffect(() => {
    const eq = (a: string[], b: string[]) => a.length === b.length && a.every((v, i) => v === b[i]);
    const urlStatus = searchParams.get("status")?.split(",").filter(Boolean) || [];
    const urlFacets = readFacetFilters(searchParams);
    const urlTags = searchParams.getAll("tag").flatMap(v => v.split(",")).filter(Boolean);
    setPage(Number(searchParams.get("page")) || 1);
    setStatusFilter(prev => (eq(prev, urlStatus) ? prev : urlStatus));
    setFacetFilters(prev => (facetFiltersEqual(prev, urlFacets) ? prev : urlFacets));
    setTagFilter(prev => (eq(prev, urlTags) ? prev : urlTags));
    setMineFilter(searchParams.get("mine") === "true");
    setSortBy(searchParams.get("sort") || "updatedAt");
    setDateFrom(searchParams.get("dateFrom") || "");
    setDateTo(searchParams.get("dateTo") || "");
  }, [searchParams]);

  useEffect(() => {
    setLoading(true);
    const params = new URLSearchParams();
    params.set("page", String(page));
    params.set("limit", String(LIMIT));
    if (statusFilter.length) params.set("status", statusFilter.join(","));
    Object.entries(facetFilters).forEach(([category, values]) =>
      values.forEach(value => params.append("facet", `${category}:${value}`)));
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
  }, [fetchWithAuth, statusFilter, facetFilters, tagFilter, mineFilter, page, sortBy, dateFrom, dateTo]);

  const statusColors: Record<string, string> = {
    draft: "bg-zinc-100 text-zinc-600",
    published: "bg-green-100 text-green-700",
    archived: "bg-red-100 text-red-700",
  };

  const hasActiveFilters = statusFilter.length > 0 || Object.values(facetFilters).some(values => values.length > 0) || tagFilter.length > 0 || dateFrom || dateTo || mineFilter;
  const activeFilterCount = statusFilter.length
    + Object.values(facetFilters).reduce((sum, values) => sum + values.length, 0)
    + tagFilter.length
    + Number(Boolean(dateFrom))
    + Number(Boolean(dateTo))
    + Number(mineFilter);

  const clearAllFilters = () => {
    setPage(1);
    setStatusFilter([]);
    setFacetFilters({});
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

      <section className="mb-4 rounded-xl border border-zinc-200 bg-white shadow-sm shadow-zinc-950/[0.02] dark:border-zinc-800 dark:bg-zinc-950">
        <div className="flex min-h-11 flex-wrap items-center gap-2 px-3 py-2">
          <div className={`inline-flex h-8 items-center gap-2 rounded-lg px-2.5 text-xs font-semibold ${hasActiveFilters
            ? "bg-blue-50 text-blue-700 dark:bg-blue-950/60 dark:text-blue-300"
            : "text-zinc-600 dark:text-zinc-300"}`}>
            <Filter size={14} /> Filtreler
            {activeFilterCount > 0 && <span className="rounded-full bg-blue-600 px-1.5 py-0.5 text-[10px] leading-none text-white">{activeFilterCount}</span>}
          </div>
          <span className="text-xs text-zinc-400">{loading ? "Yükleniyor…" : `${total} makale`}</span>
          <div className="ml-auto flex items-center gap-2">
            {hasActiveFilters && (
              <button type="button" onClick={clearAllFilters} className="inline-flex h-8 items-center gap-1 rounded-lg px-2 text-xs font-medium text-rose-600 hover:bg-rose-50 dark:hover:bg-rose-950/40">
                <RotateCcw size={12} /> <span className="hidden sm:inline">Temizle</span>
              </button>
            )}
            <div className="relative flex items-center">
              <ArrowUpDown size={13} className="pointer-events-none absolute left-2.5 text-zinc-400" />
              <select
                value={sortBy}
                onChange={(event) => { setPage(1); setSortBy(event.target.value); }}
                aria-label="Makaleleri sırala"
                className="h-8 rounded-lg border border-zinc-300 bg-white pl-7 pr-7 text-xs font-medium text-zinc-700 dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-300"
              >
                <option value="updatedAt">Son güncellenen</option>
                <option value="wilsonScore">En faydalı</option>
                <option value="viewCount">En çok görüntülenen</option>
              </select>
            </div>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-2 border-t border-zinc-100 bg-zinc-50/60 px-3 py-3 dark:border-zinc-800 dark:bg-zinc-900/30">

            {isApprover && (
              <MultiSelectDropdown
                label="Durum"
                icon={<CircleCheck size={13} />}
                options={STATUS_OPTIONS.map(({ value, label }) => ({ value, label }))}
                selected={statusFilter}
                onChange={(values) => { setPage(1); setStatusFilter(values); }}
                renderOption={(option) => {
                  const status = STATUS_OPTIONS.find(item => item.value === option.value)!;
                  const StatusIcon = status.icon;
                  return <span className={`flex items-center gap-2 ${status.color}`}><span className={`flex h-6 w-6 items-center justify-center rounded-md ${status.bg}`}><StatusIcon size={13} /></span>{status.label}</span>;
                }}
              />
            )}

            {categories.filter(category => category.isActive).map(category => (
              <MultiSelectDropdown
                key={category.id}
                label={category.label}
                options={lookups.filter(value => value.category === category.key && value.isActive)
                  .map(value => ({ value: value.value, label: value.label }))}
                selected={facetFilters[category.key] ?? []}
                onChange={(values) => {
                  setPage(1);
                  setFacetFilters(previous => ({ ...previous, [category.key]: values }));
                }}
                renderOption={(option) => {
                  const lookup = lookups.find(item => item.category === category.key && item.value === option.value);
                  const colors = getColorClasses(lookup?.color);
                  const OptionIcon = getIconComponent(lookup?.icon);
                  return <span style={colors.textStyle} className={`flex items-center gap-2 ${colors.text}`}><span style={{ ...colors.bgStyle, ...colors.textStyle }} className={`flex h-6 w-6 items-center justify-center rounded-md ${colors.bg} ${colors.text}`}><OptionIcon size={13} /></span>{option.label}</span>;
                }}
              />
            ))}

            <TagSelector
              selectedTags={tagFilter}
              onChange={(v) => { setPage(1); setTagFilter(v); }}
              valueField="slug"
              allowCreate={false}
              hideSelectedTags={true}
            />

            <DateRangeDropdown
              from={dateFrom}
              to={dateTo}
              onFromChange={(value) => { setPage(1); setDateFrom(value); }}
              onToChange={(value) => { setPage(1); setDateTo(value); }}
            />

            <button
              type="button"
              onClick={() => { setPage(1); setMineFilter(!mineFilter); }}
              className={`flex h-8 items-center gap-1.5 rounded-lg border px-2.5 text-xs font-medium transition-colors ${mineFilter
                ? "border-violet-300 bg-violet-50 text-violet-700 dark:border-violet-700 dark:bg-violet-950/50 dark:text-violet-300"
                : "border-zinc-300 text-zinc-600 hover:bg-zinc-50 dark:border-zinc-700 dark:text-zinc-400 dark:hover:bg-zinc-800"
                }`}
            >
              <UserLockIcon size={13} />
              Makalelerim
            </button>

        </div>

        {/* Active filter badges */}
        {hasActiveFilters && (
          <div className="flex flex-wrap items-center gap-1.5 border-t border-zinc-100 px-3 py-2 dark:border-zinc-800">
            {statusFilter.map(statusValue => {
              const status = STATUS_OPTIONS.find(item => item.value === statusValue);
              const StatusIcon = status?.icon ?? CircleCheck;
              return (
                <span key={statusValue} className={`inline-flex items-center gap-1 rounded-full px-2 py-1 text-[11px] font-medium ${status?.bg ?? "bg-zinc-100"} ${status?.color ?? "text-zinc-600"}`}>
                  <StatusIcon size={11} /> {status?.label ?? statusValue}
                  <button type="button" onClick={() => { setPage(1); setStatusFilter(current => current.filter(value => value !== statusValue)); }} aria-label={`${status?.label ?? statusValue} filtresini kaldır`} className="ml-0.5 rounded-full hover:text-rose-600"><X size={11} /></button>
                </span>
              );
            })}
            {Object.entries(facetFilters).flatMap(([categoryKey, values]) => values.map(value => {
              const lookup = lookups.find(item => item.category === categoryKey && item.value === value);
              const colors = getColorClasses(lookup?.color);
              const LookupIcon = getIconComponent(lookup?.icon);
              const categoryLabel = categories.find(category => category.key === categoryKey)?.label ?? categoryKey;
              const label = `${lookup?.label ?? value}`;
              return (
                <span key={`${categoryKey}:${value}`} style={{ ...colors.bgStyle, ...colors.textStyle }} className={`inline-flex items-center gap-1 rounded-full px-2 py-1 text-[11px] font-medium ${colors.bg} ${colors.text}`} title={categoryLabel + ': ' + label}>
                  <LookupIcon size={11} /> {label}
                  <button type="button" onClick={() => { setPage(1); setFacetFilters(previous => ({ ...previous, [categoryKey]: (previous[categoryKey] ?? []).filter(item => item !== value) })); }} aria-label={`${label} filtresini kaldır`} className="ml-0.5 rounded-full hover:text-rose-600"><X size={11} /></button>
                </span>
              );
            }))}
            {tagFilter.map(t => (
              <span key={t} className="inline-flex items-center gap-1 rounded-full bg-indigo-50 px-2 py-1 text-[11px] font-medium text-indigo-700 dark:bg-indigo-950/50 dark:text-indigo-300">
                <TagIcon size={11} /> {allTags.find(tag => tag.slug === t)?.name || t}
                <button type="button" onClick={() => { setPage(1); setTagFilter(tagFilter.filter(v => v !== t)); }} aria-label={`${t} etiket filtresini kaldır`} className="ml-0.5 rounded-full hover:text-rose-600"><X size={11} /></button>
              </span>
            ))}
            {(dateFrom || dateTo) && (
              <span className="inline-flex items-center gap-1 rounded-full bg-emerald-50 px-2 py-1 text-[11px] font-medium text-emerald-700 dark:bg-emerald-950/50 dark:text-emerald-300">
                <Calendar size={11} /> {dateFrom || "…"} — {dateTo || "…"}
                <button type="button" onClick={() => { setPage(1); setDateFrom(""); setDateTo(""); }} aria-label="Tarih filtresini kaldır" className="ml-0.5 rounded-full hover:text-rose-600"><X size={11} /></button>
              </span>
            )}
            {mineFilter && (
              <span className="inline-flex items-center gap-1 rounded-full bg-violet-50 px-2 py-1 text-[11px] font-medium text-violet-700 dark:bg-violet-950/50 dark:text-violet-300">
                <UserLockIcon size={11} /> Makalelerim
                <button type="button" onClick={() => { setPage(1); setMineFilter(false); }} aria-label="Makalelerim filtresini kaldır" className="ml-0.5 rounded-full hover:text-rose-600"><X size={11} /></button>
              </span>
            )}
          </div>
        )}
      </section>

      {loading ? (
        <ArticleListSkeleton />
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
                    <ContentTypeBadge contentType={article.contentType} clickable />
                    {Object.entries(article.classifications ?? {})
                      .filter(([category]) => category !== "content_type")
                      .flatMap(([categoryKey, values]) => values.map(value => (
                        <span key={`${categoryKey}:${value}`} className="text-xs px-2 py-0.5 rounded-full bg-sky-50 text-sky-700 dark:bg-sky-950 dark:text-sky-300">
                          {categories.find(category => category.key === categoryKey)?.label ?? categoryKey}: {lookups.find(lookup => lookup.category === categoryKey && lookup.value === value)?.label ?? value}
                        </span>
                      )))}
                    {article.status !== "published" && (
                      <span className={`text-xs px-2 py-0.5 rounded-full ${statusColors[article.status] || ""}`}>
                        {article.status}
                      </span>
                    )}
                    {isApprover && <ArticleIndexStatusBadge status={article.indexingStatus} />}
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
                            onClick={(e) => {
                              e.preventDefault();
                              e.stopPropagation();
                              navigate(`/articles?tag=${encodeURIComponent(tag.slug)}`);
                            }}
                            className="text-xs px-2 py-0.5 rounded-full bg-indigo-50 text-indigo-600 dark:bg-indigo-950 dark:text-indigo-400 cursor-pointer hover:bg-indigo-100 dark:hover:bg-indigo-900 transition-colors"
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
