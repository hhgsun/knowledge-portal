import { useEffect, useMemo, useState } from "react";
import {
  Activity, AlertTriangle, BarChart3, Clock3, Eye, KeyRound,
  Search, Server, TrendingUp, UserRoundCheck,
} from "lucide-react";
import { Link } from "react-router-dom";
import { useApi } from "../hooks/useApi";
import { AnalyticsSkeleton } from "../components/ui/skeleton";
import type {
  AnalyticsDailyUsage, AnalyticsIntegrationUsage, AnalyticsOperationUsage,
  AnalyticsResponse, AnalyticsUserUsage,
} from "../types/api";

type DetailTab = "users" | "integrations" | "operations";

export default function AnalyticsPage() {
  const { fetchWithAuth } = useApi();
  const [data, setData] = useState<AnalyticsResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [days, setDays] = useState(30);
  const [detailTab, setDetailTab] = useState<DetailTab>("users");
  const [filter, setFilter] = useState("");

  useEffect(() => {
    let cancelled = false;
    fetchWithAuth(`/api/analytics?days=${days}`)
      .then((res) => {
        if (!res.ok) throw new Error(res.statusText);
        return res.json();
      })
      .then((response) => {
        if (!cancelled) { setData(response); setLoading(false); }
      })
      .catch(() => {
        if (!cancelled) { setData(null); setLoading(false); }
      });
    return () => { cancelled = true; };
  }, [fetchWithAuth, days]);

  const filteredDetails = useMemo(() => {
    if (!data) return [];
    const term = filter.trim().toLocaleLowerCase("tr-TR");
    if (detailTab === "users")
      return data.usage.users.filter((item) => !term || `${item.name} ${item.email} ${item.role} ${item.topOperation ?? ""}`.toLocaleLowerCase("tr-TR").includes(term));
    if (detailTab === "integrations")
      return data.usage.integrations.filter((item) => !term || `${item.name} ${item.ownerName} ${item.ownerEmail} ${item.topOperation ?? ""}`.toLocaleLowerCase("tr-TR").includes(term));
    return data.usage.operations.filter((item) => !term || `${item.operation} ${item.channel}`.toLocaleLowerCase("tr-TR").includes(term));
  }, [data, detailTab, filter]);

  if (loading) return <AnalyticsSkeleton />;
  if (!data) return <div className="py-12 text-center text-zinc-500">Analiz verileri yüklenemedi.</div>;

  const usage = data.usage;
  return (
    <div className="mx-auto max-w-7xl">
      <div className="mb-6 flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold text-zinc-900 dark:text-zinc-100">Analiz Paneli</h1>
          <p className="mt-1 text-sm text-zinc-500">Kullanıcı, entegrasyon ve işlem bazında portal kullanımı</p>
        </div>
        <select
          value={days}
          onChange={(event) => { setLoading(true); setDays(Number(event.target.value)); }}
          aria-label="Analiz dönemi"
          className="rounded-lg border border-zinc-200 bg-white px-3 py-2 text-sm dark:border-zinc-800 dark:bg-zinc-950"
        >
          <option value={7}>Son 7 gün</option>
          <option value={30}>Son 30 gün</option>
          <option value={90}>Son 90 gün</option>
        </select>
      </div>

      <div className="mb-8 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard icon={<BarChart3 size={20} />} label="Toplam Makale" value={formatNumber(data.overview.totalArticles)} color="blue" />
        <StatCard icon={<Eye size={20} />} label="Bu Hafta Görüntülenme" value={formatNumber(data.overview.viewsThisWeek)} color="green" />
        <StatCard icon={<Search size={20} />} label="Bugünkü Aramalar" value={formatNumber(data.overview.searchesToday)} color="purple" />
        <StatCard icon={<AlertTriangle size={20} />} label="Güncelliğini Yitiren" value={formatNumber(data.overview.staleArticles)} color="amber" />
      </div>

      <section className="mb-8">
        <div className="mb-4 flex items-end justify-between gap-4">
          <div>
            <h2 className="font-semibold text-zinc-900 dark:text-zinc-100">Kullanım özeti</h2>
            <p className="text-xs text-zinc-500">{formatDate(usage.periodStart)} – {formatDate(usage.periodEnd)}</p>
          </div>
          <span className="text-xs text-zinc-500">Başarılı: {formatNumber(usage.successfulRequests)}</span>
        </div>
        <div className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-6">
          <MetricCard icon={<Activity size={17} />} label="Toplam istek" value={formatNumber(usage.totalRequests)} />
          <MetricCard icon={<UserRoundCheck size={17} />} label="Aktif kullanıcı" value={formatNumber(usage.activeUsers)} />
          <MetricCard icon={<KeyRound size={17} />} label="Aktif entegrasyon" value={formatNumber(usage.activeIntegrations)} />
          <MetricCard icon={<Server size={17} />} label="REST / MCP" value={`${formatCompact(usage.restRequests)} / ${formatCompact(usage.mcpCalls)}`} />
          <MetricCard icon={<AlertTriangle size={17} />} label="Hata oranı" value={formatPercent(usage.errorRate)} />
          <MetricCard icon={<Clock3 size={17} />} label="Ort. yanıt" value={`${Math.round(usage.averageDurationMs)} ms`} />
        </div>
        <div className="mt-3 flex flex-wrap gap-x-5 gap-y-1 text-xs text-zinc-500">
          <span>Doğrudan oturum: <strong className="text-zinc-700 dark:text-zinc-300">{formatNumber(usage.sessionRequests)}</strong></span>
          <span>API anahtarı: <strong className="text-zinc-700 dark:text-zinc-300">{formatNumber(usage.integrationRequests)}</strong></span>
          <span>Hatalı istek: <strong className="text-zinc-700 dark:text-zinc-300">{formatNumber(usage.errors)}</strong></span>
        </div>
      </section>

      <UsageTrend rows={usage.daily} />

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        <InfoList title="En Çok Arananlar (7 gün)" icon={<TrendingUp size={18} />} empty="Henüz arama verisi yok">
          {data.topSearches.map((item) => <InfoRow key={item.query} label={item.query} value={`${formatNumber(item.count)} arama`} />)}
        </InfoList>
        <InfoList title="İçerik Boşlukları" icon={<AlertTriangle size={18} />} empty="Sonuçsuz arama yok — kapsam çok iyi!">
          {data.failedSearches.map((item) => <InfoRow key={item.query} label={item.query} value={`${formatNumber(item.count)} sonuçsuz`} danger />)}
        </InfoList>
        <InfoList title="En Çok Görüntülenen Makaleler (7 gün)" icon={<Eye size={18} />} empty="Henüz görüntülenme verisi yok" wide>
          {data.topArticles.map((article) => (
            <div key={article.articleId} className="flex items-center justify-between gap-4 text-sm">
              <Link to={`/articles/${article.slug}`} className="truncate text-blue-600 hover:underline">{article.title}</Link>
              <span className="shrink-0 text-zinc-500">{formatNumber(article.views)} görüntülenme</span>
            </div>
          ))}
        </InfoList>
      </div>

      <section className="mt-8 overflow-hidden rounded-xl border border-zinc-200 dark:border-zinc-800">
        <div className="flex flex-col gap-3 border-b border-zinc-200 p-4 dark:border-zinc-800 lg:flex-row lg:items-center lg:justify-between">
          <div>
            <h2 className="font-semibold text-zinc-900 dark:text-zinc-100">Detaylı kullanım</h2>
            <p className="text-xs text-zinc-500">Kullanım miktarı, kanal, işlem türü ve hata kırılımları</p>
          </div>
          <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
            <div className="flex rounded-lg bg-zinc-100 p-1 dark:bg-zinc-900">
              <TabButton active={detailTab === "users"} onClick={() => { setDetailTab("users"); setFilter(""); }}>Kullanıcılar ({usage.users.length})</TabButton>
              <TabButton active={detailTab === "integrations"} onClick={() => { setDetailTab("integrations"); setFilter(""); }}>Entegrasyonlar ({usage.integrations.length})</TabButton>
              <TabButton active={detailTab === "operations"} onClick={() => { setDetailTab("operations"); setFilter(""); }}>İşlemler ({usage.operations.length})</TabButton>
            </div>
            <div className="relative">
              <Search size={15} className="absolute left-3 top-2.5 text-zinc-400" />
              <input
                value={filter}
                onChange={(event) => setFilter(event.target.value)}
                placeholder="Filtrele..."
                aria-label="Kullanım detaylarını filtrele"
                className="w-full rounded-lg border border-zinc-200 bg-transparent py-2 pl-9 pr-3 text-sm outline-none focus:border-blue-500 dark:border-zinc-800 sm:w-52"
              />
            </div>
          </div>
        </div>
        {detailTab === "users" && <UserUsageTable rows={filteredDetails as AnalyticsUserUsage[]} />}
        {detailTab === "integrations" && <IntegrationUsageTable rows={filteredDetails as AnalyticsIntegrationUsage[]} />}
        {detailTab === "operations" && <OperationUsageTable rows={filteredDetails as AnalyticsOperationUsage[]} />}
      </section>

    </div>
  );
}

