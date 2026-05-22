"use client";

import { useState, useEffect, useRef, Suspense } from "react";
import { useSearchParams } from "next/navigation";
import { Search as SearchIcon, Sparkles, Bot, FileText, Zap, Tag } from "lucide-react";
import Link from "next/link";
import { cn } from "@/lib/utils";

export default function SearchPage() {
  return (
    <Suspense>
      <SearchContent />
    </Suspense>
  );
}

interface SearchResult {
  id: string;
  title: string;
  slug: string;
  excerpt: string | null;
  contentType: string;
  difficulty: string;
  updatedAt: string;
}

interface RAGResponse {
  answer: string;
  sources: { articleId: string; text: string; score: number }[];
}

type SearchType = "hybrid" | "fulltext" | "semantic" | "rag";

function SearchContent() {
  const searchParams = useSearchParams();
  const initialQuery = searchParams.get("q") || "";
  const [query, setQuery] = useState(initialQuery);
  const [searchType, setSearchType] = useState<SearchType>("hybrid");
  const [results, setResults] = useState<SearchResult[]>([]);
  const [ragResponse, setRagResponse] = useState<RAGResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [searched, setSearched] = useState(false);
  const [responseTime, setResponseTime] = useState<number | null>(null);
  const [activeTag, setActiveTag] = useState<string | null>(null);

  // Tag autocomplete state
  const [showTagSuggestions, setShowTagSuggestions] = useState(false);
  const [availableTags, setAvailableTags] = useState<{ id: string; name: string; slug: string; articleCount: number }[]>([]);
  const [filteredTags, setFilteredTags] = useState<{ id: string; name: string; slug: string; articleCount: number }[]>([]);
  const inputRef = useRef<HTMLInputElement>(null);
  const suggestionsRef = useRef<HTMLDivElement>(null);

  // Load tags once
  useEffect(() => {
    fetch("/api/tags")
      .then((r) => r.json())
      .then((data) => setAvailableTags(data))
      .catch(() => {});
  }, []);

  // Filter tag suggestions based on input
  useEffect(() => {
    const match = query.match(/^@(\S*)$/);
    if (match) {
      const partial = match[1].toLowerCase();
      const filtered = availableTags.filter(
        (t) => t.slug.includes(partial) || t.name.toLowerCase().includes(partial)
      );
      setFilteredTags(filtered);
      setShowTagSuggestions(filtered.length > 0);
    } else {
      setShowTagSuggestions(false);
    }
  }, [query, availableTags]);

  // Close suggestions on click outside
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
    setQuery(`@${slug} `);
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
    setActiveTag(null);

    const res = await fetch(
      `/api/search?q=${encodeURIComponent(query.trim())}&type=${searchType}`
    );
    const data = await res.json();

    if (data.tag) {
      setActiveTag(data.tag);
    }

    if (searchType === "rag") {
      setRagResponse({ answer: data.answer, sources: data.sources || [] });
    } else {
      setResults(data.results || []);
    }
    setResponseTime(data.responseTimeMs || null);
    setLoading(false);
  };

  return (
    <div className="max-w-4xl mx-auto">
      {/* Search Header */}
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-zinc-900 dark:text-zinc-100 mb-4">
          Search Knowledge Base
        </h1>
        <form onSubmit={handleSearch} className="relative">
          <SearchIcon
            size={18}
            className="absolute left-4 top-1/2 -translate-y-1/2 text-zinc-400"
          />
          <input
            ref={inputRef}
            type="text"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder={searchType === "rag" ? "Ask a question..." : "Search articles... (use @tag to filter by tag)"}
            className="w-full pl-11 pr-4 py-3 text-base bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded-xl focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
            autoFocus
          />
          <button
            type="submit"
            className="absolute right-2 top-1/2 -translate-y-1/2 px-4 py-1.5 bg-blue-600 hover:bg-blue-700 text-white text-sm rounded-lg"
          >
            {searchType === "rag" ? "Ask" : "Search"}
          </button>

          {/* Tag autocomplete dropdown */}
          {showTagSuggestions && (
            <div
              ref={suggestionsRef}
              className="absolute top-full left-0 right-0 mt-1 bg-white dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-700 rounded-xl shadow-lg z-50 max-h-60 overflow-y-auto"
            >
              <div className="px-3 py-2 text-xs text-zinc-400 border-b border-zinc-100 dark:border-zinc-800">
                Select a tag to filter
              </div>
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

        {/* Search Type Tabs */}
        <div className="flex gap-1 mt-3 p-1 bg-zinc-100 dark:bg-zinc-800 rounded-lg w-fit">
          <SearchTypeTab
            active={searchType === "hybrid"}
            onClick={() => setSearchType("hybrid")}
            icon={<Zap size={14} />}
            label="Hybrid"
          />
          <SearchTypeTab
            active={searchType === "fulltext"}
            onClick={() => setSearchType("fulltext")}
            icon={<FileText size={14} />}
            label="Full-Text"
          />
          <SearchTypeTab
            active={searchType === "semantic"}
            onClick={() => setSearchType("semantic")}
            icon={<Sparkles size={14} />}
            label="Semantic"
          />
          <SearchTypeTab
            active={searchType === "rag"}
            onClick={() => setSearchType("rag")}
            icon={<Bot size={14} />}
            label="Ask AI"
          />
        </div>
      </div>

      {/* Results */}
      {loading ? (
        <div className="text-center py-8 text-zinc-500">
          {searchType === "rag" ? "Thinking..." : "Searching..."}
        </div>
      ) : searched ? (
        <div>
          {/* RAG Answer */}
          {searchType === "rag" && ragResponse ? (
            <div className="space-y-4">
              <div className="p-5 bg-blue-50 dark:bg-blue-950 border border-blue-200 dark:border-blue-800 rounded-xl">
                <div className="flex items-center gap-2 mb-3">
                  <Bot size={16} className="text-blue-600" />
                  <span className="text-sm font-medium text-blue-700 dark:text-blue-300">
                    AI Answer
                  </span>
                  {responseTime !== null && (
                    <span className="text-xs text-blue-400 ml-auto">
                      {responseTime}ms
                    </span>
                  )}
                </div>
                <div className="prose prose-sm dark:prose-invert max-w-none whitespace-pre-wrap">
                  {ragResponse.answer}
                </div>
              </div>

              {ragResponse.sources.length > 0 && (
                <div>
                  <h3 className="text-sm font-medium text-zinc-700 dark:text-zinc-300 mb-2">
                    Sources ({ragResponse.sources.length})
                  </h3>
                  <div className="space-y-2">
                    {ragResponse.sources.map((source, i) => (
                      <div
                        key={i}
                        className="p-3 border border-zinc-200 dark:border-zinc-800 rounded-lg text-sm"
                      >
                        <p className="text-zinc-600 dark:text-zinc-400 line-clamp-2">
                          {source.text}
                        </p>
                        <span className="text-xs text-zinc-400 mt-1 inline-block">
                          Relevance: {(source.score * 100).toFixed(0)}%
                        </span>
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </div>
          ) : (
            /* Standard search results */
            <div>
              <div className="flex items-center justify-between mb-4">
                <p className="text-sm text-zinc-500">
                  {results.length} result{results.length !== 1 ? "s" : ""}
                  {activeTag && (
                    <span className="inline-flex items-center gap-1 ml-2 px-2 py-0.5 bg-blue-100 dark:bg-blue-900 text-blue-700 dark:text-blue-300 rounded-full text-xs">
                      <Tag size={10} />
                      {activeTag}
                    </span>
                  )}
                  {!activeTag && (
                    <> for &ldquo;{query}&rdquo;</>
                  )}
                  {responseTime !== null && (
                    <span className="ml-1">({responseTime}ms)</span>
                  )}
                </p>
              </div>

              {results.length === 0 ? (
                <div className="text-center py-8 border border-dashed border-zinc-300 dark:border-zinc-700 rounded-xl">
                  <p className="text-zinc-500">No results found</p>
                  <p className="text-sm text-zinc-400 mt-1">
                    Try different keywords or use AI search mode
                  </p>
                </div>
              ) : (
                <div className="space-y-3">
                  {results.map((result) => (
                    <Link
                      key={result.id}
                      href={`/articles/${result.slug}`}
                      className="block p-4 border border-zinc-200 dark:border-zinc-800 rounded-xl hover:border-blue-300 dark:hover:border-blue-700 transition-colors"
                    >
                      <h3 className="font-medium text-zinc-900 dark:text-zinc-100">
                        {result.title}
                      </h3>
                      {result.excerpt && (
                        <p className="text-sm text-zinc-500 mt-1 line-clamp-2">
                          {result.excerpt}
                        </p>
                      )}
                      <div className="flex items-center gap-2 mt-2 text-xs text-zinc-400">
                        <span>{result.contentType}</span>
                        <span>·</span>
                        <span>{result.difficulty}</span>
                        <span>·</span>
                        <span>{new Date(result.updatedAt).toLocaleDateString()}</span>
                      </div>
                    </Link>
                  ))}
                </div>
              )}
            </div>
          )}
        </div>
      ) : (
        <div className="text-center py-8">
          <Sparkles size={32} className="mx-auto text-zinc-300 mb-3" />
          <p className="text-zinc-500">
            Search across all knowledge articles with AI-powered hybrid search
          </p>
          <p className="text-xs text-zinc-400 mt-1">
            Combines full-text and semantic search, or ask AI directly
          </p>
        </div>
      )}
    </div>
  );
}

function SearchTypeTab({
  active,
  onClick,
  icon,
  label,
}: {
  active: boolean;
  onClick: () => void;
  icon: React.ReactNode;
  label: string;
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
