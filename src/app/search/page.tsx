"use client";

import { useState, Suspense } from "react";
import { useSearchParams } from "next/navigation";
import { Search as SearchIcon, Filter, Sparkles } from "lucide-react";
import Link from "next/link";

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

function SearchContent() {
  const searchParams = useSearchParams();
  const initialQuery = searchParams.get("q") || "";
  const [query, setQuery] = useState(initialQuery);
  const [results, setResults] = useState<SearchResult[]>([]);
  const [loading, setLoading] = useState(false);
  const [searched, setSearched] = useState(false);
  const [responseTime, setResponseTime] = useState<number | null>(null);

  const handleSearch = async (e?: React.FormEvent) => {
    e?.preventDefault();
    if (!query.trim()) return;

    setLoading(true);
    setSearched(true);

    const res = await fetch(
      `/api/search?q=${encodeURIComponent(query.trim())}`
    );
    const data = await res.json();
    setResults(data.results || []);
    setResponseTime(data.responseTimeMs || null);
    setLoading(false);
  };

  return (
    <div className="max-w-4xl mx-auto">
      {/* Search Header */}
      <div className="mb-8">
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
            placeholder="Search articles, guides, runbooks..."
            className="w-full pl-11 pr-4 py-3 text-base bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded-xl focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
            autoFocus
          />
          <button
            type="submit"
            className="absolute right-2 top-1/2 -translate-y-1/2 px-4 py-1.5 bg-blue-600 hover:bg-blue-700 text-white text-sm rounded-lg"
          >
            Search
          </button>
        </form>
      </div>

      {/* Results */}
      {loading ? (
        <div className="text-center py-8 text-zinc-500">Searching...</div>
      ) : searched ? (
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
                Try different keywords or check spelling
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
      ) : (
        <div className="text-center py-8">
          <Sparkles size={32} className="mx-auto text-zinc-300 mb-3" />
          <p className="text-zinc-500">
            Enter a query to search across all knowledge articles
          </p>
          <p className="text-xs text-zinc-400 mt-1">
            Supports full-text search with AI-powered semantic search coming soon
          </p>
        </div>
      )}
    </div>
  );
}