function UsageTrend({ rows }: { rows: AnalyticsDailyUsage[] }) {
  const max = Math.max(1, ...rows.map((row) => row.requests));
  return (
    <section className="mb-8 rounded-xl border border-zinc-200 p-5 dark:border-zinc-800">
      <div className="mb-5 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="font-semibold text-zinc-900 dark:text-zinc-100">Günlük kullanım eğilimi</h2>
          <p className="text-xs text-zinc-500">Oturum ve API anahtarı kaynaklı istekler</p>
        </div>
        <div className="flex gap-4 text-xs text-zinc-500">
          <Legend color="bg-blue-500" label="Oturum" />
          <Legend color="bg-violet-500" label="Entegrasyon" />
          <Legend color="bg-red-400" label="Hata" />
        </div>
      </div>
      <div className="overflow-x-auto pb-1">
        <div className="flex h-40 min-w-max items-end gap-1.5 border-b border-zinc-200 px-1 dark:border-zinc-800">
          {rows.map((row) => {
            const height = row.requests === 0 ? 0 : Math.max(5, (row.requests / max) * 120);
            const sessionShare = row.requests === 0 ? 0 : (row.sessionRequests / row.requests) * 100;
            return (
              <div key={row.date} className="flex w-7 flex-col items-center justify-end gap-1" title={`${formatDate(row.date)}: ${row.requests} istek, ${row.errors} hata`}>
                <div className="relative flex w-5 flex-col-reverse overflow-hidden rounded-t bg-zinc-100 dark:bg-zinc-900" style={{ height }}>
                  <div className="bg-blue-500" style={{ height: `${sessionShare}%` }} />
                  <div className="flex-1 bg-violet-500" />
                  {row.errors > 0 && <div className="absolute right-0 top-0 h-1.5 w-1.5 rounded-full bg-red-400" />}
                </div>
                <span className="text-[10px] tabular-nums text-zinc-400">{new Date(`${row.date}T00:00:00`).getDate()}</span>
              </div>
            );
          })}
        </div>
      </div>
    </section>
  );
}

