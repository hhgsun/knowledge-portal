import { useState, useEffect, useRef, useCallback } from "react";
import { Link, useSearchParams, useNavigate } from "react-router-dom";
import { Search as SearchIcon, Sparkles, Bot, FileText, Zap, Tag, AlertTriangle, User, Hash, Eye, ThumbsUp, Key, Clock, X, Trash2, ChevronLeft, ChevronRight } from "lucide-react";
import { cn } from "../lib/utils";
import { useApi } from "../hooks/useApi";
import { useLookups } from "../hooks/useLookups";
import { useSearchHistory } from "../hooks/useNetworkStatus";
import { ContentTypeBadge } from "../components/ContentTypeBadge";
import { toast } from "sonner";
import type { SearchResult, TagWithCount, LookupValue, SearchIndexCoverage } from "../types/api";
import { useCapabilities } from "../contexts/CapabilitiesContext";

type SearchType = "hybrid" | "fulltext" | "semantic";
type SuggestionType = "tag" | "author" | "contentType";
interface AuthorItem { id: string; name: string; slug: string; }

function getIndexCoverageMessage(coverage: SearchIndexCoverage) {
  const count = coverage.relevantPending;
  if (coverage.mode === "fulltext") {
    return `${count} makalenin tam metin indeksi güncel değil. Sonuçlar geçici olarak eksik olabilir.`;
  }
  if (coverage.mode === "semantic") {
    return `${count} makalenin semantic arama indeksi güncel değil. Sonuçlar geçici olarak eksik veya eski olabilir.`;
  }
  if (coverage.fullTextPending > 0 && coverage.semanticPending > 0) {
    return `${count} makalenin tam metin ve/veya semantic arama indeksi güncel değil. Sonuçlar geçici olarak eksik veya eski olabilir.`;
  }
  if (coverage.fullTextPending > 0) {
    return `${count} makalenin tam metin indeksi güncel değil. Sonuçlar geçici olarak eksik olabilir.`;
  }
  return `${count} makalenin semantic arama indeksi güncel değil. Sonuçlar geçici olarak eksik veya eski olabilir.`;
}

