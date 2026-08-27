import { useCallback, useEffect, useState } from "react";
import { Play, Plus, Save, Trash2, RefreshCw } from "lucide-react";
import { toast } from "sonner";
import { useApi } from "../hooks/useApi";

type DatasetSummary = { id: string; name: string; description: string; version: string; caseCount: number };
type Run = { id: string; datasetName: string; status: string; totalCases: number; completedCases: number; metrics?: Metrics; error?: string; createdAt: string };
type Metrics = { recallAtK: number; mrr: number; ndcgAtK: number; factCoverage: number; citationCoverage: number; groundingCoverage: number; refusalAccuracy: number; forbiddenFactPassRate: number; p50LatencyMs: number; p95LatencyMs: number; passed: boolean; failedGates: string[] };
type FeedbackSummary = {
  days: number; total: number; helpful: number; notHelpful: number; helpfulRate: number;
  averageResponseTimeMs: number; reasons: { reason: string; count: number }[];
  grounding: { status: string; count: number; helpfulRate: number }[];
  configurations: { promptVersion?: string; retrievalVersion?: string; reranker?: string; indexProfile?: string; count: number; helpfulRate: number }[];
  assistant: {
    total: number; helpful: number; notHelpful: number; helpfulRate: number; averageResponseTimeMs: number;
    reasons: { reason: string; count: number }[];
    routes: { route: string; source: string; count: number; helpfulRate: number }[];
    corrections: { route: string; count: number }[];
  };
};

const defaultThresholds = { recallAtK: .8, mrr: .75, ndcgAtK: .75, factCoverage: .7, citationCoverage: .8, groundingCoverage: .8, refusalAccuracy: .9, forbiddenFactPassRate: 1, p95LatencyMs: 30000 };
const exampleCases = [{ id: "ornek-1", category: "focused", question: "API key hangi header ile gönderilir?", expectedSourceSlugs: ["api-kullanim-kilavuzu"], expectedFacts: ["X-API-Key"], forbiddenFacts: [], expectedRefusal: false, filters: { tag: [], authorIds: [], contentType: [] } }];