function UserUsageTable({ rows }: { rows: AnalyticsUserUsage[] }) {
  return <UsageTable columns={["Kullanıcı", "Toplam", "Oturum / API", "REST / MCP", "Okuma / Yazma", "Hata", "Aktif", "En çok işlem", "Son kullanım"]} empty="Bu dönemde kullanıcı kullanımı yok.">
    {rows.map((row) => <tr key={row.userId} className="border-t border-zinc-100 dark:border-zinc-900">
      <IdentityCell primary={row.name} secondary={`${row.email} · ${row.role}`} />
      <NumberCell value={formatNumber(row.requests)} />
      <NumberCell value={`${formatCompact(row.sessionRequests)} / ${formatCompact(row.integrationRequests)}`} />
      <NumberCell value={`${formatCompact(row.restRequests)} / ${formatCompact(row.mcpCalls)}`} />
      <NumberCell value={`${formatCompact(row.readRequests)} / ${formatCompact(row.writeRequests)}`} />
      <ErrorCell errors={row.errors} rate={row.errorRate} />
      <NumberCell value={`${row.activeDays} gün`} secondary={`${row.integrationsUsed} entegrasyon`} />
      <OperationCell operation={row.topOperation} count={row.topOperationRequests} />
      <DateCell value={row.lastUsedAt} secondary={`${Math.round(row.averageDurationMs)} ms ort.`} />
    </tr>)}
  </UsageTable>;
}

