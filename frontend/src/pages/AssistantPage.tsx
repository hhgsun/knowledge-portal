import { useRef, useState } from "react";
import { Link } from "react-router-dom";
import { Bot, Send, Sparkles, Search, BarChart3, MessageCircle, FileText, ShieldCheck, AlertTriangle, Loader2, ExternalLink, ThumbsUp, ThumbsDown, Square, RotateCcw } from "lucide-react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { toast } from "sonner";
import { useApi } from "../hooks/useApi";
import { useAuth } from "../contexts/AuthContext";
import { readApiError, readApiJson } from "../lib/api-response";
import { cn } from "../lib/utils";
import type { AssistantPreferredRoute, AssistantResponse } from "../types/api";
import { useCapabilities } from "../contexts/CapabilitiesContext";

const modes: { value: AssistantPreferredRoute; label: string; icon: typeof Sparkles }[] = [
  { value: "auto", label: "Otomatik yönlendir", icon: Sparkles },
  { value: "answer", label: "Kanıtlı yanıt", icon: Bot },
  { value: "search", label: "Doküman ara", icon: Search },
  { value: "analytics", label: "Portal analitiği", icon: BarChart3 },
  { value: "chat", label: "Genel sohbet", icon: MessageCircle },
];

const routeLabels: Record<AssistantResponse["route"], string> = {
  knowledge_search: "Doküman arama",
  knowledge_answer: "Kanıtlı yanıt",
  analytics: "Portal analitiği",
  general_chat: "Genel sohbet",
  clarification: "Açıklama gerekli",
};

