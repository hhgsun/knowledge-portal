import { useEffect, useState, useCallback } from "react";
import { useApi } from "../hooks/useApi";
import {
  RefreshCw, AlertTriangle, CheckCircle2, XCircle, Database,
  FileSearch, Boxes, ChevronDown, ChevronRight, Settings2, Workflow, Wrench,
  HardDrive, Bug, Search,
} from "lucide-react";
import { toast } from "sonner";
import { cn } from "../lib/utils";
import { SearchFlowModal } from "../components/layout/search-flow-modal";

interface PlanProbe {
  name: string;
  usesVectorIndex: boolean;
  plan: string;
  error: string | null;
}

interface FailingArticle {
  articleId: string;
  title: string | null;
  failureCount: number;
  nextRetryAt: string;
}

interface IndexHealth {
  name: string;
  table: string;
  exists: boolean;
  valid: boolean;
  size: string | null;
  definition: string | null;
}

interface Traffic {
  searchType: string;
  total: number;
  zeroResults: number;
  p50Ms: number;
  p95Ms: number;
}

interface Diagnostics {
  embedding: {
    published: number; indexed: number; pending: number;
    failing: number; topFailures: FailingArticle[];
  };
  fullText: { published: number; indexed: number; missing: number };
  vector: {
    chunks: number;
    articles: number;
    orphanChunks: number;
    tableSize: string | null;
    denormalizedFilterColumns: boolean;
  };
  indexes: IndexHealth[];
  plans: PlanProbe[];
  traffic: Traffic[];
  settings: { name: string; value: string; note: string | null }[];
  warnings: string[];
}

interface StorageHealth {
  files: number; bytes: number; sampleMissingFiles: number; pendingExtraction: number;
  failedExtraction: number; truncatedExtraction: number; checksumsVerified: number;
  checksumMismatches: number; freeBytes: number; rootPath: string;
}

interface RagDebugSnapshot {
  queryPlan: { originalQuery: string; rewrittenQuery: string; queries: string[]; expansions: string[]; isComplex: boolean; prefersFreshSources: boolean; extractedFilters: { tags: string[]; authors: string[]; contentTypes: string[] } };
  mode: string; retrievedCount: number; authorizedCount: number; expandedParentCount: number;
  expandedParents: string[]; contextWords: number; budgetTruncated: boolean;
  candidates: { rank: number; articleId: string; title: string; chunkId?: string; sourceName?: string; sourceLocation?: string; score: number; matchType: string; passage: string }[];
  selectedContext: { evidenceId: string; articleId: string; chunkId?: string; title: string; sourceName?: string; sourceLocation?: string; wordCount: number; passage: string }[];
}

/** Coverage bar: how much of the corpus each index actually covers. */
function Coverage({ done, total }: { done: number; total: number }) {
  const pct = total === 0 ? 100 : Math.round((done / total) * 100);
  return (
    <div className="space-y-1">
      <div className="h-2 rounded-full bg-zinc-100 dark:bg-zinc-800 overflow-hidden">
        <div
          className={cn("h-full rounded-full transition-all",
            pct === 100 ? "bg-emerald-500" : pct >= 90 ? "bg-amber-500" : "bg-red-500")}
          style={{ width: `${pct}%` }}
        />
      </div>
      <p className="text-xs text-zinc-500 dark:text-zinc-400">
        {done.toLocaleString("tr-TR")} / {total.toLocaleString("tr-TR")} makale ({pct}%)
      </p>
    </div>
  );
}

function Card({ icon, title, children }: { icon: React.ReactNode; title: string; children: React.ReactNode }) {
  return (
    <div className="rounded-lg border border-zinc-200 dark:border-zinc-700 p-4 space-y-3">
      <div className="flex items-center gap-2 text-zinc-700 dark:text-zinc-300">
        {icon}
        <h2 className="text-sm font-semibold">{title}</h2>
      </div>
      {children}
    </div>
  );
}

function Stat({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="flex items-baseline justify-between gap-4 text-sm">
      <span className="text-zinc-500 dark:text-zinc-400">{label}</span>
      <span className="font-medium text-zinc-900 dark:text-zinc-100 tabular-nums">{value}</span>
    </div>
  );
}