export default function RagEvaluationsPage() {
  const { fetchWithAuth } = useApi();
  const [datasets, setDatasets] = useState<DatasetSummary[]>([]);
  const [runs, setRuns] = useState<Run[]>([]);
  const [id, setId] = useState<string>();
  const [name, setName] = useState("Yeni RAG Kalite Seti");
  const [description, setDescription] = useState("");
  const [version, setVersion] = useState("1.0.0");
  const [cases, setCases] = useState(JSON.stringify(exampleCases, null, 2));
  const [thresholds, setThresholds] = useState(JSON.stringify(defaultThresholds, null, 2));
  const [busy, setBusy] = useState(false);
  const [feedback, setFeedback] = useState<FeedbackSummary>();

  const load = useCallback(async () => {
    const [d, r, f] = await Promise.all([fetchWithAuth("/api/admin/rag-evaluations/datasets"), fetchWithAuth("/api/admin/rag-evaluations/runs"), fetchWithAuth("/api/admin/rag-evaluations/feedback-summary?days=30")]);
    if (d.ok) setDatasets((await d.json()).datasets);
    if (r.ok) setRuns((await r.json()).runs.map((x: Run & { metricsJson?: string }) => ({ ...x, metrics: x.metricsJson ? JSON.parse(x.metricsJson) : undefined })));
    if (f.ok) setFeedback(await f.json());
  }, [fetchWithAuth]);
  useEffect(() => { void load(); }, [load]);
  useEffect(() => { if (!runs.some(r => r.status === "pending" || r.status === "running")) return; const timer = setInterval(() => void load(), 2000); return () => clearInterval(timer); }, [runs, load]);

  async function selectDataset(datasetId: string) {
    const res = await fetchWithAuth(`/api/admin/rag-evaluations/datasets/${datasetId}`); if (!res.ok) return toast.error("Dataset yüklenemedi");
    const d = await res.json(); setId(d.id); setName(d.name); setDescription(d.description); setVersion(d.version); setCases(JSON.stringify(d.cases, null, 2)); setThresholds(JSON.stringify(d.thresholds, null, 2));
  }
  function fresh() { setId(undefined); setName("Yeni RAG Kalite Seti"); setDescription(""); setVersion("1.0.0"); setCases(JSON.stringify(exampleCases, null, 2)); setThresholds(JSON.stringify(defaultThresholds, null, 2)); }
  async function save() {
    try {
      setBusy(true); const body = JSON.stringify({ name, description, version, cases: JSON.parse(cases), thresholds: JSON.parse(thresholds) });
      const res = await fetchWithAuth(id ? `/api/admin/rag-evaluations/datasets/${id}` : "/api/admin/rag-evaluations/datasets", { method: id ? "PUT" : "POST", body, noRetry: true });
      const data = await res.json(); if (!res.ok) throw new Error(data.error); setId(data.id); toast.success("Dataset kaydedildi"); await load();
    } catch (e) { toast.error(e instanceof Error ? e.message : "Geçersiz JSON"); } finally { setBusy(false); }
  }
  async function run() { if (!id) return toast.error("Önce dataset'i kaydedin"); const res = await fetchWithAuth(`/api/admin/rag-evaluations/datasets/${id}/runs`, { method: "POST", noRetry: true }); const data = await res.json(); if (!res.ok) return toast.error(data.error); toast.success("Değerlendirme kuyruğa alındı"); await load(); }
  async function remove() { if (!id || !confirm("Dataset ve geçmiş çalışmaları silinsin mi?")) return; const res = await fetchWithAuth(`/api/admin/rag-evaluations/datasets/${id}`, { method: "DELETE", noRetry: true }); if (res.ok) { fresh(); await load(); toast.success("Dataset silindi"); } }

  const percent = (v: number) => `${(v * 100).toFixed(1)}%`;
  return <div className="max-w-7xl mx-auto p-6 space-y-6">
    <div className="flex items-center justify-between"><div><h1 className="text-2xl font-bold">RAG Kalite Değerlendirmesi</h1><p className="text-sm text-zinc-500">Golden dataset oluşturun, gerçek RAG zincirini çalıştırın ve regresyonları ölçün.</p></div><button onClick={fresh} className="flex gap-2 px-3 py-2 rounded-lg bg-blue-600 text-white"><Plus size={16}/>Yeni</button></div>
    {feedback && <section className="border rounded-xl dark:border-zinc-800 p-4 space-y-4">
      <div className="flex items-center justify-between"><div><h2 className="font-semibold">Gerçek kullanıcı geri bildirimi</h2><p className="text-xs text-zinc-500">Son {feedback.days} gün · golden dataset sonuçlarını üretim sinyaliyle birlikte değerlendirin.</p></div><span className={`text-lg font-semibold ${feedback.helpfulRate >= .7 ? "text-green-600" : "text-amber-600"}`}>{percent(feedback.helpfulRate)} yararlı</span></div>
      <div className="grid grid-cols-2 md:grid-cols-4 gap-3 text-sm"><div><div className="text-zinc-500">Toplam</div><div className="font-semibold">{feedback.total}</div></div><div><div className="text-zinc-500">Yararlı</div><div className="font-semibold text-green-600">{feedback.helpful}</div></div><div><div className="text-zinc-500">Yararlı değil</div><div className="font-semibold text-red-600">{feedback.notHelpful}</div></div><div><div className="text-zinc-500">Ort. gecikme</div><div className="font-semibold">{Math.round(feedback.averageResponseTimeMs)} ms</div></div></div>
      {feedback.reasons.length > 0 && <div className="flex flex-wrap gap-2">{feedback.reasons.map(x=><span key={x.reason} className="rounded-full bg-zinc-100 dark:bg-zinc-800 px-2 py-1 text-xs">{x.reason}: {x.count}</span>)}</div>}
      <div className="grid md:grid-cols-2 gap-4 text-xs">
        <div><h3 className="font-medium mb-2">Grounding cohort'ları</h3><div className="space-y-1">{feedback.grounding.map(x=><div key={x.status} className="flex justify-between rounded bg-zinc-50 dark:bg-zinc-900 px-2 py-1"><span>{x.status} · {x.count}</span><span>{percent(x.helpfulRate)} yararlı</span></div>)}</div></div>
        <div><h3 className="font-medium mb-2">Retrieval / reranker cohort'ları</h3><div className="space-y-1">{feedback.configurations.map((x, i)=><div key={`${x.retrievalVersion}-${x.reranker}-${i}`} className="rounded bg-zinc-50 dark:bg-zinc-900 px-2 py-1"><div className="flex justify-between"><span className="truncate">{x.retrievalVersion ?? "retrieval bilinmiyor"} · {x.reranker ?? "reranker bilinmiyor"}</span><span className="ml-2 shrink-0">{percent(x.helpfulRate)} / {x.count}</span></div><div className="truncate text-zinc-500">prompt: {x.promptVersion ?? "—"} · index: {x.indexProfile ?? "—"}</div></div>)}</div></div>
      </div>
      <div className="border-t dark:border-zinc-800 pt-4 space-y-3">
        <div className="flex items-center justify-between"><div><h3 className="font-medium">Assistant yönlendirme geri bildirimi</h3><p className="text-xs text-zinc-500">Rota doğruluğunu RAG yanıt kalitesinden ayrı izler.</p></div><span className="text-sm font-semibold">{percent(feedback.assistant.helpfulRate)} yararlı · {feedback.assistant.total} oy</span></div>
        <div className="grid md:grid-cols-2 gap-4 text-xs">
          <div className="space-y-1">{feedback.assistant.routes.map(x=><div key={`${x.route}-${x.source}`} className="flex justify-between rounded bg-zinc-50 dark:bg-zinc-900 px-2 py-1"><span>{x.route} · {x.source} · {x.count}</span><span>{percent(x.helpfulRate)}</span></div>)}</div>
          <div className="flex flex-wrap content-start gap-2">{feedback.assistant.reasons.map(x=><span key={x.reason} className="rounded-full bg-zinc-100 dark:bg-zinc-800 px-2 py-1">{x.reason}: {x.count}</span>)}{feedback.assistant.corrections.map(x=><span key={x.route} className="rounded-full bg-blue-50 text-blue-700 dark:bg-blue-950 dark:text-blue-300 px-2 py-1">önerilen {x.route}: {x.count}</span>)}</div>
        </div>
      </div>
    </section>}
    <div className="grid lg:grid-cols-[260px_1fr] gap-6">
      <aside className="border rounded-xl dark:border-zinc-800 p-3 space-y-2">{datasets.map(d => <button key={d.id} onClick={() => void selectDataset(d.id)} className={`w-full text-left p-3 rounded-lg ${id === d.id ? "bg-blue-50 dark:bg-blue-950" : "hover:bg-zinc-50 dark:hover:bg-zinc-900"}`}><div className="font-medium text-sm">{d.name}</div><div className="text-xs text-zinc-500">v{d.version} · {d.caseCount} vaka</div></button>)}</aside>
      <section className="border rounded-xl dark:border-zinc-800 p-5 space-y-4">
        <div className="grid md:grid-cols-3 gap-3"><input value={name} onChange={e=>setName(e.target.value)} className="border rounded-lg p-2 dark:bg-zinc-900 dark:border-zinc-700" placeholder="Dataset adı"/><input value={version} onChange={e=>setVersion(e.target.value)} className="border rounded-lg p-2 dark:bg-zinc-900 dark:border-zinc-700" placeholder="Versiyon"/><input value={description} onChange={e=>setDescription(e.target.value)} className="border rounded-lg p-2 dark:bg-zinc-900 dark:border-zinc-700" placeholder="Açıklama"/></div>
        <div><label className="text-sm font-medium">Vakalar (JSON)</label><textarea value={cases} onChange={e=>setCases(e.target.value)} rows={16} spellCheck={false} className="mt-1 w-full font-mono text-xs border rounded-lg p-3 dark:bg-zinc-950 dark:border-zinc-700"/></div>
        <div><label className="text-sm font-medium">Kalite eşikleri (JSON)</label><textarea value={thresholds} onChange={e=>setThresholds(e.target.value)} rows={8} spellCheck={false} className="mt-1 w-full font-mono text-xs border rounded-lg p-3 dark:bg-zinc-950 dark:border-zinc-700"/></div>
        <div className="flex gap-2"><button disabled={busy} onClick={()=>void save()} className="flex gap-2 px-3 py-2 rounded-lg bg-blue-600 text-white"><Save size={16}/>Kaydet</button><button onClick={()=>void run()} className="flex gap-2 px-3 py-2 rounded-lg bg-green-600 text-white"><Play size={16}/>Çalıştır</button>{id&&<button onClick={()=>void remove()} className="p-2 text-red-600"><Trash2 size={18}/></button>}</div>
      </section>
    </div>
    <section className="border rounded-xl dark:border-zinc-800 overflow-hidden"><div className="p-4 flex justify-between"><h2 className="font-semibold">Çalışma geçmişi</h2><button onClick={()=>void load()}><RefreshCw size={16}/></button></div><div className="overflow-x-auto"><table className="w-full text-sm"><thead className="bg-zinc-50 dark:bg-zinc-900"><tr>{["Dataset","Durum","İlerleme","Recall","Fact","Citation","Grounding","p95","Sonuç"].map(x=><th key={x} className="text-left p-3">{x}</th>)}</tr></thead><tbody>{runs.map(r=><tr key={r.id} className="border-t dark:border-zinc-800"><td className="p-3">{r.datasetName}</td><td className="p-3">{r.status}</td><td className="p-3">{r.completedCases}/{r.totalCases}</td><td className="p-3">{r.metrics?percent(r.metrics.recallAtK):"—"}</td><td className="p-3">{r.metrics?percent(r.metrics.factCoverage):"—"}</td><td className="p-3">{r.metrics?percent(r.metrics.citationCoverage):"—"}</td><td className="p-3">{r.metrics?percent(r.metrics.groundingCoverage):"—"}</td><td className="p-3">{r.metrics?`${r.metrics.p95LatencyMs} ms`:"—"}</td><td className={`p-3 font-medium ${r.metrics?.passed?"text-green-600":"text-red-600"}`}>{r.metrics?(r.metrics.passed?"PASS":"FAIL"):r.error??"—"}</td></tr>)}</tbody></table></div></section>
  </div>;
}
