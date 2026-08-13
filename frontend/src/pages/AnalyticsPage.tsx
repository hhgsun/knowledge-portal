import { useEffect, useState } from "react";
import { BarChart3, Search, Eye, AlertTriangle, TrendingUp, Users, Plug, Activity } from "lucide-react";
import { Link } from "react-router-dom";
import { useApi } from "../hooks/useApi";
import { AnalyticsSkeleton } from "../components/ui/skeleton";
import type { AnalyticsResponse } from "../types/api";

export default function AnalyticsPage() {
  const { fetchWithAuth } = useApi();
  const [data, setData] = useState<AnalyticsResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [days, setDays] = useState(30);

  useEffect(() => {
    setLoading(true);
    fetchWithAuth(`/api/analytics?days=${days}`)
      .then((res) => {
        if (!res.ok) throw new Error(res.statusText);
        return res.json();
      })
      .then((d) => { setData(d); setLoading(false); })
      .catch(() => setLoading(false));
  }, [fetchWithAuth, days]);

  if (loading) return <AnalyticsSkeleton />;
  if (!data) return <div className="text-center py-12 text-zinc-500">Analiz verileri yüklenemedi</div>;

  return (
    <div className="max-w-5xl mx-auto">
      <div className="flex items-center justify-between gap-4 mb-6">
        <h1 className="text-2xl font-bold text-zinc-900 dark:text-zinc-100">Analiz Paneli</h1>
        <select value={days} onChange={(e) => setDays(Number(e.target.value))} className="rounded-lg border border-zinc-200 bg-transparent px-3 py-2 text-sm dark:border-zinc-800">
          <option value={7}>Son 7 gün</option><option value={30}>Son 30 gün</option><option value={90}>Son 90 gün</option>
        </select>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mb-8">
        <StatCard icon={<BarChart3 size={20} />} label="Total Articles" value={data.overview.totalArticles.toString()} color="blue" />
        <StatCard icon={<Eye size={20} />} label="Views This Week" value={data.overview.viewsThisWeek.toString()} color="green" />
        <StatCard icon={<Search size={20} />} label="Searches Today" value={data.overview.searchesToday.toString()} color="purple" />
        <StatCard icon={<AlertTriangle size={20} />} label="Stale Articles" value={data.overview.staleArticles.toString()} color="amber" />
      </div>

      <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-8">
        <StatCard icon={<Activity size={20} />} label="Toplam İstek" value={data.usage.totalRequests.toLocaleString("tr-TR")} color="blue" />
        <StatCard icon={<AlertTriangle size={20} />} label="Hata Oranı" value={`%${(data.usage.errorRate * 100).toFixed(1)}`} color="amber" />
        <StatCard icon={<TrendingUp size={20} />} label="Ort. Yanıt Süresi" value={`${Math.round(data.usage.averageDurationMs)} ms`} color="purple" />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 mb-8">
        <UsageTable title="Kullanıcı Kullanımı" icon={<Users size={18} />} empty="Henüz kullanıcı kullanımı yok" rows={data.usage.users.map((u) => ({ id: u.userId, primary: u.name, secondary: u.email, requests: u.requests, errors: u.errors, duration: u.averageDurationMs }))} />
        <UsageTable title="Entegrasyon Kullanımı" icon={<Plug size={18} />} empty="Henüz entegrasyon kullanımı yok" rows={data.usage.integrations.map((i) => ({ id: i.apiKeyId, primary: i.name, secondary: `${i.ownerName} · ${i.mcpCalls} MCP`, requests: i.requests, errors: i.errors, duration: i.averageDurationMs }))} />
      </div>

      <div className="border border-zinc-200 dark:border-zinc-800 rounded-xl p-6 mb-8">
        <h2 className="font-semibold text-zinc-900 dark:text-zinc-100 mb-4 flex items-center gap-2"><Activity size={18} />En Çok Kullanılan İşlemler</h2>
        <div className="space-y-2">{data.usage.operations.map((o) => <div key={`${o.channel}-${o.operation}`} className="grid grid-cols-[1fr_auto_auto] gap-4 text-sm"><span className="truncate text-zinc-700 dark:text-zinc-300">{o.operation} <span className="text-zinc-400">({o.channel})</span></span><span>{o.requests} istek</span><span className="text-zinc-400">{Math.round(o.averageDurationMs)} ms</span></div>)}</div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <div className="border border-zinc-200 dark:border-zinc-800 rounded-xl p-6">
          <h2 className="font-semibold text-zinc-900 dark:text-zinc-100 mb-4 flex items-center gap-2">
            <TrendingUp size={18} />
            Top Searches (7 days)
          </h2>
          {data.topSearches.length === 0 ? (
            <p className="text-sm text-zinc-500">Henüz arama verisi yok</p>
          ) : (
            <div className="space-y-2">
              {data.topSearches.map((s, i) => (
                <div key={i} className="flex items-center justify-between text-sm">
                  <span className="text-zinc-700 dark:text-zinc-300">{s.query}</span>
                  <span className="text-zinc-400">{s.count} searches</span>
                </div>
              ))}
            </div>
          )}
        </div>

        <div className="border border-zinc-200 dark:border-zinc-800 rounded-xl p-6">
          <h2 className="font-semibold text-zinc-900 dark:text-zinc-100 mb-4 flex items-center gap-2">
            <AlertTriangle size={18} />
            Content Gaps (no results)
          </h2>
          {data.failedSearches.length === 0 ? (
            <p className="text-sm text-zinc-500">Sonuçsuz arama yok — kapsam çok iyi!</p>
          ) : (
            <div className="space-y-2">
              {data.failedSearches.map((s, i) => (
                <div key={i} className="flex items-center justify-between text-sm">
                  <span className="text-zinc-700 dark:text-zinc-300">{s.query}</span>
                  <span className="text-red-500">{s.count}× no results</span>
                </div>
              ))}
            </div>
          )}
        </div>

        <div className="border border-zinc-200 dark:border-zinc-800 rounded-xl p-6 lg:col-span-2">
          <h2 className="font-semibold text-zinc-900 dark:text-zinc-100 mb-4 flex items-center gap-2">
            <Eye size={18} />
            Most Viewed Articles (7 days)
          </h2>
          {data.topArticles.length === 0 ? (
            <p className="text-sm text-zinc-500">Henüz görüntülenme verisi yok</p>
          ) : (
            <div className="space-y-2">
              {data.topArticles.map((a, i) => (
                <div key={i} className="flex items-center justify-between text-sm">
                  <Link to={`/articles/${a.slug}`} className="text-blue-600 hover:underline">{a.title}</Link>
                  <span className="text-zinc-400">{a.views} views</span>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function UsageTable({ title, icon, empty, rows }: { title: string; icon: React.ReactNode; empty: string; rows: { id: string; primary: string; secondary: string; requests: number; errors: number; duration: number }[] }) {
  return <div className="border border-zinc-200 dark:border-zinc-800 rounded-xl p-6"><h2 className="font-semibold text-zinc-900 dark:text-zinc-100 mb-4 flex items-center gap-2">{icon}{title}</h2>{rows.length === 0 ? <p className="text-sm text-zinc-500">{empty}</p> : <div className="space-y-3">{rows.map((r) => <div key={r.id} className="flex items-center justify-between gap-4 text-sm"><div className="min-w-0"><p className="truncate font-medium text-zinc-800 dark:text-zinc-200">{r.primary}</p><p className="truncate text-xs text-zinc-500">{r.secondary}</p></div><div className="shrink-0 text-right"><p>{r.requests} istek</p><p className="text-xs text-zinc-500">{r.errors} hata · {Math.round(r.duration)} ms</p></div></div>)}</div>}</div>;
}

function StatCard({ icon, label, value, color }: {
  icon: React.ReactNode; label: string; value: string;
  color: "blue" | "green" | "amber" | "purple";
}) {
  const colorClasses = {
    blue: "bg-blue-50 text-blue-600 dark:bg-blue-950 dark:text-blue-400",
    green: "bg-green-50 text-green-600 dark:bg-green-950 dark:text-green-400",
    amber: "bg-amber-50 text-amber-600 dark:bg-amber-950 dark:text-amber-400",
    purple: "bg-purple-50 text-purple-600 dark:bg-purple-950 dark:text-purple-400",
  };

  return (
    <div className="border border-zinc-200 dark:border-zinc-800 rounded-xl p-4">
      <div className="flex items-center gap-3">
        <div className={`p-2 rounded-lg ${colorClasses[color]}`}>{icon}</div>
        <div>
          <p className="text-2xl font-bold text-zinc-900 dark:text-zinc-100">{value}</p>
          <p className="text-xs text-zinc-500">{label}</p>
        </div>
      </div>
    </div>
  );
}