export default function SearchDiagnosticsPage() {
  const { fetchWithAuth } = useApi();
  const [data, setData] = useState<Diagnostics | null>(null);
  const [loading, setLoading] = useState(true);
  const [openPlan, setOpenPlan] = useState<string | null>(null);
  const [showSettings, setShowSettings] = useState(false);
  const [showFlow, setShowFlow] = useState(false);
  const [repairing, setRepairing] = useState(false);
  const [storage, setStorage] = useState<StorageHealth | null>(null);
  const [debugQuery, setDebugQuery] = useState("");
  const [debugResult, setDebugResult] = useState<RagDebugSnapshot | null>(null);
  const [debugging, setDebugging] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [res, storageRes] = await Promise.all([
        fetchWithAuth("/api/search/diagnostics"),
        fetchWithAuth("/api/search/storage-status"),
      ]);
      if (res.ok) setData(await res.json());
      if (storageRes.ok) setStorage(await storageRes.json());
    } catch {
      // handled by useApi
    } finally {
      setLoading(false);
    }
  }, [fetchWithAuth]);

  useEffect(() => { load(); }, [load]);

  const repairIndexing = async () => {
    const confirmed = confirm(
      "Yalnızca indeksi eksik, yeniden denemede bekleyen, hata almış veya lease süresi geçmiş işler onarılacak. " +
      "Güncel makaleler ve halen aktif çalışan işler değiştirilmeyecek. Devam edilsin mi?"
    );
    if (!confirmed) return;

    setRepairing(true);
    try {
      const res = await fetchWithAuth("/api/search/repair-indexing", {
        method: "POST",
        noRetry: true,
      });
      const result = await res.json();
      if (!res.ok) {
        toast.error(result.error || "İndeks işleri onarılamadı");
        return;
      }

      toast.success(
        result.articlesRepaired > 0
          ? `${result.articlesRepaired.toLocaleString("tr-TR")} indeks işi onarıldı`
          : "Onarılabilir indeks işi bulunamadı"
      );
      await load();
    } catch {
      // Network/auth feedback is handled by useApi.
    } finally {
      setRepairing(false);
    }
  };

  const fmt = (n: number) => n.toLocaleString("tr-TR");
  const fmtBytes = (n: number) => n >= 1024 ** 3 ? `${(n / 1024 ** 3).toFixed(1)} GB` : n >= 1024 ** 2 ? `${(n / 1024 ** 2).toFixed(1)} MB` : `${Math.round(n / 1024)} KB`;

  const runDebug = async () => {
    if (!debugQuery.trim()) return;
    setDebugging(true); setDebugResult(null);
    try {
      const res = await fetchWithAuth(`/api/search/rag-debug?q=${encodeURIComponent(debugQuery.trim())}`);
      const body = await res.json();
      if (!res.ok) return toast.error(body.error || "RAG debug çalıştırılamadı");
      setDebugResult(body);
    } finally { setDebugging(false); }
  };

  return (
    <div className="max-w-5xl mx-auto space-y-6">
      <SearchFlowModal open={showFlow} onClose={() => setShowFlow(false)} />
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-zinc-900 dark:text-zinc-100">Arama Teşhisi</h1>
          <p className="text-sm text-zinc-500 dark:text-zinc-400 mt-1">
            Tam metin ve vektör indekslerinin durumu, etkin ayarlar ve sorgu planı kontrolü
          </p>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={() => setShowFlow(true)}
            className="flex items-center gap-2 px-3 py-2 text-sm rounded-lg border border-zinc-200 dark:border-zinc-700 hover:bg-zinc-50 dark:hover:bg-zinc-800 transition-colors"
          >
            <Workflow size={16} />
            Nasıl çalışıyor?
          </button>
          <button
            onClick={load}
            disabled={loading}
            className="flex items-center gap-2 px-3 py-2 text-sm rounded-lg border border-zinc-200 dark:border-zinc-700 hover:bg-zinc-50 dark:hover:bg-zinc-800 transition-colors"
          >
            <RefreshCw size={16} className={cn(loading && "animate-spin")} />
            Yenile
          </button>
        </div>
      </div>

      {loading && !data ? (
        <div className="space-y-3">
          {[1, 2, 3].map((i) => (
            <div key={i} className="h-28 bg-zinc-100 dark:bg-zinc-800 rounded-lg animate-pulse" />
          ))}
        </div>
      ) : !data ? (
        <p className="text-sm text-zinc-500 dark:text-zinc-400">Teşhis bilgisi alınamadı.</p>
      ) : (
        <>
          {data.warnings.length > 0 && (
            <div className="rounded-lg border border-amber-300 dark:border-amber-700/60 bg-amber-50 dark:bg-amber-900/20 p-4 space-y-2">
              <div className="flex items-center gap-2 text-amber-800 dark:text-amber-300">
                <AlertTriangle size={16} />
                <h2 className="text-sm font-semibold">Dikkat edilmesi gerekenler</h2>
              </div>
              <ul className="space-y-1.5">
                {data.warnings.map((w, i) => (
                  <li key={i} className="text-sm text-amber-900 dark:text-amber-200/90 leading-relaxed">• {w}</li>
                ))}
              </ul>
            </div>
          )}

          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <Card icon={<FileSearch size={16} />} title="Tam metin indeksi">
              <Coverage done={data.fullText.indexed} total={data.fullText.published} />
              <Stat label="İndekssiz makale" value={fmt(data.fullText.missing)} />
            </Card>

            <Card icon={<Boxes size={16} />} title="Embedding kuyruğu">
              <Coverage done={data.embedding.indexed} total={data.embedding.published} />
              <Stat label="Sırada bekleyen" value={fmt(data.embedding.pending)} />
              <Stat
                label="Sürekli hata alan"
                value={
                  <span className={cn(data.embedding.failing > 0 && "text-red-600 dark:text-red-400")}>
                    {fmt(data.embedding.failing)}
                  </span>
                }
              />
              {data.embedding.topFailures.length > 0 && (
                <div className="pt-2 border-t border-zinc-100 dark:border-zinc-800 space-y-1.5">
                  {data.embedding.topFailures.map((f) => (
                    <div key={f.articleId} className="text-xs">
                      <div className="text-zinc-700 dark:text-zinc-300 truncate">
                        {f.title ?? f.articleId}
                      </div>
                      <div className="text-zinc-400">
                        {f.failureCount} hata · sonraki deneme{" "}
                        {new Date(f.nextRetryAt).toLocaleString("tr-TR")}
                      </div>
                    </div>
                  ))}
                </div>
              )}
              {(data.embedding.pending > 0 || data.embedding.failing > 0 || data.fullText.missing > 0) && (
                <div className="pt-2 border-t border-zinc-100 dark:border-zinc-800 space-y-2">
                  <button
                    type="button"
                    onClick={repairIndexing}
                    disabled={repairing}
                    className="w-full flex items-center justify-center gap-2 px-3 py-2 text-sm font-medium rounded-lg border border-amber-300 dark:border-amber-700 text-amber-800 dark:text-amber-300 bg-amber-50 dark:bg-amber-900/20 hover:bg-amber-100 dark:hover:bg-amber-900/35 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                  >
                    {repairing
                      ? <RefreshCw size={16} className="animate-spin" />
                      : <Wrench size={16} />}
                    {repairing ? "Onarılıyor…" : "Eksik/takılan indeksleri onar"}
                  </button>
                  <p className="text-xs text-zinc-500 dark:text-zinc-400 leading-relaxed">
                    Yalnız sorunlu kuyruk işlerini yeniden açar; güncel makaleleri yeniden indekslemez.
                  </p>
                </div>
              )}
            </Card>

            <Card icon={<Database size={16} />} title="Vektör deposu">
              <Stat label="Chunk sayısı" value={fmt(data.vector.chunks)} />
              <Stat label="Kapsanan makale" value={fmt(data.vector.articles)} />
              <Stat label="Ortalama chunk/makale" value={
                data.vector.articles === 0 ? "—" : (data.vector.chunks / data.vector.articles).toFixed(1)
              } />
              <Stat label="Tablo boyutu" value={data.vector.tableSize ?? "—"} />
              {data.vector.orphanChunks > 0 && (
                <Stat label="Sahipsiz chunk" value={fmt(data.vector.orphanChunks)} />
              )}
            </Card>

            {storage && <Card icon={<HardDrive size={16} />} title="Ek dosya çıkarımı">
              <Stat label="Dosya" value={fmt(storage.files)} />
              <Stat label="Kullanılan alan" value={fmtBytes(storage.bytes)} />
              <Stat label="Bekleyen" value={fmt(storage.pendingExtraction)} />
              <Stat label="Başarısız" value={<span className={cn(storage.failedExtraction > 0 && "text-red-600")}>{fmt(storage.failedExtraction)}</span>} />
              <Stat label="Metni kırpılan" value={<span className={cn(storage.truncatedExtraction > 0 && "text-amber-600")}>{fmt(storage.truncatedExtraction)}</span>} />
              <Stat label="Eksik dosya (örneklem)" value={fmt(storage.sampleMissingFiles)} />
              <Stat label="Checksum uyuşmazlığı" value={fmt(storage.checksumMismatches)} />
              <Stat label="Boş disk" value={fmtBytes(storage.freeBytes)} />
            </Card>}

            <Card icon={<Settings2 size={16} />} title="Filtre kolonları">
              <div className="flex items-start gap-2 text-sm">
                {data.vector.denormalizedFilterColumns ? (
                  <>
                    <CheckCircle2 size={16} className="text-emerald-500 shrink-0 mt-0.5" />
                    <span className="text-zinc-600 dark:text-zinc-300">
                      Denormalize edilmiş. Filtreli semantik arama tek tablo üzerinden çalışır.
                    </span>
                  </>
                ) : (
                  <>
                    <XCircle size={16} className="text-red-500 shrink-0 mt-0.5" />
                    <span className="text-zinc-600 dark:text-zinc-300">
                      Eksik. Filtreli arama <code>articles</code> tablosuna join yapmak zorunda kalır.
                    </span>
                  </>
                )}
              </div>
            </Card>
          </div>

          <div className="space-y-2">
            <h2 className="text-sm font-semibold text-zinc-700 dark:text-zinc-300">Arama indeksleri</h2>
            <div className="rounded-lg border border-zinc-200 dark:border-zinc-700 divide-y divide-zinc-100 dark:divide-zinc-800">
              {data.indexes.map((ix) => (
                <div key={ix.name} className="flex items-center gap-3 px-4 py-3">
                  {ix.exists && ix.valid ? (
                    <CheckCircle2 size={16} className="text-emerald-500 shrink-0" />
                  ) : (
                    <XCircle size={16} className="text-red-500 shrink-0" />
                  )}
                  <div className="min-w-0 flex-1">
                    <div className="text-sm font-mono text-zinc-900 dark:text-zinc-100 truncate">{ix.name}</div>
                    <div className="text-xs text-zinc-400">{ix.table}</div>
                  </div>
                  <span className="text-xs text-zinc-500 dark:text-zinc-400 shrink-0">
                    {!ix.exists ? "yok" : !ix.valid ? "geçersiz" : (ix.size ?? "—")}
                  </span>
                </div>
              ))}
            </div>
          </div>

          <section className="space-y-3 rounded-lg border border-zinc-200 dark:border-zinc-700 p-4">
            <div className="flex items-center gap-2"><Bug size={16}/><div><h2 className="text-sm font-semibold">Korumalı RAG debug</h2><p className="text-xs text-zinc-500">LLM çağrısı yapmadan query planı, rerank adayları, komşu genişletme ve final context'i gösterir.</p></div></div>
            <div className="flex gap-2"><input value={debugQuery} onChange={e=>setDebugQuery(e.target.value)} onKeyDown={e=>{if(e.key==="Enter")void runDebug();}} placeholder="Teşhis edilecek soru" className="flex-1 rounded-lg border border-zinc-300 dark:border-zinc-700 bg-transparent px-3 py-2 text-sm"/><button onClick={()=>void runDebug()} disabled={debugging||!debugQuery.trim()} className="flex items-center gap-2 rounded-lg bg-blue-600 px-3 py-2 text-sm text-white disabled:opacity-50">{debugging?<RefreshCw size={15} className="animate-spin"/>:<Search size={15}/>}Çalıştır</button></div>
            {debugResult && <div className="space-y-4 text-xs"><div className="grid md:grid-cols-4 gap-2"><Stat label="Mod" value={debugResult.mode}/><Stat label="Aday / yetkili" value={`${debugResult.retrievedCount} / ${debugResult.authorizedCount}`}/><Stat label="Genişletilen parent" value={debugResult.expandedParentCount}/><Stat label="Context" value={`${debugResult.contextWords} kelime`}/></div><div className="rounded-lg bg-zinc-50 dark:bg-zinc-900 p-3 space-y-1"><div><b>Orijinal:</b> {debugResult.queryPlan.originalQuery}</div><div><b>Rewrite:</b> {debugResult.queryPlan.rewrittenQuery}</div><div><b>Alt sorgular:</b> {debugResult.queryPlan.queries.join(" · ")}</div><div><b>Expansion:</b> {debugResult.queryPlan.expansions.join(", ") || "—"}</div><div><b>Filtreler:</b> {[...debugResult.queryPlan.extractedFilters.tags,...debugResult.queryPlan.extractedFilters.authors,...debugResult.queryPlan.extractedFilters.contentTypes].join(", ")||"—"}</div></div><details><summary className="cursor-pointer font-medium">Rerank adayları ({debugResult.candidates.length})</summary><div className="mt-2 max-h-80 overflow-auto divide-y dark:divide-zinc-800">{debugResult.candidates.map(x=><div key={`${x.rank}-${x.chunkId}`} className="py-2"><div className="font-medium">#{x.rank} {x.title} · {(x.score*100).toFixed(1)}% · {x.matchType}</div><div className="text-zinc-500">{x.sourceName||"makale"} · {x.sourceLocation||"—"} · {x.chunkId}</div><p className="mt-1 text-zinc-600 dark:text-zinc-400 whitespace-pre-wrap">{x.passage}</p></div>)}</div></details><details open><summary className="cursor-pointer font-medium">Seçilen context ({debugResult.selectedContext.length})</summary><div className="mt-2 max-h-96 overflow-auto divide-y dark:divide-zinc-800">{debugResult.selectedContext.map(x=><div key={x.evidenceId} className="py-2"><div className="font-medium text-blue-600">{x.evidenceId} · {x.title} · {x.wordCount} kelime</div><div className="text-zinc-500">{x.sourceName||"makale"} · {x.sourceLocation||"—"} · {x.chunkId}</div><p className="mt-1 whitespace-pre-wrap text-zinc-600 dark:text-zinc-400">{x.passage}</p></div>)}</div></details></div>}
          </section>

          {/* Traffic: the only evidence of what users actually experience. */}
          <div className="space-y-2">
            <h2 className="text-sm font-semibold text-zinc-700 dark:text-zinc-300">
              Gerçek arama trafiği (son 7 gün)
            </h2>
            {data.traffic.length === 0 ? (
              <p className="text-sm text-zinc-500 dark:text-zinc-400">Bu dönemde kayıtlı arama yok.</p>
            ) : (
              <div className="rounded-lg border border-zinc-200 dark:border-zinc-700 overflow-x-auto">
                <table className="w-full text-sm">
                  <thead className="text-xs text-zinc-500 dark:text-zinc-400 border-b border-zinc-200 dark:border-zinc-700">
                    <tr>
                      <th className="text-left font-medium px-4 py-2">Tür</th>
                      <th className="text-right font-medium px-4 py-2">Arama</th>
                      <th className="text-right font-medium px-4 py-2">Sonuçsuz</th>
                      <th className="text-right font-medium px-4 py-2">p50</th>
                      <th className="text-right font-medium px-4 py-2">p95</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-zinc-100 dark:divide-zinc-800">
                    {data.traffic.map((t) => (
                      <tr key={t.searchType}>
                        <td className="px-4 py-2 font-medium text-zinc-900 dark:text-zinc-100">{t.searchType}</td>
                        <td className="px-4 py-2 text-right tabular-nums text-zinc-600 dark:text-zinc-300">{fmt(t.total)}</td>
                        <td className="px-4 py-2 text-right tabular-nums text-zinc-600 dark:text-zinc-300">
                          {fmt(t.zeroResults)}
                          {t.total > 0 && (
                            <span className="text-zinc-400"> (%{Math.round((t.zeroResults / t.total) * 100)})</span>
                          )}
                        </td>
                        <td className="px-4 py-2 text-right tabular-nums text-zinc-600 dark:text-zinc-300">{fmt(t.p50Ms)} ms</td>
                        <td className={cn("px-4 py-2 text-right tabular-nums",
                          t.p95Ms >= 2000 ? "text-red-600 dark:text-red-400 font-medium" : "text-zinc-600 dark:text-zinc-300")}>
                          {fmt(t.p95Ms)} ms
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
            <p className="text-xs text-zinc-500 dark:text-zinc-400">
              Semantic, hybrid ve RAG süreleri Ollama model çağrılarını da içerir — yavaşlık her zaman
              veritabanı kaynaklı değildir.
            </p>
          </div>

          {/* Plan probes — the check that matters most, because this failure is silent. */}
          <div className="space-y-2">
            <h2 className="text-sm font-semibold text-zinc-700 dark:text-zinc-300">Sorgu planı kontrolü</h2>
            <p className="text-xs text-zinc-500 dark:text-zinc-400">
              HNSW indeksi kullanılmadığında arama yanlış sonuç vermez — sadece tüm embedding'leri tarar.
              Küçük korpusta bu normaldir; büyüdükçe sorunun tek işareti budur.
            </p>
            {data.plans.map((probe) => (
              <div key={probe.name} className="rounded-lg border border-zinc-200 dark:border-zinc-700 overflow-hidden">
                <button
                  onClick={() => setOpenPlan(openPlan === probe.name ? null : probe.name)}
                  className="w-full flex items-center gap-2 px-4 py-3 text-left hover:bg-zinc-50 dark:hover:bg-zinc-800/50 transition-colors"
                >
                  {probe.error ? (
                    <AlertTriangle size={16} className="text-amber-500 shrink-0" />
                  ) : probe.usesVectorIndex ? (
                    <CheckCircle2 size={16} className="text-emerald-500 shrink-0" />
                  ) : (
                    <XCircle size={16} className="text-red-500 shrink-0" />
                  )}
                  <span className="text-sm font-medium text-zinc-900 dark:text-zinc-100 flex-1">
                    {probe.name}
                  </span>
                  <span className="text-xs text-zinc-500 dark:text-zinc-400">
                    {probe.error ? "ölçülemedi" : probe.usesVectorIndex ? "HNSW indeksi kullanılıyor" : "tam tarama"}
                  </span>
                  {!probe.error && (openPlan === probe.name
                    ? <ChevronDown size={14} className="text-zinc-400" />
                    : <ChevronRight size={14} className="text-zinc-400" />)}
                </button>
                {probe.error && (
                  <p className="px-4 pb-3 text-sm text-zinc-500 dark:text-zinc-400">{probe.error}</p>
                )}
                {openPlan === probe.name && probe.plan && (
                  <pre className="px-4 pb-4 text-xs font-mono text-zinc-600 dark:text-zinc-300 overflow-x-auto whitespace-pre">
                    {probe.plan}
                  </pre>
                )}
              </div>
            ))}
          </div>

          <div className="rounded-lg border border-zinc-200 dark:border-zinc-700 overflow-hidden">
            <button
              onClick={() => setShowSettings(!showSettings)}
              className="w-full flex items-center gap-2 px-4 py-3 text-left hover:bg-zinc-50 dark:hover:bg-zinc-800/50 transition-colors"
            >
              <Settings2 size={16} className="text-zinc-400" />
              <span className="text-sm font-medium text-zinc-900 dark:text-zinc-100 flex-1">
                Etkin ayarlar ({data.settings.length})
              </span>
              {showSettings ? <ChevronDown size={14} className="text-zinc-400" />
                            : <ChevronRight size={14} className="text-zinc-400" />}
            </button>
            {showSettings && (
              <div className="border-t border-zinc-200 dark:border-zinc-700 divide-y divide-zinc-100 dark:divide-zinc-800">
                {data.settings.map((s) => (
                  <div key={s.name} className="flex items-center gap-4 px-4 py-2 text-sm">
                    <span className="font-mono text-xs text-zinc-600 dark:text-zinc-300 flex-1 truncate">{s.name}</span>
                    <span className="font-medium text-zinc-900 dark:text-zinc-100 tabular-nums">{s.value}</span>
                    {s.note && <span className="text-xs text-zinc-400 w-20 text-right shrink-0">{s.note}</span>}
                  </div>
                ))}
              </div>
            )}
          </div>

          {data.indexes.filter((i) => i.definition).map((i) => (
            <p key={i.name} className="text-xs font-mono text-zinc-400 dark:text-zinc-500 break-all">
              {i.definition}
            </p>
          ))}
        </>
      )}
    </div>
  );
}
