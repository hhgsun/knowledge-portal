"use client";

import { useState, Suspense } from "react";
import { useSearchParams } from "next/navigation";
import { Search as SearchIcon, Sparkles, Bot, FileText, Zap } from "lucide-react";
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

  const handleSearch = async (e?: React.FormEvent) => {
    e?.preventDefault();
    if (!query.trim()) return;

    setLoading(true);
    setSearched(true);
    setRagResponse(null);
    setResults([]);

    const res = await fetch(
      `/api/search?q=${encodeURIComponent(query.trim())}&type=${searchType}`
    );
    const data = await res.json();

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
            type="text"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder={searchType === "rag" ? "Ask a question..." : "Search articles, guides, runbooks..."}
            className="w-full pl-11 pr-4 py-3 text-base bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded-xl focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
            autoFocus
          />
          <button
            type="submit"
            className="absolute right-2 top-1/2 -translate-y-1/2 px-4 py-1.5 bg-blue-600 hover:bg-blue-700 text-white text-sm rounded-lg"
          >
            {searchType === "rag" ? "Ask" : "Search"}
          </button>
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
                  {results.length} result{results.length !== 1 ? "s" : ""} for &ldquo;
                  {query}&rdquo;
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