export default function AssistantPage() {
  const { fetchWithAuth } = useApi();
  const { user } = useAuth();
  const { capabilities } = useCapabilities();
  const [message, setMessage] = useState("");
  const [mode, setMode] = useState<AssistantPreferredRoute>("auto");
  const [loading, setLoading] = useState(false);
  const [response, setResponse] = useState<AssistantResponse | null>(null);
  const abortRef = useRef<AbortController | null>(null);

  const visibleModes = modes.filter(item =>
    (capabilities?.supportedModes ?? modes.map(mode => mode.value)).includes(item.value)
    && (item.value !== "analytics" || user?.role === "admin" || user?.role === "editor"));

  const execute = async (text: string, preferredMode: AssistantPreferredRoute) => {
    if (!text || loading) return;
    const controller = new AbortController();
    abortRef.current = controller;
    setLoading(true);
    try {
      const result = await fetchWithAuth("/api/assistant", {
        method: "POST",
        noRetry: true,
        signal: controller.signal,
        body: JSON.stringify({ message: text, preferredRoute: preferredMode }),
      });
      if (!result.ok) throw new Error(await readApiError(result, "Asistan isteği tamamlanamadı."));
      setResponse(await readApiJson<AssistantResponse>(result));
    } catch (error) {
      if (error instanceof DOMException && error.name === "AbortError") {
        toast.info("Asistan isteği iptal edildi.");
        return;
      }
      toast.error(error instanceof Error ? error.message : "Asistan isteği tamamlanamadı.");
    } finally {
      if (abortRef.current === controller) abortRef.current = null;
      setLoading(false);
    }
  };

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    const text = message.trim();
    void execute(text, mode);
  };

  return (
    <div className="mx-auto max-w-5xl space-y-6">
      <header>
        <div className="flex items-center gap-3">
          <div className="rounded-xl bg-blue-100 p-2.5 text-blue-600 dark:bg-blue-950/50 dark:text-blue-300"><Bot size={24} /></div>
          <div>
            <h1 className="text-2xl font-bold text-zinc-900 dark:text-zinc-100">Bilgi Asistanı</h1>
            <p className="text-sm text-zinc-500">Sorunuzu güvenli biçimde doküman arama, kaynaklı RAG veya yetkili portal analitiği hattına yönlendirir.</p>
          </div>
        </div>
      </header>

      <form onSubmit={submit} className="rounded-2xl border border-zinc-200 bg-white p-4 shadow-sm dark:border-zinc-800 dark:bg-zinc-900">
        <textarea
          value={message}
          onChange={event => setMessage(event.target.value)}
          onKeyDown={event => {
            if (event.key === "Enter" && !event.shiftKey) {
              event.preventDefault();
              event.currentTarget.form?.requestSubmit();
            }
          }}
          maxLength={capabilities?.maxMessageCharacters ?? 4000}
          rows={4}
          placeholder="Örn. VPN politikası nedir ve ilgili rehberleri listele"
          className="w-full resize-none bg-transparent text-sm text-zinc-900 outline-none placeholder:text-zinc-400 dark:text-zinc-100"
          aria-label="Asistana sorunuz"
        />
        <div className="mt-3 flex flex-col gap-3 border-t border-zinc-100 pt-3 sm:flex-row sm:items-center sm:justify-between dark:border-zinc-800">
          <div className="flex flex-wrap gap-2">
            {visibleModes.map(item => {
              const Icon = item.icon;
              return <button key={item.value} type="button" onClick={() => setMode(item.value)}
                className={cn("inline-flex items-center gap-1.5 rounded-full px-3 py-1.5 text-xs font-medium transition-colors",
                  mode === item.value ? "bg-blue-100 text-blue-700 dark:bg-blue-950 dark:text-blue-300" : "bg-zinc-100 text-zinc-600 hover:bg-zinc-200 dark:bg-zinc-800 dark:text-zinc-400 dark:hover:bg-zinc-700")}
                aria-pressed={mode === item.value}><Icon size={13} />{item.label}</button>;
            })}
          </div>
          {loading ? <button type="button" onClick={() => abortRef.current?.abort()}
            className="inline-flex items-center justify-center gap-2 rounded-lg border border-red-200 px-4 py-2 text-sm font-medium text-red-600 hover:bg-red-50 dark:border-red-900 dark:text-red-400 dark:hover:bg-red-950/30">
            <Square size={14} />İptal et
          </button> : <button type="submit" disabled={!message.trim()}
            className="inline-flex items-center justify-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50">
            <Send size={16} />Gönder
          </button>}
        </div>
        {loading && <div className="mt-3 flex items-center gap-2 text-xs text-blue-600 dark:text-blue-400"><Loader2 size={13} className="animate-spin" />Routing ve kaynak doğrulama devam ediyor…</div>}
      </form>

      {response && <AssistantResult key={response.interactionId ?? response.traceId} response={response}
        feedbackEnabled={capabilities?.feedbackEnabled ?? true}
        onRetry={(preferredMode, query) => { setMessage(query); setMode(preferredMode); void execute(query, preferredMode); }} />}
    </div>
  );
}