function IntegrationUsageTable({ rows }: { rows: AnalyticsIntegrationUsage[] }) {
  return <UsageTable columns={["Entegrasyon", "Sahibi", "Toplam", "REST / MCP", "Okuma / Yazma", "Hata", "Aktif", "En çok işlem", "Son kullanım"]} empty="Bu dönemde entegrasyon kullanımı yok.">
    {rows.map((row) => <tr key={row.apiKeyId} className="border-t border-zinc-100 dark:border-zinc-900">
      <IdentityCell primary={row.name} secondary={row.apiKeyId} />
      <IdentityCell primary={row.ownerName || "—"} secondary={row.ownerEmail} />
      <NumberCell value={formatNumber(row.requests)} />
      <NumberCell value={`${formatCompact(row.restRequests)} / ${formatCompact(row.mcpCalls)}`} />
      <NumberCell value={`${formatCompact(row.readRequests)} / ${formatCompact(row.writeRequests)}`} />
      <ErrorCell errors={row.errors} rate={row.errorRate} />
      <NumberCell value={`${row.activeDays} gün`} />
      <OperationCell operation={row.topOperation} count={row.topOperationRequests} />
      <DateCell value={row.lastUsedAt} secondary={`${Math.round(row.averageDurationMs)} ms ort.`} />
    </tr>)}
  </UsageTable>;
}

function OperationUsageTable({ rows }: { rows: AnalyticsOperationUsage[] }) {
  return <UsageTable columns={["İşlem", "Kanal", "Toplam", "Kullanıcı", "Entegrasyon", "Hata", "Ort. süre", "Son kullanım"]} empty="Bu dönemde işlem kaydı yok.">
    {rows.map((row) => <tr key={`${row.channel}-${row.operation}`} className="border-t border-zinc-100 dark:border-zinc-900">
      <td className="max-w-80 px-4 py-3 font-mono text-xs text-zinc-700 dark:text-zinc-300"><span className="block truncate" title={row.operation}>{row.operation}</span></td>
      <td className="px-4 py-3"><ChannelBadge channel={row.channel} /></td>
      <NumberCell value={formatNumber(row.requests)} />
      <NumberCell value={formatNumber(row.uniqueUsers)} />
      <NumberCell value={formatNumber(row.uniqueIntegrations)} />
      <ErrorCell errors={row.errors} rate={row.errorRate} />
      <NumberCell value={`${Math.round(row.averageDurationMs)} ms`} />
      <DateCell value={row.lastUsedAt} />
    </tr>)}
  </UsageTable>;
}

function UsageTable({ columns, empty, children }: { columns: string[]; empty: string; children: React.ReactNode }) {
  const hasRows = Array.isArray(children) ? children.length > 0 : Boolean(children);
  if (!hasRows) return <p className="p-8 text-center text-sm text-zinc-500">{empty}</p>;
  return <div className="overflow-x-auto"><table className="w-full min-w-max text-left text-sm"><thead className="bg-zinc-50 text-xs text-zinc-500 dark:bg-zinc-900/60"><tr>{columns.map((column) => <th key={column} className="px-4 py-3 font-medium">{column}</th>)}</tr></thead><tbody>{children}</tbody></table></div>;
}

function IdentityCell({ primary, secondary }: { primary: string; secondary: string }) {
  return <td className="max-w-60 px-4 py-3"><p className="truncate font-medium text-zinc-800 dark:text-zinc-200" title={primary}>{primary}</p><p className="truncate text-xs text-zinc-500" title={secondary}>{secondary || "—"}</p></td>;
}

function NumberCell({ value, secondary }: { value: string; secondary?: string }) {
  return <td className="px-4 py-3 tabular-nums text-zinc-700 dark:text-zinc-300"><span className="whitespace-nowrap">{value}</span>{secondary && <span className="block text-xs text-zinc-500">{secondary}</span>}</td>;
}

function ErrorCell({ errors, rate }: { errors: number; rate: number }) {
  return <td className="px-4 py-3 tabular-nums"><span className={errors > 0 ? "text-red-600 dark:text-red-400" : "text-zinc-500"}>{formatNumber(errors)}</span><span className="block text-xs text-zinc-500">{formatPercent(rate)}</span></td>;
}

function OperationCell({ operation, count }: { operation: string | null; count: number }) {
  return <td className="max-w-64 px-4 py-3"><span className="block truncate font-mono text-xs text-zinc-700 dark:text-zinc-300" title={operation ?? ""}>{operation ?? "—"}</span>{operation && <span className="text-xs text-zinc-500">{formatNumber(count)} kez</span>}</td>;
}

