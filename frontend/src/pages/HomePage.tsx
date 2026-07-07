import { useEffect, useState, useRef } from "react";
import { Search, ArrowRight, FileText } from "lucide-react";
import { Link, useNavigate } from "react-router-dom";
import { useApi } from "../hooks/useApi";
import { toast } from "sonner";
import { ContentTypeBadge } from "../components/ContentTypeBadge";
import { HomeSkeleton } from "../components/ui/skeleton";
import type { DashboardResponse } from "../types/api";

export default function HomePage() {
  const { fetchWithAuth } = useApi();
  const navigate = useNavigate();
  const inputRef = useRef<HTMLInputElement>(null);
  const [data, setData] = useState<DashboardResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [query, setQuery] = useState("");

  useEffect(() => {
    fetchWithAuth("/api/dashboard")
      .then((res) => {
        if (!res.ok) throw new Error("Failed to load dashboard");
        return res.json();
      })
      .then((d) => { setData(d); setLoading(false); })
      .catch((e) => { toast.error(e.message); setLoading(false); });
  }, [fetchWithAuth]);

  useEffect(() => {
    inputRef.current?.focus();
  }, [loading]);

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault();
    if (!query.trim()) return;
    navigate(`/search?q=${encodeURIComponent(query.trim())}`);
  };

  if (loading) {
    return <HomeSkeleton />;
  }

  return (
    <div className="max-w-4xl mx-auto">
      {/* Hero search section */}
      <div className="flex flex-col items-center text-center pt-8 pb-10">
        <h1 className="text-3xl font-bold text-zinc-900 dark:text-zinc-100 mb-2">
          Knowledge Portal
        </h1>
        <p className="text-zinc-500 mb-8">
          Bilgi tabanında arayın, keşfedin ve katkıda bulunun.
        </p>

        <form onSubmit={handleSearch} className="w-full max-w-2xl relative">
          <Search size={20} className="absolute left-4 top-1/2 -translate-y-1/2 text-zinc-400" />
          <input
            ref={inputRef}
            type="text"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Makale, konu veya anahtar kelime ara..."
            className="w-full pl-12 pr-14 py-4 text-lg rounded-xl border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-zinc-900 dark:text-zinc-100 placeholder:text-zinc-400 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent shadow-sm"
          />
          <button
            type="submit"
            className="absolute right-3 top-1/2 -translate-y-1/2 p-2 rounded-lg bg-blue-600 text-white hover:bg-blue-700 transition-colors"
          >
            <ArrowRight size={18} />
          </button>
        </form>

        {/* Popular search chips */}
        {data?.topSearches && data.topSearches.length > 0 && (
          <div className="flex flex-wrap items-center justify-center gap-2 mt-4">
            <span className="text-xs text-zinc-400">Popüler:</span>
            {data.topSearches.slice(0, 6).map((s, i) => (
              <Link
                key={i}
                to={`/search?q=${encodeURIComponent(s.query)}`}
                className="px-3 py-1 text-xs rounded-full bg-zinc-100 dark:bg-zinc-800 text-zinc-600 dark:text-zinc-300 hover:bg-blue-50 hover:text-blue-600 dark:hover:bg-blue-950 dark:hover:text-blue-400 transition-colors"
              >
                {s.query}
              </Link>
            ))}
          </div>
        )}
      </div>

      {/* Recent articles */}
      <div className="border border-zinc-200 dark:border-zinc-800 rounded-xl p-6">
        <div className="flex items-center justify-between mb-4">
          <h2 className="font-semibold text-zinc-900 dark:text-zinc-100 flex items-center gap-2">
            <FileText size={18} />
            Son Makaleler
          </h2>
          <Link to="/articles" className="text-xs text-blue-600 hover:underline flex items-center gap-1">
            Tümünü gör <ArrowRight size={14} />
          </Link>
        </div>
        {data?.recentArticles && data.recentArticles.length > 0 ? (
          <ul className="space-y-3">
            {data.recentArticles.map((a) => (
              <li key={a.id} className="flex items-center justify-between gap-2">
                <Link to={`/articles/${a.slug}`} className="text-sm text-zinc-700 dark:text-zinc-300 hover:text-blue-600 dark:hover:text-blue-400 truncate">
                  {a.title}
                </Link>
                <ContentTypeBadge value={a.contentType} />
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-sm text-zinc-500">Henüz makale yok. İlk makalenizi oluşturun.</p>
        )}
      </div>
    </div>
  );
}