function AssistantResult({ response, feedbackEnabled, onRetry }: {
  response: AssistantResponse;
  feedbackEnabled: boolean;
  onRetry: (mode: AssistantPreferredRoute, query: string) => void;
}) {
  const { fetchWithAuth } = useApi();
  const [feedback, setFeedback] = useState<"helpful" | "not_helpful" | null>(null);
  const [feedbackReason, setFeedbackReason] = useState("");
  const [feedbackSubmitting, setFeedbackSubmitting] = useState(false);
  const answerMarkdown = (response.answer ?? response.clarification ?? "")
    .replace(/\[(S\d+)\](?!\()/g, "[$1](#assistant-evidence-$1)");

  const sendFeedback = async (helpful: boolean, reason?: string,
      correctedRoute?: AssistantPreferredRoute) => {
    if (!response.interactionId || feedbackSubmitting) return;
    setFeedbackSubmitting(true);
    try {
      const result = await fetchWithAuth("/api/assistant/feedback", {
        method: "POST",
        noRetry: true,
        body: JSON.stringify({ interactionId: response.interactionId, helpful,
          reason: reason || null, correctedRoute: correctedRoute || null }),
      });
      if (!result.ok) throw new Error(await readApiError(result, "Geri bildirim kaydedilemedi."));
      setFeedback(helpful ? "helpful" : "not_helpful");
      toast.success("Geri bildiriminiz kaydedildi.");
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "Geri bildirim kaydedilemedi.");
    } finally {
      setFeedbackSubmitting(false);
    }
  };

  const recordClick = (articleId: string) => {
    if (!response.searchQueryId) return;
    void fetchWithAuth("/api/search/click", {
      method: "POST", noRetry: true,
      body: JSON.stringify({ searchQueryId: response.searchQueryId, articleId }),
    }).catch(() => undefined);
  };

  return <section className="space-y-4" aria-live="polite">
    <div className="flex flex-wrap items-center gap-2 text-xs">
      <span className="rounded-full bg-purple-100 px-2.5 py-1 font-medium text-purple-700 dark:bg-purple-950 dark:text-purple-300">{routeLabels[response.route]}</span>
      <span className="text-zinc-500" title="Routing karar skoru; kalibre edilmiş doğruluk olasılığı değildir.">Karar skoru %{Math.round(response.confidence * 100)}</span>
      <span className="text-zinc-400">· {response.responseTimeMs} ms</span>
      {response.toolCalls.map(tool => <span key={tool} className="rounded bg-zinc-100 px-2 py-1 text-zinc-500 dark:bg-zinc-800">{tool}</span>)}
    </div>

    {(response.answer || response.clarification) && <div className="rounded-2xl border border-zinc-200 bg-white p-5 dark:border-zinc-800 dark:bg-zinc-900">
      <div className="mb-3 flex items-center gap-2 text-sm font-medium text-zinc-700 dark:text-zinc-300"><Bot size={17} className="text-blue-500" />Asistan yanıtı</div>
      <div className="prose prose-sm max-w-none text-zinc-700 dark:prose-invert dark:text-zinc-300">
        <ReactMarkdown remarkPlugins={[remarkGfm]}>{answerMarkdown}</ReactMarkdown>
      </div>
      {response.rag && <div className="mt-4 flex items-center gap-2 border-t border-zinc-100 pt-3 text-xs text-zinc-500 dark:border-zinc-800">
        <ShieldCheck size={14} className="text-emerald-500" />
        {response.rag.groundingStatus.replaceAll("_", " ")} · atıf %{Math.round(response.rag.citationCoverage * 100)}
      </div>}
      {feedbackEnabled && response.interactionId && <div className="mt-4 flex flex-wrap items-center gap-2 border-t border-zinc-100 pt-3 text-xs dark:border-zinc-800">
        <span className="text-zinc-500">Bu sonuç yardımcı oldu mu?</span>
        <button type="button" disabled={feedbackSubmitting} onClick={() => void sendFeedback(true)}
          aria-pressed={feedback === "helpful"} className={cn("rounded-md p-1.5 hover:bg-emerald-100 dark:hover:bg-emerald-950", feedback === "helpful" && "bg-emerald-100 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300")} aria-label="Yardımcı oldu"><ThumbsUp size={14} /></button>
        <button type="button" disabled={feedbackSubmitting} onClick={() => void sendFeedback(false, feedbackReason || undefined)}
          aria-pressed={feedback === "not_helpful"} className={cn("rounded-md p-1.5 hover:bg-red-100 dark:hover:bg-red-950", feedback === "not_helpful" && "bg-red-100 text-red-700 dark:bg-red-950 dark:text-red-300")} aria-label="Yardımcı olmadı"><ThumbsDown size={14} /></button>
        <select value={feedbackReason} onChange={event => setFeedbackReason(event.target.value)}
          className="rounded-md border border-zinc-200 bg-transparent px-2 py-1 text-zinc-600 dark:border-zinc-700 dark:text-zinc-300" aria-label="Geri bildirim nedeni">
          <option value="">Neden? (isteğe bağlı)</option><option value="incorrect">Yanlış bilgi</option><option value="incomplete">Eksik</option><option value="wrong_source">Yanlış kaynak</option><option value="wrong_route">Yanlış yönlendirildi</option><option value="outdated">Güncel değil</option><option value="no_answer">Yanıt yok</option><option value="other">Diğer</option>
        </select>
      </div>}
    </div>}

    <div className="flex flex-wrap gap-2">
      {response.route !== "knowledge_answer" && <button type="button" onClick={() => { void sendFeedback(false, "wrong_route", "answer"); onRetry("answer", response.normalizedQuery); }} className="inline-flex items-center gap-1.5 rounded-lg border border-zinc-200 px-3 py-1.5 text-xs text-zinc-600 hover:bg-zinc-50 dark:border-zinc-800 dark:text-zinc-300 dark:hover:bg-zinc-900"><RotateCcw size={13} />Kanıtlı yanıt olarak tekrar dene</button>}
      {response.route !== "knowledge_search" && <button type="button" onClick={() => { void sendFeedback(false, "wrong_route", "search"); onRetry("search", response.normalizedQuery); }} className="inline-flex items-center gap-1.5 rounded-lg border border-zinc-200 px-3 py-1.5 text-xs text-zinc-600 hover:bg-zinc-50 dark:border-zinc-800 dark:text-zinc-300 dark:hover:bg-zinc-900"><RotateCcw size={13} />Doküman araması olarak tekrar dene</button>}
    </div>

    {response.warnings.length > 0 && <div className="rounded-xl border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800 dark:border-amber-900 dark:bg-amber-950/30 dark:text-amber-300">
      {response.warnings.map(warning => <p key={warning} className="flex items-start gap-2"><AlertTriangle size={15} className="mt-0.5 shrink-0" />{warning}</p>)}
    </div>}

    {response.rag && <RagSources rag={response.rag} onSourceClick={recordClick} />}

    {response.results.length > 0 && <div className="rounded-2xl border border-zinc-200 bg-white dark:border-zinc-800 dark:bg-zinc-900">
      <h2 className="border-b border-zinc-100 px-5 py-3 text-sm font-medium text-zinc-800 dark:border-zinc-800 dark:text-zinc-200">İlgili dokümanlar</h2>
      <div className="divide-y divide-zinc-100 dark:divide-zinc-800">{response.results.map(result =>
        <Link key={result.id} to={`/articles/${result.slug}`} onClick={() => recordClick(result.id)} className="flex gap-3 px-5 py-3 transition-colors hover:bg-zinc-50 dark:hover:bg-zinc-800/50">
          <FileText size={17} className="mt-0.5 shrink-0 text-blue-500" />
          <div className="min-w-0"><div className="truncate text-sm font-medium text-zinc-900 dark:text-zinc-100">{result.title}</div><p className="mt-1 line-clamp-2 text-xs text-zinc-500">{result.snippet || result.excerpt || "Açıklama bulunmuyor."}</p></div>
        </Link>)}</div>
    </div>}

    {response.rag && response.rag.evidence.length > 0 && <details open className="rounded-2xl border border-zinc-200 bg-white dark:border-zinc-800 dark:bg-zinc-900">
      <summary className="cursor-pointer px-5 py-3 text-sm font-medium text-zinc-800 dark:text-zinc-200">Kanıt pasajları ({response.rag.evidence.length})</summary>
      <div className="space-y-3 border-t border-zinc-100 p-5 dark:border-zinc-800">{response.rag.evidence.map((item, index) =>
        <div id={`assistant-evidence-${item.sourceId}`} key={`${item.sourceId}-${item.chunkId ?? index}`} className="scroll-mt-4 border-l-2 border-blue-400 pl-3 text-xs">
          <Link to={`/articles/${item.slug}`} target="_blank" rel="noopener noreferrer" onClick={() => recordClick(item.articleId)} className="font-medium text-blue-600 hover:underline dark:text-blue-400">{item.sourceId} · {item.sourceName || item.title}{item.pageNumber ? ` · sayfa ${item.pageNumber}` : ""}</Link>
          <p className="mt-1 whitespace-pre-wrap text-zinc-500">{item.passage}</p>
        </div>)}</div>
    </details>}

    {response.analytics && <AnalyticsResult analytics={response.analytics} />}
  </section>;
}

