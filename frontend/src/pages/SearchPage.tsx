import { useState, useEffect, useRef, useCallback } from "react";
import { useSearchParams, useNavigate } from "react-router-dom";
import { Search as SearchIcon, Sparkles, Bot, FileText, Zap, Tag, AlertTriangle } from "lucide-react";
import { cn } from "../lib/utils";
import { useApi } from "../hooks/useApi";
import { toast } from "sonner";
import type { SearchResult, RagResponse, RagSource, TagWithCount } from "../types/api";

type SearchType = "hybrid" | "fulltext" | "semantic" | "rag";

export default function SearchPage() {
  const { fetchWithAuth } = useApi();
  const [searchParams] = useSearchParams();
  const initialQuery = searchParams.get("q") || "";
  const [query, setQuery] = useState(initialQuery);
  const [searchType, setSearchType] = useState<SearchType>("hybrid");
  const [results, setResults] = useState<SearchResult[]>([]);
  const [ragResponse, setRagResponse] = useState<RagResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [searched, setSearched] = useState(false);
  const [responseTime, setResponseTime] = useState<number | null>(null);
  const [activeTags, setActiveTags] = useState<string[]>([]);
  const [searchQueryId, setSearchQueryId] = useState<string | null>(null);
  const [indexingPending, setIndexingPending] = useState(false);
  const [warning, setWarning] = useState<string | null>(null);
  const navigate = useNavigate();

  const [showTagSuggestions, setShowTagSuggestions] = useState(false);
  const [availableTags, setAvailableTags] = useState<TagWithCount[]>([]);
  const [filteredTags, setFilteredTags] = useState<TagWithCount[]>([]);
  const inputRef = useRef<HTMLInputElement>(null);
  const suggestionsRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    fetchWithAuth("/api/tags")
      .then((r) => r.json())
      .then((data) => setAvailableTags(data))
      .catch(() => { toast.error("Failed to load tags"); });
  }, [fetchWithAuth]);

  useEffect(() => {
    // Detect @partial at end of query for tag suggestions
    const match = query.match(/@(\S*)$/);
    if (match) {
      const partial = match[1].toLowerCase();
      // Exclude already selected tags
      const existingTags = [...query.matchAll(/@(\S+)/g)].map(m => m[1].toLowerCase()).filter(t => t !== partial);
      const filtered = availableTags.filter(
        (t) => (t.slug.includes(partial) || t.name.toLowerCase().includes(partial)) && !existingTags.includes(t.slug)
      );
      setFilteredTags(filtered);
      setShowTagSuggestions(filtered.length > 0);
    } else {
      setShowTagSuggestions(false);
    }
  }, [query, availableTags]);

  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (suggestionsRef.current && !suggestionsRef.current.contains(e.target as Node)) {
        setShowTagSuggestions(false);
      }
    };
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  const selectTag = (slug: string) => {
    // Replace the incomplete @partial at end with the selected tag
    const newQuery = query.replace(/@\S*$/, `@${slug} `);
    setQuery(newQuery);
    setShowTagSuggestions(false);
    inputRef.current?.focus();
  };

  const handleSearch = async (e?: React.FormEvent) => {
    e?.preventDefault();
    if (!query.trim()) return;

    setLoading(true);
    setSearched(true);
    setRagResponse(null);
    setResults([]);
    setActiveTags([]);
    setSearchQueryId(null);
    setIndexingPending(false);
    setWarning(null);

    const res = await fetchWithAuth(
      `/api/search?q=${encodeURIComponent(query.trim())}&type=${searchType}`
    );
    const data = await res.json();

    if (data.tags) setActiveTags(data.tags);
    if (data.searchQueryId) setSearchQueryId(data.searchQueryId);
    if (data.indexingPending) setIndexingPending(data.indexingPending);
    if (data.warning) setWarning(data.warning);

    if (searchType === "rag") {
      setRagResponse({ answer: data.answer, sources: data.sources || [], query: data.query, type: "rag", responseTimeMs: data.responseTimeMs, indexingPending: data.indexingPending });
    } else {
      setResults(data.results || []);
    }
    setResponseTime(data.responseTimeMs || null);
    setLoading(false);
  };

  const trackClick = useCallback((articleId: string, slug: string) => {
    if (searchQueryId) {
      fetchWithAuth("/api/search/click", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ searchQueryId, articleId }),
      }).catch(() => {});
    }
    navigate(`/articles/${slug}`);
  }, [searchQueryId, fetchWithAuth, navigate]);

  return (
    <div className="max-w-5xl mx-auto">
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-zinc-900 dark:text-zinc-100 mb-4">Search Knowledge Base</h1>
        <form onSubmit={handleSearch} className="relative">
          <SearchIcon size={18} className="absolute left-4 top-1/2 -translate-y-1/2 text-zinc-400" />
          <input
            ref={inputRef}
            type="text"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder={searchType === "rag" ? "Ask a question..." : "Search articles... (use @tag to filter, multiple tags supported)"}
            className="w-full pl-11 pr-4 py-3 text-base bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded-xl focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
            autoFocus
          />
          <button type="submit" className="absolute right-2 top-1/2 -translate-y-1/2 px-4 py-1.5 bg-blue-600 hover:bg-blue-700 text-white text-sm rounded-lg">
            {searchType === "rag" ? "Ask" : "Search"}
          </button>

          {showTagSuggestions && (
            <div ref={suggestionsRef} className="absolute top-full left-0 right-0 mt-1 bg-white dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-700 rounded-xl shadow-lg z-50 max-h-60 overflow-y-auto">
              <div className="px-3 py-2 text-xs text-zinc-400 border-b border-zinc-100 dark:border-zinc-800">Select a tag to filter</div>
              {filteredTags.map((tag) => (
                <button
                  key={tag.id}
                  type="button"
                  onClick={() => selectTag(tag.slug)}
                  className="w-full flex items-center gap-2 px-3 py-2 text-sm text-left hover:bg-zinc-100 dark:hover:bg-zinc-800 transition-colors"
                >
                  <Tag size={14} className="text-blue-500 shrink-0" />
                  <span className="text-zinc-900 dark:text-zinc-100">{tag.name}</span>
                  <span className="ml-auto text-xs text-zinc-400">{tag.articleCount} article{tag.articleCount !== 1 ? "s" : ""}</span>
                </button>
              ))}
            </div>
          )}
        </form>

        <div className="flex gap-1 mt-3 p-1 bg-zinc-100 dark:bg-zinc-800 rounded-lg w-fit">
          <SearchTypeTab active={searchType === "hybrid"} onClick={() => setSearchType("hybrid")} icon={<Zap size={14} />} label="Hybrid" />
          <SearchTypeTab active={searchType === "fulltext"} onClick={() => setSearchType("fulltext")} icon={<FileText size={14} />} label="Full-Text" />
          <SearchTypeTab active={searchType === "semantic"} onClick={() => setSearchType("semantic")} icon={<Sparkles size={14} />} label="Semantic" />
          <SearchTypeTab active={searchType === "rag"} onClick={() => setSearchType("rag")} icon={<Bot size={14} />} label="Ask AI" />
        </div>
      </div>

      {loading ? (
        <div className="text-center py-8 text-zinc-500">{searchType === "rag" ? "AI düşünüyor..." : "Searching..."}</div>
      ) : searched ? (
        <div>
          {indexingPending && (
            <div className="flex items-center gap-2 mb-4 p-3 bg-amber-50 dark:bg-amber-950 border border-amber-200 dark:border-amber-800 rounded-lg text-sm text-amber-700 dark:text-amber-300">
              <AlertTriangle size={16} />
              <span>Bazı makaleler henüz indekslenmedi. Sonuçlar tam olmayabilir.</span>
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
                  <span className="text-sm font-medium text-blue-700 dark:text-blue-300">AI Answer</span>
                  {responseTime !== null && <span className="text-xs text-blue-400 ml-auto">{responseTime}ms</span>}
                </div>
                <div className="prose prose-sm dark:prose-invert max-w-none whitespace-pre-wrap">{ragResponse.answer}</div>
              </div>

              {ragResponse.sources.length > 0 && (
                <div>
                  <h3 className="text-sm font-medium text-zinc-700 dark:text-zinc-300 mb-2">Sources ({ragResponse.sources.length})</h3>
                  <div className="flex flex-wrap gap-2">
                    {ragResponse.sources.map((source: RagSource) => (
                      <button
                        key={source.articleId}
                        onClick={() => navigate(`/articles/${source.slug}`)}
                        className="inline-flex items-center gap-2 px-3 py-2 border border-zinc-200 dark:border-zinc-800 rounded-lg text-sm hover:border-blue-300 dark:hover:border-blue-700 transition-colors"
                      >
                        <FileText size={14} className="text-blue-500" />
                        <span className="text-zinc-900 dark:text-zinc-100">{source.title}</span>
                        <span className="text-xs text-purple-500 font-medium">{(source.score * 100).toFixed(0)}%</span>
                      </button>
                    ))}
                  </div>
                </div>
              )}
            </div>
          ) : (
            <div>
              <div className="flex items-center justify-between mb-4">
                <p className="text-sm text-zinc-500">
                  {results.length} result{results.length !== 1 ? "s" : ""}
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
                  <p className="text-zinc-500">No results found</p>
                  <p className="text-sm text-zinc-400 mt-1">Try different keywords or use AI search mode</p>
                </div>
              ) : (
                <div className="space-y-3">
                  {results.map((result) => (
                    <button
                      key={result.id}
                      onClick={() => trackClick(result.id, result.slug)}
                      className="block w-full text-left p-4 border border-zinc-200 dark:border-zinc-800 rounded-xl hover:border-blue-300 dark:hover:border-blue-700 transition-colors"
                    >
                      <div className="flex items-start justify-between gap-2">
                        <h3 className="font-medium text-zinc-900 dark:text-zinc-100">{result.title}</h3>
                        <div className="flex items-center gap-1.5 shrink-0">
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
                        </div>
                      </div>
                      {result.excerpt && <p className="text-sm text-zinc-500 mt-1 line-clamp-2">{result.excerpt}</p>}
                      <div className="flex items-center gap-2 mt-2 text-xs text-zinc-400">
                        <span>{result.contentType}</span>
                        <span>·</span>
                        <span>{new Date(result.updatedAt).toLocaleDateString()}</span>
                      </div>
                    </button>
                  ))}
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

function SearchTypeTab({ active, onClick, icon, label }: {
  active: boolean; onClick: () => void; icon: React.ReactNode; label: string;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        "flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded-md transition-colors",
        active
          ? "bg-white dark:bg-zinc-700 text-zinc-900 dark:text-zinc-100 shadow-sm"
          : "text-zinc-500 hover:text-zinc-700 dark:hover:text-zinc-300"
      )}
    >
      {icon}
      {label}
    </button>
  );
}
