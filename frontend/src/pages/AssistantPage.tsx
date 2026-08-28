import { useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { AlertTriangle, Bot, ExternalLink, FileText, Loader2, Plus, Send, ShieldCheck, Square, ThumbsDown, ThumbsUp, Trash2 } from "lucide-react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { toast } from "sonner";
import { useApi } from "../hooks/useApi";
import { readApiError, readApiJson } from "../lib/api-response";
import { cn } from "../lib/utils";
import type { AssistantConversation, AssistantConversationMessage, AssistantResponse } from "../types/api";
import { useCapabilities } from "../contexts/CapabilitiesContext";

export default function AssistantPage() {
  const { fetchWithAuth } = useApi();
  const { capabilities } = useCapabilities();
  const [searchParams] = useSearchParams();
  const [message, setMessage] = useState(() => searchParams.get("q")?.trim() ?? "");
  const [loading, setLoading] = useState(false);
  const [response, setResponse] = useState<AssistantResponse | null>(null);
  const [conversationList, setConversationList] = useState<AssistantConversation[]>([]);
  const [conversationId, setConversationId] = useState<string | null>(null);
  const [history, setHistory] = useState<AssistantConversationMessage[]>([]);
  const [streamedText, setStreamedText] = useState("");
  const [streamStage, setStreamStage] = useState("");
  const abortRef = useRef<AbortController | null>(null);

  const loadConversations = async () => {
    if (capabilities && !capabilities.conversationHistoryEnabled) return;
    const result = await fetchWithAuth("/api/assistant/conversations", { noRetry: true });
    if (result.ok) setConversationList((await result.json()).conversations);
  };
  const loadMessages = async (id: string) => {
    const result = await fetchWithAuth(`/api/assistant/conversations/${id}/messages`, { noRetry: true });
    if (result.ok) setHistory((await result.json()).messages);
  };
  useEffect(() => { if (capabilities?.conversationHistoryEnabled) void loadConversations(); }, [capabilities?.conversationHistoryEnabled]); // eslint-disable-line react-hooks/exhaustive-deps

  const createConversation = async () => {
    const result = await fetchWithAuth("/api/assistant/conversations", { method: "POST", noRetry: true });
    if (!result.ok) return null;
    const item = await result.json() as AssistantConversation;
    setConversationId(item.id); setHistory([]); setResponse(null); setStreamedText("");
    await loadConversations();
    return item.id;
  };

  const execute = async (text: string) => {
    if (!text || loading) return;
    const controller = new AbortController();
    abortRef.current = controller;
    setLoading(true); setResponse(null); setStreamedText(""); setStreamStage("Yetkili kaynaklar getiriliyor…");
    try {
      let activeConversation = conversationId;
      if (capabilities?.conversationHistoryEnabled && !activeConversation)
        activeConversation = await createConversation();
      const streaming = capabilities?.streamingEnabled ?? true;
      const result = await fetchWithAuth(streaming ? "/api/assistant/stream" : "/api/assistant", {
        method: "POST", noRetry: true, signal: controller.signal,
        body: JSON.stringify({ message: text, conversationId: activeConversation }),
      });
      if (!result.ok) throw new Error(await readApiError(result, "Asistan isteği tamamlanamadı."));
      if (!streaming || !result.body) setResponse(await readApiJson<AssistantResponse>(result));
      else {
        const reader = result.body.getReader(); const decoder = new TextDecoder(); let buffer = "";
        while (true) {
          const { value, done } = await reader.read(); buffer += decoder.decode(value, { stream: !done });
          const blocks = buffer.split("\n\n"); buffer = blocks.pop() ?? "";
          for (const block of blocks) {
            const event = block.split("\n").find(line => line.startsWith("event: "))?.slice(7);
            const dataLine = block.split("\n").find(line => line.startsWith("data: "))?.slice(6);
            if (!event || !dataLine) continue;
            const data = JSON.parse(dataLine);
            if (event === "status") setStreamStage(data.message);
            if (event === "token") setStreamedText(current => current + data.text);
            if (event === "error") throw new Error(data.error);
            if (event === "complete") setResponse(data as AssistantResponse);
          }
          if (done) break;
        }
      }
      if (activeConversation) { await loadMessages(activeConversation); await loadConversations(); }
    } catch (error) {
      if (error instanceof DOMException && error.name === "AbortError") toast.info("Asistan isteği iptal edildi.");
      else toast.error(error instanceof Error ? error.message : "Asistan isteği tamamlanamadı.");
    } finally {
      if (abortRef.current === controller) abortRef.current = null;
      setLoading(false); setStreamStage("");
    }
  };

  return <div className="mx-auto max-w-5xl space-y-6">
    {capabilities?.conversationHistoryEnabled && <section className="rounded-xl border border-zinc-200 bg-white p-3 dark:border-zinc-800 dark:bg-zinc-900">
      <div className="mb-2 flex items-center justify-between"><h2 className="text-xs font-semibold uppercase tracking-wide text-zinc-500">Konuşmalar</h2><div className="flex gap-1"><button type="button" onClick={() => void createConversation()} className="rounded p-1.5 hover:bg-zinc-100 dark:hover:bg-zinc-800" aria-label="Yeni konuşma"><Plus size={15}/></button><button type="button" onClick={async () => { if (!confirm("Tüm konuşma geçmişi silinsin mi?")) return; await fetchWithAuth("/api/assistant/conversations", { method: "DELETE", noRetry: true }); setConversationList([]); setConversationId(null); setHistory([]); setResponse(null); }} className="rounded p-1.5 text-red-500 hover:bg-red-50 dark:hover:bg-red-950" aria-label="Tüm geçmişi temizle"><Trash2 size={15}/></button></div></div>
      <div className="flex gap-2 overflow-x-auto pb-1">{conversationList.map(item => <div key={item.id} className={cn("flex shrink-0 items-center rounded-lg border", conversationId === item.id ? "border-blue-400 bg-blue-50 dark:bg-blue-950" : "border-zinc-200 dark:border-zinc-700")}><button type="button" onClick={() => { setConversationId(item.id); setResponse(null); void loadMessages(item.id); }} className="px-3 py-2 text-left text-xs"><span className="block max-w-44 truncate font-medium">{item.title}</span><span className="text-zinc-400">{item.messageCount} mesaj</span></button><button type="button" onClick={async () => { await fetchWithAuth(`/api/assistant/conversations/${item.id}`, { method:"DELETE", noRetry:true }); if (conversationId === item.id) { setConversationId(null); setHistory([]); setResponse(null); } await loadConversations(); }} className="mr-1 rounded p-1 text-zinc-400 hover:bg-red-50 hover:text-red-500 dark:hover:bg-red-950" aria-label={`${item.title} konuşmasını sil`}><Trash2 size={13}/></button></div>)}</div>
    </section>}

    <header className="flex items-center gap-3">
      <div className="rounded-xl bg-blue-100 p-2.5 text-blue-600 dark:bg-blue-950/50 dark:text-blue-300"><Bot size={24}/></div>
      <div><h1 className="text-2xl font-bold text-zinc-900 dark:text-zinc-100">Bilgi Asistanı</h1><p className="text-sm text-zinc-500">Portal kaynaklarına dayalı, atıflı ve doğrulanmış RAG yanıtları üretir. Doküman listesi için Arama’yı kullanın.</p></div>
    </header>

    {history.length > 0 && <section className="max-h-80 space-y-2 overflow-y-auto rounded-xl bg-zinc-50 p-3 dark:bg-zinc-950">{history.map(item => <div key={item.id} className={cn("max-w-[85%] rounded-xl px-3 py-2 text-sm", item.role === "user" ? "ml-auto bg-blue-600 text-white" : "bg-white text-zinc-700 dark:bg-zinc-900 dark:text-zinc-300")}>{item.content}</div>)}</section>}

    <form onSubmit={event => { event.preventDefault(); void execute(message.trim()); }} className="rounded-2xl border border-zinc-200 bg-white p-4 shadow-sm dark:border-zinc-800 dark:bg-zinc-900">
      <textarea value={message} onChange={event => setMessage(event.target.value)} onKeyDown={event => { if (event.key === "Enter" && !event.shiftKey) { event.preventDefault(); event.currentTarget.form?.requestSubmit(); } }} maxLength={capabilities?.maxMessageCharacters ?? 4000} rows={4} placeholder="Örn. VPN politikası nedir ve hangi koşullar geçerlidir?" className="w-full resize-none bg-transparent text-sm text-zinc-900 outline-none placeholder:text-zinc-400 dark:text-zinc-100" aria-label="Bilgi Asistanına sorunuz" autoFocus />
      <div className="mt-3 flex items-center justify-between border-t border-zinc-100 pt-3 dark:border-zinc-800"><span className="inline-flex items-center gap-1.5 text-xs text-zinc-500"><ShieldCheck size={13} className="text-emerald-500"/>Yalnız yetkili portal kanıtları kullanılır</span>{loading ? <button type="button" onClick={() => abortRef.current?.abort()} className="inline-flex items-center gap-2 rounded-lg border border-red-200 px-4 py-2 text-sm font-medium text-red-600 hover:bg-red-50 dark:border-red-900"><Square size={14}/>İptal et</button> : <button type="submit" disabled={!message.trim()} className="inline-flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"><Send size={16}/>Sor</button>}</div>
      {loading && <div className="mt-3 flex items-center gap-2 text-xs text-blue-600 dark:text-blue-400"><Loader2 size={13} className="animate-spin"/>{streamStage || "Yanıt kanıtlarla doğrulanıyor…"}</div>}
    </form>

    {loading && streamedText && <div className="rounded-2xl border border-blue-200 bg-white p-5 text-sm text-zinc-700 dark:border-blue-900 dark:bg-zinc-900 dark:text-zinc-300"><div className="mb-2 flex items-center gap-2 text-xs font-medium text-blue-600"><Bot size={15}/>Doğrulanmış yanıt aktarılıyor</div><p className="whitespace-pre-wrap">{streamedText}</p></div>}
    {response && <AssistantResult key={response.interactionId ?? response.traceId} response={response} feedbackEnabled={capabilities?.feedbackEnabled ?? true}/>} 
  </div>;
}

function AssistantResult({ response, feedbackEnabled }: { response: AssistantResponse; feedbackEnabled: boolean }) {
  const { fetchWithAuth } = useApi();
  const [feedback, setFeedback] = useState<"helpful" | "not_helpful" | null>(null);
  const [feedbackReason, setFeedbackReason] = useState("");
  const [feedbackSubmitting, setFeedbackSubmitting] = useState(false);
  const answerMarkdown = (response.answer ?? "").replace(/\[(S\d+)\](?!\()/g, "[$1](#assistant-evidence-$1)");

  const sendFeedback = async (helpful: boolean) => {
    if (!response.interactionId || feedbackSubmitting) return;
    setFeedbackSubmitting(true);
    try {
      const result = await fetchWithAuth("/api/assistant/feedback", { method: "POST", noRetry: true, body: JSON.stringify({ interactionId: response.interactionId, helpful, reason: feedbackReason || null }) });
      if (!result.ok) throw new Error(await readApiError(result, "Geri bildirim kaydedilemedi."));
      setFeedback(helpful ? "helpful" : "not_helpful"); toast.success("Geri bildiriminiz kaydedildi.");
    } catch (error) { toast.error(error instanceof Error ? error.message : "Geri bildirim kaydedilemedi."); }
    finally { setFeedbackSubmitting(false); }
  };
  const recordClick = (articleId: string) => {
    if (!response.interactionId) return;
    void fetchWithAuth("/api/assistant/source-click", { method: "POST", noRetry: true, body: JSON.stringify({ interactionId: response.interactionId, articleId }) }).catch(() => undefined);
  };

  return <section className="space-y-4" aria-live="polite">
    <div className="flex flex-wrap items-center gap-2 text-xs"><span className="rounded-full bg-purple-100 px-2.5 py-1 font-medium text-purple-700 dark:bg-purple-950 dark:text-purple-300">Kanıtlı RAG yanıtı</span><span className="text-zinc-400">· {response.responseTimeMs} ms</span>{response.cacheHit && <span className="rounded bg-emerald-100 px-2 py-1 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300">güvenli cache</span>}</div>
    {response.answer && <div className="rounded-2xl border border-zinc-200 bg-white p-5 dark:border-zinc-800 dark:bg-zinc-900"><div className="mb-3 flex items-center gap-2 text-sm font-medium text-zinc-700 dark:text-zinc-300"><Bot size={17} className="text-blue-500"/>Asistan yanıtı</div><div className="prose prose-sm max-w-none text-zinc-700 dark:prose-invert dark:text-zinc-300"><ReactMarkdown remarkPlugins={[remarkGfm]}>{answerMarkdown}</ReactMarkdown></div>{response.rag && <div className="mt-4 flex items-center gap-2 border-t border-zinc-100 pt-3 text-xs text-zinc-500 dark:border-zinc-800"><ShieldCheck size={14} className="text-emerald-500"/>{response.rag.groundingStatus.replaceAll("_", " ")} · atıf %{Math.round(response.rag.citationCoverage * 100)}</div>}{feedbackEnabled && response.interactionId && <div className="mt-4 flex flex-wrap items-center gap-2 border-t border-zinc-100 pt-3 text-xs dark:border-zinc-800"><span className="text-zinc-500">Bu yanıt yardımcı oldu mu?</span><button type="button" disabled={feedbackSubmitting} onClick={() => void sendFeedback(true)} aria-pressed={feedback === "helpful"} className={cn("rounded-md p-1.5 hover:bg-emerald-100 dark:hover:bg-emerald-950", feedback === "helpful" && "bg-emerald-100 text-emerald-700")} aria-label="Yardımcı oldu"><ThumbsUp size={14}/></button><button type="button" disabled={feedbackSubmitting} onClick={() => void sendFeedback(false)} aria-pressed={feedback === "not_helpful"} className={cn("rounded-md p-1.5 hover:bg-red-100 dark:hover:bg-red-950", feedback === "not_helpful" && "bg-red-100 text-red-700")} aria-label="Yardımcı olmadı"><ThumbsDown size={14}/></button><select value={feedbackReason} onChange={event => setFeedbackReason(event.target.value)} className="rounded-md border border-zinc-200 bg-transparent px-2 py-1 text-zinc-600 dark:border-zinc-700 dark:text-zinc-300" aria-label="Geri bildirim nedeni"><option value="">Neden? (isteğe bağlı)</option><option value="incorrect">Yanlış bilgi</option><option value="incomplete">Eksik</option><option value="wrong_source">Yanlış kaynak</option><option value="outdated">Güncel değil</option><option value="no_answer">Yanıt yok</option><option value="other">Diğer</option></select></div>}</div>}
    {response.warnings.length > 0 && <div className="rounded-xl border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800 dark:border-amber-900 dark:bg-amber-950/30 dark:text-amber-300">{response.warnings.map(warning => <p key={warning} className="flex items-start gap-2"><AlertTriangle size={15} className="mt-0.5 shrink-0"/>{warning}</p>)}</div>}
    {response.rag && <RagSources rag={response.rag} onSourceClick={recordClick}/>} 
    {response.rag && response.rag.evidence.length > 0 && <details open className="rounded-2xl border border-zinc-200 bg-white dark:border-zinc-800 dark:bg-zinc-900"><summary className="cursor-pointer px-5 py-3 text-sm font-medium text-zinc-800 dark:text-zinc-200">Kanıt pasajları ({response.rag.evidence.length})</summary><div className="space-y-3 border-t border-zinc-100 p-5 dark:border-zinc-800">{response.rag.evidence.map((item, index) => <div id={`assistant-evidence-${item.sourceId}`} key={`${item.sourceId}-${item.chunkId ?? index}`} className="scroll-mt-4 border-l-2 border-blue-400 pl-3 text-xs"><Link to={`/articles/${item.slug}`} target="_blank" rel="noopener noreferrer" onClick={() => recordClick(item.articleId)} className="font-medium text-blue-600 hover:underline dark:text-blue-400">{item.sourceId} · {item.sourceName || item.title}{item.pageNumber ? ` · sayfa ${item.pageNumber}` : ""}</Link><p className="mt-1 whitespace-pre-wrap text-zinc-500">{item.passage}</p></div>)}</div></details>}
  </section>;
}

function RagSources({ rag, onSourceClick }: { rag: NonNullable<AssistantResponse["rag"]>; onSourceClick: (articleId: string) => void }) {
  const citedIds = new Set(rag.sources.map(source => source.articleId));
  const sources = [...rag.sources, ...rag.consultedSources.filter(source => !citedIds.has(source.articleId))];
  if (sources.length === 0) return null;
  return <div className="rounded-2xl border border-zinc-200 bg-white dark:border-zinc-800 dark:bg-zinc-900"><h2 className="border-b border-zinc-100 px-5 py-3 text-sm font-medium text-zinc-800 dark:border-zinc-800 dark:text-zinc-200">Kaynak kullanımı</h2><div className="divide-y divide-zinc-100 dark:divide-zinc-800">{sources.map(source => <Link key={source.articleId} to={`/articles/${source.slug}`} target="_blank" rel="noopener noreferrer" onClick={() => onSourceClick(source.articleId)} className="flex items-center gap-3 px-5 py-3 hover:bg-zinc-50 dark:hover:bg-zinc-800/50"><FileText size={16} className="shrink-0 text-blue-500"/><span className="min-w-0 flex-1 truncate text-sm font-medium text-zinc-900 dark:text-zinc-100">{source.title}</span><span className={cn("rounded-full px-2 py-0.5 text-[10px] font-medium", citedIds.has(source.articleId) ? "bg-emerald-100 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300" : "bg-zinc-100 text-zinc-500 dark:bg-zinc-800 dark:text-zinc-400")}>{citedIds.has(source.articleId) ? "Yanıtta kullanıldı" : "Yalnız incelendi"}</span><ExternalLink size={14} className="shrink-0 text-zinc-400"/></Link>)}</div></div>;
}