function RagSources({ rag, onSourceClick }: {
  rag: NonNullable<AssistantResponse["rag"]>;
  onSourceClick: (articleId: string) => void;
}) {
  const citedIds = new Set(rag.sources.map(source => source.articleId));
  const sources = [...rag.sources, ...rag.consultedSources.filter(source => !citedIds.has(source.articleId))];
  if (sources.length === 0) return null;
  return <div className="rounded-2xl border border-zinc-200 bg-white dark:border-zinc-800 dark:bg-zinc-900">
    <h2 className="border-b border-zinc-100 px-5 py-3 text-sm font-medium text-zinc-800 dark:border-zinc-800 dark:text-zinc-200">Kaynak kullanımı</h2>
    <div className="divide-y divide-zinc-100 dark:divide-zinc-800">{sources.map(source =>
      <Link key={source.articleId} to={`/articles/${source.slug}`} target="_blank" rel="noopener noreferrer"
        onClick={() => onSourceClick(source.articleId)}
        className="flex items-center gap-3 px-5 py-3 transition-colors hover:bg-zinc-50 dark:hover:bg-zinc-800/50">
        <FileText size={16} className="shrink-0 text-blue-500" />
        <span className="min-w-0 flex-1 truncate text-sm font-medium text-zinc-900 dark:text-zinc-100">{source.title}</span>
        <span className={cn("rounded-full px-2 py-0.5 text-[10px] font-medium", citedIds.has(source.articleId)
          ? "bg-emerald-100 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300"
          : "bg-zinc-100 text-zinc-500 dark:bg-zinc-800 dark:text-zinc-400")}>{citedIds.has(source.articleId) ? "Yanıtta kullanıldı" : "Yalnız incelendi"}</span>
        <ExternalLink size={14} className="shrink-0 text-zinc-400" />
      </Link>)}</div>
  </div>;
}