export default function SearchPage() {
  const { fetchWithAuth } = useApi();
  const { assistantEnabled } = useCapabilities();
  const { contentTypes, categories, lookups } = useLookups();
  const { history, addToHistory, removeFromHistory, clearHistory } = useSearchHistory();
  const [searchParams, setSearchParams] = useSearchParams();
  const initialQuery = searchParams.get("q") || "";
  const requestedType = searchParams.get("type");
  const initialType: SearchType = requestedType === "fulltext" || requestedType === "semantic"
    ? requestedType : "hybrid";
  const [query, setQuery] = useState(initialQuery);
  const [searchType, setSearchType] = useState<SearchType>(initialType);
  const [results, setResults] = useState<SearchResult[]>([]);
  const [loading, setLoading] = useState(false);
  const [searched, setSearched] = useState(false);
  const [responseTime, setResponseTime] = useState<number | null>(null);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [activeTags, setActiveTags] = useState<string[]>([]);
  const [facetFilters, setFacetFilters] = useState<Record<string, string[]>>(() =>
    searchParams.getAll("facet").reduce<Record<string, string[]>>((result, item) => {
      const [category, value] = item.split(":", 2);
      if (category && value) result[category] = [...(result[category] ?? []), value];
      return result;
    }, {}));
  const [searchQueryId, setSearchQueryId] = useState<string | null>(null);
  const [indexCoverage, setIndexCoverage] = useState<SearchIndexCoverage | null>(null);
  const searchInFlightRef = useRef(false);
  const [warning, setWarning] = useState<string | null>(null);
  const [showHistory, setShowHistory] = useState(false);
  const [selectedHistoryIdx, setSelectedHistoryIdx] = useState(-1);
  const navigate = useNavigate();

  // Autocomplete data
  const [availableTags, setAvailableTags] = useState<TagWithCount[]>([]);
  const [authors, setAuthors] = useState<AuthorItem[]>([]);
  const [showSuggestions, setShowSuggestions] = useState(false);
  const [suggestionType, setSuggestionType] = useState<SuggestionType>("tag");
  const [filteredSuggestions, setFilteredSuggestions] = useState<{ id: string; label: string; value: string; extra?: string }[]>([]);
  const inputRef = useRef<HTMLInputElement>(null);
  const suggestionsRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    fetchWithAuth("/api/tags")
      .then((r) => r.json())
      .then((data) => setAvailableTags(data))
      .catch(() => { toast.error("Failed to load tags"); });
  }, [fetchWithAuth]);

  useEffect(() => {
    fetchWithAuth("/api/search/authors")
      .then((r) => r.json())
      .then((data) => { if (Array.isArray(data)) setAuthors(data); })
      .catch(() => { });
  }, [fetchWithAuth]);

  // Autocomplete logic for @user, #tag, ##contentType
  useEffect(() => {
    // Check for ## first (contentType), then # (tag), then @ (user)
    const ctMatch = query.match(/##(\S*)$/);
    if (ctMatch) {
      const partial = ctMatch[1].toLowerCase();
      const existing = [...query.matchAll(/##(\S+)/g)].map(m => m[1].toLowerCase()).filter(t => t !== partial);
      const filtered = contentTypes
        .filter((ct: LookupValue) => (ct.value.includes(partial) || ct.label.toLowerCase().includes(partial)) && !existing.includes(ct.value))
        .map((ct: LookupValue) => ({ id: ct.id, label: ct.label, value: ct.value }));
      setFilteredSuggestions(filtered);
      setSuggestionType("contentType");
      setShowSuggestions(filtered.length > 0);
      return;
    }

    const tagMatch = query.match(/(?<![#])#(\S*)$/);
    if (tagMatch) {
      const partial = tagMatch[1].toLowerCase();
      const existing = [...query.matchAll(/(?<![#])#(\S+)/g)].map(m => m[1].toLowerCase()).filter(t => t !== partial);
      const filtered = availableTags
        .filter((t) => (t.slug.includes(partial) || t.name.toLowerCase().includes(partial)) && !existing.includes(t.slug))
        .map((t) => ({ id: t.id, label: t.name, value: t.slug, extra: `${t.articleCount}` }));
      setFilteredSuggestions(filtered);
      setSuggestionType("tag");
      setShowSuggestions(filtered.length > 0);
      return;
    }

    const authorMatch = query.match(/@(\S*)$/);
    if (authorMatch) {
      const partial = authorMatch[1].toLowerCase();
      const existing = [...query.matchAll(/@(\S+)/g)].map(m => m[1].toLowerCase()).filter(t => t !== partial);
      const filtered = authors
        .filter((a) => (a.slug.includes(partial) || a.name.toLowerCase().includes(partial)) && !existing.includes(a.slug))
        .map((a) => ({ id: a.id, label: a.name, value: a.slug }));
      setFilteredSuggestions(filtered);
      setSuggestionType("author");
      setShowSuggestions(filtered.length > 0);
      return;
    }

    setShowSuggestions(false);
  }, [query, availableTags, authors, contentTypes]);

  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (suggestionsRef.current && !suggestionsRef.current.contains(e.target as Node)) {
        setShowSuggestions(false);
      }
      // Also close history if clicking outside
      if (inputRef.current && !inputRef.current.contains(e.target as Node) &&
        suggestionsRef.current && !suggestionsRef.current.contains(e.target as Node)) {
        setShowHistory(false);
      }
    };
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  const selectSuggestion = (value: string) => {
    let newQuery: string;
    if (suggestionType === "contentType") {
      newQuery = query.replace(/##\S*$/, `##${value} `);
    } else if (suggestionType === "tag") {
      newQuery = query.replace(/(?<![#])#\S*$/, `#${value} `);
    } else {
      newQuery = query.replace(/@\S*$/, `@${value} `);
    }
    setQuery(newQuery);
    setShowSuggestions(false);
    inputRef.current?.focus();
  };

  const handleSearch = async (e?: React.FormEvent, pageArg = 1) => {
    e?.preventDefault();
    if (!query.trim() || searchInFlightRef.current) return;

    searchInFlightRef.current = true;

    addToHistory(query.trim());
    setShowHistory(false);

    // Sync URL params
    const params = new URLSearchParams();
    params.set("q", query.trim());
    if (searchType !== "hybrid") params.set("type", searchType);
    Object.entries(facetFilters).forEach(([category, values]) =>
      values.forEach(value => params.append("facet", `${category}:${value}`)));
    setSearchParams(params, { replace: true });

    setLoading(true);
    setSearched(true);
    setResults([]);
    setActiveTags([]);
    setSearchQueryId(null);
    setIndexCoverage(null);
    setWarning(null);

    try {
      const facetQuery = Object.entries(facetFilters).flatMap(([category, values]) =>
        values.map(value => `facet=${encodeURIComponent(`${category}:${value}`)}`)).join("&");
      const res = await fetchWithAuth(
        `/api/search?q=${encodeURIComponent(query.trim())}&type=${searchType}&page=${pageArg}${facetQuery ? `&${facetQuery}` : ""}`
      );
      const data = await res.json();

      if (data.tags) setActiveTags(data.tags);
      if (data.searchQueryId) setSearchQueryId(data.searchQueryId);
      setIndexCoverage(data.indexCoverage ?? null);
      if (data.warning) setWarning(data.warning);

      setResults(data.results || []);
      setTotal(data.total ?? (data.results || []).length);
      setPage(data.page ?? 1);
      setTotalPages(data.totalPages ?? 1);
      setResponseTime(data.responseTimeMs || null);
    } finally {
      searchInFlightRef.current = false;
      setLoading(false);
    }
  };

  const goToPage = (p: number) => {
    handleSearch(undefined, p);
    window.scrollTo({ top: 0 });
  };

  // Auto-search on mount if query param exists
  const hasAutoSearched = useRef(false);
  useEffect(() => {
    if (initialQuery && !hasAutoSearched.current) {
      hasAutoSearched.current = true;
      handleSearch();
    }
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // Fire-and-forget click analytics; navigation is handled by the result <Link>
  const trackClick = useCallback((articleId: string) => {
    if (searchQueryId) {
      fetchWithAuth("/api/search/click", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ searchQueryId, articleId }),
      }).catch(() => { });
    }
  }, [searchQueryId, fetchWithAuth]);

  return (
    <div className="max-w-5xl mx-auto">
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-zinc-900 dark:text-zinc-100">Doküman Ara</h1>
        <p className="mb-4 mt-1 text-sm text-zinc-500">Makale ve ekleri anahtar kelime, anlam benzerliği ve metadata filtreleriyle bulun.</p>
        <div className="flex gap-1 mb-3 p-1 bg-zinc-100 dark:bg-zinc-800 rounded-lg w-fit" role="tablist" aria-label="Search type">
          <SearchTypeTab active={searchType === "hybrid"} onClick={() => setSearchType("hybrid")} icon={<Zap size={14} />} label="Hybrid" />
          <SearchTypeTab active={searchType === "fulltext"} onClick={() => setSearchType("fulltext")} icon={<FileText size={14} />} label="Full-Text" />
          <SearchTypeTab active={searchType === "semantic"} onClick={() => setSearchType("semantic")} icon={<Sparkles size={14} />} label="Semantic" />
        </div>
        <p id="search-help" className="sr-only">Use @ for author filter, # for tag filter, ## for content type filter. Press Enter to search.</p>
        <form onSubmit={handleSearch} className="relative" role="search" aria-label="Search knowledge base">
          <SearchIcon size={18} className="absolute left-4 top-1/2 -translate-y-1/2 text-zinc-400" aria-hidden="true" />
          <input
            ref={inputRef}
            type="search"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onFocus={() => { if (!query && history.length > 0 && !showSuggestions) setShowHistory(true); }}
            onKeyDown={(e) => {
              if (showHistory && history.length > 0) {
                if (e.key === "ArrowDown") {
                  e.preventDefault();
                  setSelectedHistoryIdx((prev) => Math.min(prev + 1, history.length - 1));
                } else if (e.key === "ArrowUp") {
                  e.preventDefault();
                  setSelectedHistoryIdx((prev) => Math.max(prev - 1, -1));
                } else if (e.key === "Enter" && selectedHistoryIdx >= 0) {
                  e.preventDefault();
                  setQuery(history[selectedHistoryIdx]);
                  setShowHistory(false);
                  setSelectedHistoryIdx(-1);
                } else if (e.key === "Escape") {
                  setShowHistory(false);
                  setSelectedHistoryIdx(-1);
                }
              }
            }}
            placeholder="Search... (@user #tag ##type)"
            aria-label="Search articles"
            aria-describedby="search-help"
            aria-autocomplete="list"
            aria-expanded={showSuggestions || showHistory}
            className="w-full pl-11 pr-4 py-3 text-base bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded-xl focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
            autoFocus
          />
          <button
            type="submit"
            disabled={loading || !query.trim()}
            aria-label="Search"
            aria-busy={loading}
            className="absolute right-2 top-1/2 -translate-y-1/2 px-4 py-1.5 bg-blue-600 hover:bg-blue-700 text-white text-sm rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50 disabled:hover:bg-blue-600"
          >
            {loading ? "Bekleyin..." : "Search"}
          </button>

          {/* Arama geçmişi dropdown */}
          {showHistory && !showSuggestions && history.length > 0 && (
            <div ref={suggestionsRef} className="absolute top-full left-0 right-0 mt-1 bg-white dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-700 rounded-xl shadow-lg z-50 max-h-60 overflow-y-auto" role="listbox" aria-label="Arama geçmişi">
              <div className="flex items-center justify-between px-3 py-2 border-b border-zinc-100 dark:border-zinc-800">
                <span className="text-xs text-zinc-400 flex items-center gap-1">
                  <Clock size={12} aria-hidden="true" />
                  Recent searches
                </span>
                <button
                  type="button"
                  onClick={(e) => { e.stopPropagation(); clearHistory(); setShowHistory(false); }}
                  className="text-xs text-zinc-400 hover:text-red-500 flex items-center gap-1"
                  aria-label="Tüm arama geçmişini temizle"
                >
                  <Trash2 size={12} aria-hidden="true" />
                  Clear
                </button>
              </div>
              {history.map((item, idx) => (
                <div
                  key={item}
                  role="option"
                  aria-selected={selectedHistoryIdx === idx}
                  className={cn(
                    "flex items-center gap-2 px-3 py-2 text-sm cursor-pointer transition-colors group",
                    selectedHistoryIdx === idx ? "bg-zinc-100 dark:bg-zinc-800" : "hover:bg-zinc-50 dark:hover:bg-zinc-800/50"
                  )}
                  onClick={() => { setQuery(item); setShowHistory(false); }}
                >
                  <Clock size={14} className="text-zinc-400 shrink-0" aria-hidden="true" />
                  <span className="text-zinc-700 dark:text-zinc-300 flex-1 truncate">{item}</span>
                  <button
                    type="button"
                    onClick={(e) => { e.stopPropagation(); removeFromHistory(item); }}
                    className="opacity-0 group-hover:opacity-100 p-1 rounded hover:bg-zinc-200 dark:hover:bg-zinc-700 text-zinc-400 hover:text-red-500 transition-opacity"
                    aria-label={`Remove "${item}" from history`}
                  >
                    <X size={12} aria-hidden="true" />
                  </button>
                </div>
              ))}
            </div>
          )}

          {showSuggestions && (
            <div ref={suggestionsRef} className="absolute top-full left-0 right-0 mt-1 bg-white dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-700 rounded-xl shadow-lg z-50 max-h-60 overflow-y-auto" role="listbox" aria-label="Otomatik tamamlama önerileri">
              <div className="px-3 py-2 text-xs text-zinc-400 border-b border-zinc-100 dark:border-zinc-800">
                {suggestionType === "tag" && "Etiket seç (#)"}
                {suggestionType === "author" && "Yazar seç (@)"}
                {suggestionType === "contentType" && "İçerik tipi seç (##)"}
              </div>
              {filteredSuggestions.map((item) => (
                <button
                  key={item.id}
                  type="button"
                  role="option"
                  onClick={() => selectSuggestion(item.value)}
                  className="w-full flex items-center gap-2 px-3 py-2 text-sm text-left hover:bg-zinc-100 dark:hover:bg-zinc-800 transition-colors"
                >
                  {suggestionType === "tag" && <Hash size={14} className="text-emerald-500 shrink-0" aria-hidden="true" />}
                  {suggestionType === "author" && <User size={14} className="text-violet-500 shrink-0" aria-hidden="true" />}
                  {suggestionType === "contentType" && <FileText size={14} className="text-orange-500 shrink-0" aria-hidden="true" />}
                  <span className="text-zinc-900 dark:text-zinc-100">{item.label}</span>
                  {item.extra && <span className="ml-auto text-xs text-zinc-400">{item.extra} article{item.extra !== "1" ? "s" : ""}</span>}
                </button>
              ))}
            </div>
          )}
        </form>
        {categories.some(category => category.isActive && category.key !== "content_type") && (
          <div className="mt-3 flex flex-wrap gap-3">
            {categories.filter(category => category.isActive && category.key !== "content_type" && category.ragBehavior !== "none")
              .map(category => (
                <label key={category.id} className="text-xs text-zinc-500">
                  <span className="mb-1 block font-medium">{category.label}</span>
                  <select
                    multiple={category.cardinality === "multiple"}
                    value={facetFilters[category.key] ?? []}
                    onChange={(event) => setFacetFilters(previous => ({ ...previous,
                      [category.key]: Array.from(event.target.selectedOptions, option => option.value).filter(Boolean),
                    }))}
                    className="min-w-40 rounded-lg border border-zinc-300 bg-white px-2 py-1.5 text-sm dark:border-zinc-700 dark:bg-zinc-900"
                  >
                    {category.cardinality === "single" && <option value="">Tümü</option>}
                    {lookups.filter(value => value.category === category.key && value.isActive).map(value => (
                      <option key={value.id} value={value.value}>{value.label}</option>
                    ))}
                  </select>
                </label>
              ))}
          </div>
        )}
        {assistantEnabled && query.trim() && (
          <div className="mt-3 flex flex-col gap-2 rounded-xl border border-blue-100 bg-blue-50/70 px-3 py-2.5 text-sm sm:flex-row sm:items-center sm:justify-between dark:border-blue-900 dark:bg-blue-950/30">
            <span className="text-blue-700 dark:text-blue-300">Doküman listesi yerine kaynaklara dayalı bir açıklama mı istiyorsunuz?</span>
            <Link
              to={`/assistant?q=${encodeURIComponent(query.trim())}`}
              className="inline-flex shrink-0 items-center gap-1.5 font-medium text-blue-700 hover:underline dark:text-blue-300"
            >
              <Bot size={15} />
              Kanıtlı yanıt al
            </Link>
          </div>
        )}
      </div>

      {loading ? (
        <div aria-live="polite" aria-busy="true" className="space-y-3">
          {Array.from({ length: 4 }).map((_, i) => (
              <div key={i} className="p-4 border border-zinc-200 dark:border-zinc-800 rounded-xl animate-pulse">
                <div className="h-5 w-2/3 bg-zinc-200 dark:bg-zinc-700 rounded mb-2" />
                <div className="h-4 w-full bg-zinc-100 dark:bg-zinc-800 rounded mb-2" />
                <div className="flex items-center gap-2">
                  <div className="h-4 w-16 bg-zinc-100 dark:bg-zinc-800 rounded" />
                  <div className="h-4 w-10 bg-zinc-100 dark:bg-zinc-800 rounded" />
                  <div className="h-4 w-12 bg-zinc-100 dark:bg-zinc-800 rounded" />
                </div>
              </div>
            ))}
          <p className="sr-only">Searching...</p>
        </div>
      ) : searched ? (
        <div aria-live="polite" aria-atomic="true">
          {indexCoverage && indexCoverage.relevantPending > 0 && (
            <div className="flex items-center gap-2 mb-4 p-3 bg-amber-50 dark:bg-amber-950 border border-amber-200 dark:border-amber-800 rounded-lg text-sm text-amber-700 dark:text-amber-300">
              <AlertTriangle size={16} />
              <span>{getIndexCoverageMessage(indexCoverage)}</span>
            </div>
          )}
          {warning && (
            <div className="flex items-center gap-2 mb-4 p-3 bg-zinc-100 dark:bg-zinc-800 border border-zinc-300 dark:border-zinc-700 rounded-lg text-sm text-zinc-600 dark:text-zinc-400">
              <AlertTriangle size={16} />
              <span>{warning}</span>
            </div>
          )}

          <div>
              <div className="flex items-center justify-between mb-4">
                <p className="text-sm text-zinc-500">
                  {total} result{total !== 1 ? "s" : ""}
                  {activeTags.length > 0 && activeTags.map((tag) => (
                    <span key={tag} className="inline-flex items-center gap-1 ml-2 px-2 py-0.5 bg-blue-100 dark:bg-blue-900 text-blue-700 dark:text-blue-300 rounded-full text-xs">
                      <Tag size={10} />
                      {tag}
                    </span>
                  ))}
                  {activeTags.length === 0 && <> for &ldquo;{query}&rdquo;</>}
                  {responseTime !== null && <span className="ml-1">({responseTime}ms)</span>}
                </p>
              </div>

              {results.length === 0 ? (
                <div className="text-center py-8 border border-dashed border-zinc-300 dark:border-zinc-700 rounded-xl">
                  <p className="text-zinc-500">Sonuç bulunamadı</p>
                  <p className="text-sm text-zinc-400 mt-1">Farklı anahtar kelimeler veya daha geniş bir arama modu deneyin.</p>
                </div>
              ) : (
                <div className="space-y-3">
                  {results.map((result) => (
                    <Link
                      key={result.id}
                      to={`/articles/${result.slug}`}
                      onClick={() => trackClick(result.id)}
                      className="block w-full text-left p-4 border border-zinc-200 dark:border-zinc-800 rounded-xl hover:border-blue-300 dark:hover:border-blue-700 transition-colors"
                    >
                      <div className="flex items-start justify-between">
                        <div className="flex-1">
                          <h3 className="font-medium text-zinc-900 dark:text-zinc-100">{result.title}</h3>
                          {result.snippet ? (
                            <p className="text-sm text-zinc-500 mt-1 line-clamp-2">
                              <HighlightedText text={result.snippet} query={query} />
                            </p>
                          ) : result.excerpt ? (
                            <p className="text-sm text-zinc-500 mt-1 line-clamp-2">{result.excerpt}</p>
                          ) : null}
                          <div className="flex items-center gap-2 mt-2">
                            <ContentTypeBadge contentType={result.contentType} clickable />
                            {result.status && result.status !== "published" && (
                              <span className={`text-xs px-2 py-0.5 rounded-full ${result.status === "draft" ? "bg-zinc-100 text-zinc-600" :
                                  result.status === "archived" ? "bg-red-100 text-red-700" : ""
                                }`}>
                                {result.status}
                              </span>
                            )}
                            {result.score != null && (
                              <span className="text-xs px-1.5 py-0.5 bg-purple-100 dark:bg-purple-900 text-purple-700 dark:text-purple-300 rounded font-medium">
                                {(result.score * 100).toFixed(0)}%
                              </span>
                            )}
                            {result.matchType && result.matchType !== "fulltext" && (
                              <span className={cn("text-xs px-1.5 py-0.5 rounded font-medium", result.matchType === "both" ? "bg-blue-100 dark:bg-blue-900 text-blue-700 dark:text-blue-300" : "bg-violet-100 dark:bg-violet-900 text-violet-700 dark:text-violet-300")}>
                                {result.matchType === "both" ? "hybrid" : "semantic"}
                              </span>
                            )}
                            <span className="flex items-center gap-0.5 text-xs text-zinc-400">
                              <Eye size={12} />
                              {result.viewCount}
                            </span>
                            {result.wilsonScore > 0 && (
                              <span className="flex items-center gap-0.5 text-xs text-blue-600 dark:text-blue-400">
                                <ThumbsUp size={12} />
                                {(result.wilsonScore * 100).toFixed(0)}%
                              </span>
                            )}
                            {result.tags && result.tags.length > 0 && (
                              <span className="flex items-center gap-1 flex-wrap">
                                <Tag size={12} className="text-zinc-400" />
                                {result.tags.map((tag) => (
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
                            {result.apiKeyName ? (
                              <span className="flex items-center gap-1 text-xs text-purple-600 dark:text-purple-400">
                                <Key size={12} />
                                {result.apiKeyName}
                              </span>
                            ) : result.ownerName ? (
                              <span className="flex items-center gap-1 text-xs text-zinc-500">
                                <User size={12} />
                                {result.ownerName}
                              </span>
                            ) : null}
                          </div>
                        </div>
                        <span className="text-xs text-zinc-400 ml-4 whitespace-nowrap">
                          {new Date(result.updatedAt).toLocaleDateString()}
                        </span>
                      </div>
                    </Link>
                  ))}
                </div>
              )}

              {totalPages > 1 && (
                <div className="flex items-center justify-between mt-6 pt-4 border-t border-zinc-200 dark:border-zinc-800">
                  <span className="text-sm text-zinc-500">
                    Page {page} of {totalPages}
                  </span>
                  <div className="flex items-center gap-2">
                    <button
                      onClick={() => goToPage(Math.max(1, page - 1))}
                      disabled={page <= 1}
                      className="flex items-center gap-1 px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg disabled:opacity-40 disabled:cursor-not-allowed hover:bg-zinc-50 dark:hover:bg-zinc-800 transition-colors"
                    >
                      <ChevronLeft size={14} />
                      Previous
                    </button>
                    <button
                      onClick={() => goToPage(Math.min(totalPages, page + 1))}
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
        </div>
      ) : (
        <div className="text-center py-8">
          <Sparkles size={32} className="mx-auto text-zinc-300 mb-3" />
          <p className="text-zinc-500">Tüm bilgi makalelerinde hybrid arama yapın</p>
          <p className="text-xs text-zinc-400 mt-1">Tam metin ve anlam benzerliği sonuçlarını birlikte değerlendirir.</p>
        </div>
      )}
    </div>
  );
}

// Turkish accent folding mirroring the backend's SlugHelper.Transliterate — 1 char in,
// 1 char out, so match indices in folded text map straight back to the original.
const TR_FOLD: Record<string, string> = {
  "ş": "s", "ç": "c", "ğ": "g", "ü": "u", "ö": "o", "ı": "i",
  "Ş": "s", "Ç": "c", "Ğ": "g", "Ü": "u", "Ö": "o", "İ": "i",
};

function foldText(s: string): string {
  let out = "";
  for (let i = 0; i < s.length; i++) {
    const mapped = TR_FOLD[s[i]] ?? s[i];
    const lower = mapped.toLowerCase();
    out += lower.length === 1 ? lower : mapped;
  }
  return out;
}

const HIGHLIGHT_STOP_WORDS = new Set([
  "nedir", "nasil", "neden", "hangi", "kim", "nerede", "hakkinda", "acikla", "anlat", "bilgi", "ver",
  "what", "how", "why", "where", "who", "which", "define", "explain", "about", "please",
]);

function highlightTokens(query: string): string[] {
  const tokens = query
    .split(/\s+/)
    .filter((token) => token && !token.startsWith("#") && !token.startsWith("@"))
    .flatMap((token) => {
      const cleaned = token.replace(/^[^\p{L}\p{N}]+|[^\p{L}\p{N}:/_-]+$/gu, "");
      if (!cleaned) return [];
      const folded = foldText(cleaned);
      return [folded, ...folded.split(/[:/_-]+/)];
    })
    .filter((token) => token.length > 2 && !HIGHLIGHT_STOP_WORDS.has(token));

  return [...new Set(tokens)].sort((left, right) => right.length - left.length);
}

function isWordCharacter(char: string | undefined): boolean {
  return Boolean(char && /[\p{L}\p{N}_]/u.test(char));
}

/** Renders snippet text with query terms wrapped in <mark>, accent/case-insensitively. */
function HighlightedText({ text, query }: { text: string; query: string }) {
  const tokens = highlightTokens(query);
  if (tokens.length === 0) return <>{text}</>;

  const folded = foldText(text);
  const ranges: [number, number][] = [];
  for (const t of tokens) {
    let idx = 0;
    while ((idx = folded.indexOf(t, idx)) !== -1) {
      const end = idx + t.length;
      if (!isWordCharacter(folded[idx - 1]) && !isWordCharacter(folded[end])) {
        ranges.push([idx, end]);
      }
      idx += t.length;
    }
  }
  if (ranges.length === 0) return <>{text}</>;

  ranges.sort((a, b) => a[0] - b[0]);
  const merged: [number, number][] = [];
  for (const r of ranges) {
    const last = merged[merged.length - 1];
    if (last && r[0] <= last[1]) last[1] = Math.max(last[1], r[1]);
    else merged.push([r[0], r[1]]);
  }

  const parts: React.ReactNode[] = [];
  let pos = 0;
  merged.forEach(([start, end], i) => {
    if (start > pos) parts.push(text.slice(pos, start));
    parts.push(
      <mark key={i} className="rounded bg-amber-200/80 px-0.5 text-zinc-900 ring-1 ring-amber-300/70 dark:bg-amber-400/30 dark:text-amber-100 dark:ring-amber-500/40">
        {text.slice(start, end)}
      </mark>
    );
    pos = end;
  });
  if (pos < text.length) parts.push(text.slice(pos));
  return <>{parts}</>;
}

function SearchTypeTab({ active, onClick, icon, label }: {
  active: boolean; onClick: () => void; icon: React.ReactNode; label: string;
}) {
  return (
    <button
      type="button"
      role="tab"
      aria-selected={active}
      aria-label={`${label} search`}
      onClick={onClick}
      className={cn(
        "flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded-md transition-colors focus:outline-none focus:ring-2 focus:ring-blue-500",
        active
          ? "bg-white dark:bg-zinc-700 text-zinc-900 dark:text-zinc-100 shadow-sm"
          : "text-zinc-500 hover:text-zinc-700 dark:hover:text-zinc-300"
      )}
    >
      <span aria-hidden="true">{icon}</span>
      {label}
    </button>
  );
}