function DateCell({ value, secondary }: { value: string; secondary?: string }) {
  return <td className="px-4 py-3 whitespace-nowrap text-xs text-zinc-600 dark:text-zinc-400">{formatDateTime(value)}{secondary && <span className="block text-zinc-500">{secondary}</span>}</td>;
}

function TabButton({ active, onClick, children }: { active: boolean; onClick: () => void; children: React.ReactNode }) {
  return <button type="button" onClick={onClick} className={`rounded-md px-3 py-1.5 text-xs font-medium transition ${active ? "bg-white text-zinc-900 shadow-sm dark:bg-zinc-800 dark:text-zinc-100" : "text-zinc-500 hover:text-zinc-800 dark:hover:text-zinc-200"}`}>{children}</button>;
}

function ChannelBadge({ channel }: { channel: string }) {
  return <span className={`rounded-full px-2 py-1 text-[11px] font-medium uppercase ${channel === "mcp" ? "bg-violet-100 text-violet-700 dark:bg-violet-950 dark:text-violet-300" : "bg-blue-100 text-blue-700 dark:bg-blue-950 dark:text-blue-300"}`}>{channel}</span>;
}

function Legend({ color, label }: { color: string; label: string }) {
  return <span className="flex items-center gap-1.5"><span className={`h-2 w-2 rounded-full ${color}`} />{label}</span>;
}

function MetricCard({ icon, label, value }: { icon: React.ReactNode; label: string; value: string }) {
  return <div className="rounded-xl border border-zinc-200 p-3 dark:border-zinc-800"><div className="mb-2 text-zinc-400">{icon}</div><p className="text-xl font-bold tabular-nums text-zinc-900 dark:text-zinc-100">{value}</p><p className="text-xs text-zinc-500">{label}</p></div>;
}

function InfoList({ title, icon, empty, wide, children }: { title: string; icon: React.ReactNode; empty: string; wide?: boolean; children: React.ReactNode }) {
  const hasChildren = Array.isArray(children) ? children.length > 0 : Boolean(children);
  return <section className={`rounded-xl border border-zinc-200 p-6 dark:border-zinc-800 ${wide ? "lg:col-span-2" : ""}`}><h2 className="mb-4 flex items-center gap-2 font-semibold text-zinc-900 dark:text-zinc-100">{icon}{title}</h2>{hasChildren ? <div className="space-y-2">{children}</div> : <p className="text-sm text-zinc-500">{empty}</p>}</section>;
}

function InfoRow({ label, value, danger }: { label: string; value: string; danger?: boolean }) {
  return <div className="flex items-center justify-between gap-4 text-sm"><span className="truncate text-zinc-700 dark:text-zinc-300">{label}</span><span className={`shrink-0 ${danger ? "text-red-500" : "text-zinc-500"}`}>{value}</span></div>;
}

function StatCard({ icon, label, value, color }: { icon: React.ReactNode; label: string; value: string; color: "blue" | "green" | "amber" | "purple" }) {
  const colors = {
    blue: "bg-blue-50 text-blue-600 dark:bg-blue-950 dark:text-blue-400",
    green: "bg-green-50 text-green-600 dark:bg-green-950 dark:text-green-400",
    amber: "bg-amber-50 text-amber-600 dark:bg-amber-950 dark:text-amber-400",
    purple: "bg-purple-50 text-purple-600 dark:bg-purple-950 dark:text-purple-400",
  };
  return <div className="rounded-xl border border-zinc-200 p-4 dark:border-zinc-800"><div className="flex items-center gap-3"><div className={`rounded-lg p-2 ${colors[color]}`}>{icon}</div><div><p className="text-2xl font-bold text-zinc-900 dark:text-zinc-100">{value}</p><p className="text-xs text-zinc-500">{label}</p></div></div></div>;
}

const formatNumber = (value: number) => value.toLocaleString("tr-TR");
const formatCompact = (value: number) => value.toLocaleString("tr-TR", { notation: "compact", maximumFractionDigits: 1 });
const formatPercent = (value: number) => `%${(value * 100).toLocaleString("tr-TR", { maximumFractionDigits: 1 })}`;
const formatDate = (value: string) => new Date(value).toLocaleDateString("tr-TR", { day: "2-digit", month: "short", year: "numeric" });
const formatDateTime = (value: string) => new Date(value).toLocaleString("tr-TR", { day: "2-digit", month: "short", hour: "2-digit", minute: "2-digit" });