function AnalyticsResult({ analytics }: { analytics: NonNullable<AssistantResponse["analytics"]> }) {
  const cards = [
    ["Toplam makale", analytics.overview.totalArticles],
    ["Haftalık görüntülenme", analytics.overview.viewsThisWeek],
    ["Bugünkü arama", analytics.overview.searchesToday],
    ["Eski içerik", analytics.overview.staleArticles],
  ];
  return <div className="space-y-4">
    <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">{cards.map(([label, value]) =>
      <div key={label} className="rounded-xl border border-zinc-200 bg-white p-4 dark:border-zinc-800 dark:bg-zinc-900"><div className="text-xs text-zinc-500">{label}</div><div className="mt-1 text-2xl font-semibold text-zinc-900 dark:text-zinc-100">{value}</div></div>)}</div>
    <div className="grid gap-4 md:grid-cols-2">
      <div className="rounded-xl border border-zinc-200 bg-white p-4 dark:border-zinc-800 dark:bg-zinc-900"><h3 className="mb-3 text-sm font-medium">En sık aramalar</h3>{analytics.topSearches.slice(0, 5).map(item => <div key={item.query} className="flex justify-between py-1 text-xs text-zinc-500"><span>{item.query}</span><span>{item.count}</span></div>)}</div>
      <div className="rounded-xl border border-zinc-200 bg-white p-4 dark:border-zinc-800 dark:bg-zinc-900"><h3 className="mb-3 text-sm font-medium">En çok okunanlar</h3>{analytics.topArticles.slice(0, 5).map(item => <Link to={`/articles/${item.slug}`} key={item.articleId} className="flex justify-between py-1 text-xs text-zinc-500 hover:text-blue-600"><span className="truncate">{item.title}</span><span>{item.views}</span></Link>)}</div>
    </div>
  </div>;
}
