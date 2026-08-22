import { useState, useEffect, useRef, useCallback } from "react";
import { Link, useSearchParams, useNavigate } from "react-router-dom";
import { Search as SearchIcon, Sparkles, Bot, FileText, Zap, Tag, AlertTriangle, User, Hash, Eye, ThumbsUp, ThumbsDown, Key, Clock, X, Trash2, ChevronLeft, ChevronRight, ExternalLink, Copy, ShieldCheck } from "lucide-react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { cn } from "../lib/utils";
import { useApi } from "../hooks/useApi";
import { useLookups } from "../hooks/useLookups";
import { useSearchHistory } from "../hooks/useNetworkStatus";
import { ContentTypeBadge } from "../components/ContentTypeBadge";
import { toast } from "sonner";
import type { SearchResult, RagResponse, RagSource, TagWithCount, LookupValue, SearchIndexCoverage } from "../types/api";

type SearchType = "hybrid" | "fulltext" | "semantic" | "rag";
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
  const { contentTypes } = useLookups();
  const { history, addToHistory, removeFromHistory, clearHistory } = useSearchHistory();
  const [searchParams, setSearchParams] = useSearchParams();
  const initialQuery = searchParams.get("q") || "";
  const initialType = (searchParams.get("type") as SearchType) || "hybrid";
  const [query, setQuery] = useState(initialQuery);
  const [searchType, setSearchType] = useState<SearchType>(initialType);
  const [results, setResults] = useState<SearchResult[]>([]);
  const [ragResponse, setRagResponse] = useState<RagResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [searched, setSearched] = useState(false);
  const [responseTime, setResponseTime] = useState<number | null>(null);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [totalPages, setTotalPages] = useState(1);
  const [activeTags, setActiveTags] = useState<string[]>([]);
  const [searchQueryId, setSearchQueryId] = useState<string | null>(null);
  const [indexCoverage, setIndexCoverage] = useState<SearchIndexCoverage | null>(null);
  const [ragFeedback, setRagFeedback] = useState<"helpful" | "not_helpful" | null>(null);
  const [feedbackSubmitting, setFeedbackSubmitting] = useState(false);
  const searchInFlightRef = useRef(false);

  const copyRagAnswer = async () => {
    if (!ragResponse?.answer) return;

    try {
      await navigator.clipboard.writeText(ragResponse.answer);
      toast.success("AI yanıtı kopyalandı");
    } catch {
      toast.error("Yanıt kopyalanamadı");
    }
  };

  const submitRagFeedback = async (helpful: boolean) => {
    if (!searchQueryId || feedbackSubmitting) return;
    setFeedbackSubmitting(true);
    try {
      const response = await fetchWithAuth("/api/search/rag-feedback", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ searchQueryId, helpful }),
      });
      if (!response.ok) throw new Error("feedback failed");
      setRagFeedback(helpful ? "helpful" : "not_helpful");
      toast.success("Geri bildiriminiz kaydedildi");
    } catch {
      toast.error("Geri bildirim kaydedilemedi");
    } finally {
      setFeedbackSubmitting(false);
    }
  };
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
    setSearchParams(params, { replace: true });

    setLoading(true);
    setSearched(true);
    setRagResponse(null);
    setResults([]);
    setActiveTags([]);
    setSearchQueryId(null);
    setRagFeedback(null);
    setIndexCoverage(null);
    setWarning(null);

    try {
      const res = await fetchWithAuth(
        `/api/search?q=${encodeURIComponent(query.trim())}&type=${searchType}&page=${pageArg}`
      );
      const data = await res.json();

      if (data.tags) setActiveTags(data.tags);
      if (data.searchQueryId) setSearchQueryId(data.searchQueryId);
      setIndexCoverage(data.indexCoverage ?? null);
      if (data.warning) setWarning(data.warning);

      if (searchType === "rag") {
        setRagResponse({ ...data, sources: data.sources || [], type: "rag" });
      } else {
        setResults(data.results || []);
        setTotal(data.total ?? (data.results || []).length);
        setPage(data.page ?? 1);
        setTotalPages(data.totalPages ?? 1);
      }
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
        <h1 className="text-2xl font-bold text-zinc-900 dark:text-zinc-100 mb-4">Bilgi Bankasında Ara</h1>
        <div className="flex gap-1 mb-3 p-1 bg-zinc-100 dark:bg-zinc-800 rounded-lg w-fit" role="tablist" aria-label="Search type">
          <SearchTypeTab active={searchType === "hybrid"} onClick={() => setSearchType("hybrid")} icon={<Zap size={14} />} label="Hybrid" />
          <SearchTypeTab active={searchType === "fulltext"} onClick={() => setSearchType("fulltext")} icon={<FileText size={14} />} label="Full-Text" />
          <SearchTypeTab active={searchType === "semantic"} onClick={() => setSearchType("semantic")} icon={<Sparkles size={14} />} label="Semantic" />
          <SearchTypeTab active={searchType === "rag"} onClick={() => setSearchType("rag")} icon={<Bot size={14} />} label="Ask AI" />
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
            placeholder={searchType === "rag" ? "Ask a question..." : "Search... (@user #tag ##type)"}
            aria-label={searchType === "rag" ? "Ask a question" : "Search articles"}
            aria-describedby="search-help"
            aria-autocomplete="list"
            aria-expanded={showSuggestions || showHistory}
            className="w-full pl-11 pr-4 py-3 text-base bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded-xl focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
            autoFocus
          />
          <button
            type="submit"
            disabled={loading || !query.trim()}
            aria-label={searchType === "rag" ? "Ask AI" : "Search"}
            aria-busy={loading}
            className="absolute right-2 top-1/2 -translate-y-1/2 px-4 py-1.5 bg-blue-600 hover:bg-blue-700 text-white text-sm rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50 disabled:hover:bg-blue-600"
          >
            {loading ? "Bekleyin..." : searchType === "rag" ? "Ask" : "Search"}
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
      </div>

      {loading ? (
        <div aria-live="polite" aria-busy="true" className="space-y-3">
          {searchType === "rag" ? (
            <div className="p-5 bg-blue-50 dark:bg-blue-950 border border-blue-200 dark:border-blue-800 rounded-xl animate-pulse">
              <div className="flex items-center gap-2 mb-3">
                <div className="w-4 h-4 rounded bg-blue-200 dark:bg-blue-700" />
                <div className="w-20 h-4 rounded bg-blue-200 dark:bg-blue-700" />
              </div>
              <div className="space-y-2">
                <div className="h-4 w-full rounded bg-blue-100 dark:bg-blue-900" />
                <div className="h-4 w-5/6 rounded bg-blue-100 dark:bg-blue-900" />
                <div className="h-4 w-4/6 rounded bg-blue-100 dark:bg-blue-900" />
              </div>
            </div>
          ) : (
            Array.from({ length: 4 }).map((_, i) => (
              <div key={i} className="p-4 border border-zinc-200 dark:border-zinc-800 rounded-xl animate-pulse">
                <div className="h-5 w-2/3 bg-zinc-200 dark:bg-zinc-700 rounded mb-2" />
                <div className="h-4 w-full bg-zinc-100 dark:bg-zinc-800 rounded mb-2" />
                <div className="flex items-center gap-2">
                  <div className="h-4 w-16 bg-zinc-100 dark:bg-zinc-800 rounded" />
                  <div className="h-4 w-10 bg-zinc-100 dark:bg-zinc-800 rounded" />
                  <div className="h-4 w-12 bg-zinc-100 dark:bg-zinc-800 rounded" />
                </div>
              </div>
            ))
          )}
          <p className="sr-only">{searchType === "rag" ? "AI is thinking..." : "Searching..."}</p>
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

          {searchType === "rag" && ragResponse ? (
            <div className="space-y-4">
              <div className="p-5 bg-blue-50 dark:bg-blue-950 border border-blue-200 dark:border-blue-800 rounded-xl">
                <div className="flex items-center gap-2 mb-3">
                  <Bot size={16} className="text-blue-600" />
                  <span className="text-sm font-medium text-blue-700 dark:text-blue-300">Yapay Zekâ Yanıtı</span>
                  <div className="ml-auto flex items-center gap-2">
                    {responseTime !== null && <span className="text-xs text-blue-400">{responseTime}ms</span>}
                    <button
                      type="button"
                      onClick={copyRagAnswer}
                      className="inline-flex items-center gap-1.5 rounded-md px-2 py-1 text-xs font-medium text-blue-700 hover:bg-blue-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 dark:text-blue-300 dark:hover:bg-blue-900"
                      aria-label="AI yanıtını kopyala"
                      title="Yanıtı kopyala"
                    >
                      <Copy size={14} />
                      Kopyala
                    </button>
                  </div>
                </div>
                <div className="prose prose-sm dark:prose-invert max-w-none break-words">
                  <ReactMarkdown
                    remarkPlugins={[remarkGfm]}
                    components={{
                      a: ({ children, ...props }) => (
                        <a {...props} target="_blank" rel="noopener noreferrer">{children}</a>
                      ),
                    }}
                  >
                    {ragResponse.answer}
                  </ReactMarkdown>
                </div>
                {searchQueryId && (
                  <div className="mt-4 flex items-center gap-2 border-t border-blue-200 pt-3 text-xs text-blue-700 dark:border-blue-800 dark:text-blue-300">
                    <span>Bu yanıt yardımcı oldu mu?</span>
                    <button type="button" disabled={feedbackSubmitting} onClick={() => submitRagFeedback(true)}
                      aria-pressed={ragFeedback === "helpful"}
                      className={cn("rounded-md p-1.5 hover:bg-blue-100 dark:hover:bg-blue-900", ragFeedback === "helpful" && "bg-blue-200 dark:bg-blue-800")}
                      aria-label="Yanıt yardımcı oldu"><ThumbsUp size={14} /></button>
                    <button type="button" disabled={feedbackSubmitting} onClick={() => submitRagFeedback(false)}
                      aria-pressed={ragFeedback === "not_helpful"}
                      className={cn("rounded-md p-1.5 hover:bg-blue-100 dark:hover:bg-blue-900", ragFeedback === "not_helpful" && "bg-blue-200 dark:bg-blue-800")}
                      aria-label="Yanıt yardımcı olmadı"><ThumbsDown size={14} /></button>
                  </div>
                )}
              </div>

              {ragResponse.groundingStatus && (
                <div className={cn("flex flex-wrap items-center gap-x-3 gap-y-1 rounded-lg border px-3 py-2 text-xs",
                  ragResponse.groundingStatus === "lexically_grounded" || ragResponse.groundingStatus === "citations_verified" ? "border-green-200 bg-green-50 text-green-700 dark:border-green-900 dark:bg-green-950 dark:text-green-300" :
                  ragResponse.groundingStatus === "insufficient_context" ? "border-zinc-200 bg-zinc-50 text-zinc-600 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-300" :
                  "border-amber-200 bg-amber-50 text-amber-700 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-300") }>
                  <ShieldCheck size={14}/><span>Grounding: {ragResponse.groundingStatus.replaceAll("_", " ")}</span>
                  <span className="ml-auto">Citation IDs: {((ragResponse.citationCoverage ?? 0) * 100).toFixed(0)}%</span>
                  <span>Claim support: {((ragResponse.claimSupportCoverage ?? 0) * 100).toFixed(0)}%</span>
                </div>
              )}

              {(ragResponse.partialResult || (ragResponse.warnings?.length ?? 0) > 0) && (
                <div className="rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-amber-700 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-300">
                  {ragResponse.partialResult && <p className="font-medium">Yanıt bazı başarılı kaynak gruplarıyla oluşturuldu; sonuç kısmi olabilir.</p>}
                  {ragResponse.warnings?.map((warning, index) => <p key={index}>{warning}</p>)}
                </div>
              )}

              {ragResponse.sources.length > 0 && (
                <section aria-labelledby="rag-sources-heading">
                  <h3 id="rag-sources-heading" className="mb-2 text-sm font-medium text-zinc-700 dark:text-zinc-300">
                    Kaynaklar ({ragResponse.sources.length})
                    <span className="ml-1 font-normal text-zinc-500">· {ragResponse.evidence?.length ?? 0} kanıt</span>
                  </h3>
                  <div className="space-y-3">
                    {ragResponse.sources.map((source: RagSource) => {
                      const sourceEvidence = ragResponse.evidence?.filter(evidence => evidence.articleId === source.articleId) ?? [];

                      return (
                        <article key={source.articleId} className="overflow-hidden rounded-xl border border-zinc-200 dark:border-zinc-800">
                          <a
                            href={`/articles/${source.slug}`}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="flex items-center gap-2 px-4 py-3 text-sm transition-colors hover:bg-zinc-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-blue-500 dark:hover:bg-zinc-900"
                            aria-label={`${source.title} kaynağını yeni sekmede aç`}
                          >
                            <FileText size={14} className="shrink-0 text-blue-500" />
                            <span className="min-w-0 flex-1 truncate font-medium text-zinc-900 dark:text-zinc-100">{source.title}</span>
                            <span className="text-xs font-medium text-purple-500">{(source.score * 100).toFixed(0)}%</span>
                            <ExternalLink size={12} className="shrink-0 text-zinc-400" aria-hidden="true" />
                          </a>
                          {sourceEvidence.length > 0 && (
                            <div className="space-y-3 border-t border-zinc-200 px-4 py-3 dark:border-zinc-800">
                              {sourceEvidence.map(evidence => (
                                <div key={evidence.sourceId} className="border-l-2 border-blue-400 pl-3 text-xs">
                                  <div className="font-medium text-zinc-700 dark:text-zinc-300">
                                    <span className="text-blue-600 dark:text-blue-400">{evidence.sourceId}</span>
                                    {evidence.sourceName ? ` · ${evidence.sourceName}` : ""}
                                    {evidence.pageNumber ? ` · sayfa ${evidence.pageNumber}` : ""}
                                  </div>
                                  <p className="mt-1 whitespace-pre-wrap text-zinc-500 dark:text-zinc-400">{evidence.passage}</p>
                                </div>
                              ))}
                            </div>
                          )}
                        </article>
                      );
                    })}
                  </div>
                </section>
              )}
            </div>
          ) : (
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
                  <p className="text-sm text-zinc-400 mt-1">Farklı anahtar kelimeler deneyin veya yapay zekâ arama modunu kullanın</p>
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
          )}
        </div>
      ) : (
        <div className="text-center py-8">
          <Sparkles size={32} className="mx-auto text-zinc-300 mb-3" />
          <p className="text-zinc-500">Search across all knowledge articles with AI-powered hybrid search</p>
          <p className="text-xs text-zinc-400 mt-1">Combines full-text and semantic search, or ask AI directly</p>
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

/** Renders snippet text with query terms wrapped in <mark>, accent/case-insensitively. */
function HighlightedText({ text, query }: { text: string; query: string }) {
  const tokens = query
    .split(/\s+/)
    .filter((w) => w && !w.startsWith("#") && !w.startsWith("@"))
    .map(foldText)
    .filter((w) => w.length > 1);
  if (tokens.length === 0) return <>{text}</>;

  const folded = foldText(text);
  const ranges: [number, number][] = [];
  for (const t of tokens) {
    let idx = 0;
    while ((idx = folded.indexOf(t, idx)) !== -1) {
      ranges.push([idx, idx + t.length]);
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
      <mark key={i} className="bg-yellow-100 dark:bg-yellow-900/60 text-inherit rounded px-0.5">
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
