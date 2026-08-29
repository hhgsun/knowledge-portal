import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { AlertTriangle, Bot, BookOpen, Check, ChevronRight, Clock3, Copy, Database, ExternalLink, FileText, History, Loader2, Menu, MessageSquare, PanelRight, Plus, Search, Send, ShieldCheck, Sparkles, Square, ThumbsDown, ThumbsUp, Trash2, X } from "lucide-react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { toast } from "sonner";
import { useCapabilities } from "../contexts/CapabilitiesContext";
import { useApi } from "../hooks/useApi";
import { readApiError, readApiJson } from "../lib/api-response";
import { cn } from "../lib/utils";
import type { AssistantConversation, AssistantConversationMessage, AssistantResponse, RagSource } from "../types/api";

const starterQuestions = [
  { icon: ShieldCheck, label: "Politika ve kontroller", question: "Bilgi güvenliği politikamızdaki temel sorumluluklar ve istisnalar nelerdir?" },
  { icon: BookOpen, label: "Süreç özeti", question: "Yeni bir çalışan için ilk hafta tamamlanması gereken adımları özetle." },
  { icon: Database, label: "Karşılaştırmalı yanıt", question: "İlgili dokümanlardaki kuralları karşılaştır ve varsa çelişkileri belirt." },
];

export default function AssistantPage() {
  const { fetchWithAuth } = useApi();
  const { capabilities } = useCapabilities();
  const [searchParams] = useSearchParams();
  const [message, setMessage] = useState(() => searchParams.get("q")?.trim() ?? "");
  const [loading, setLoading] = useState(false);
  const [response, setResponse] = useState<AssistantResponse | null>(null);
  const [conversations, setConversations] = useState<AssistantConversation[]>([]);
  const [conversationId, setConversationId] = useState<string | null>(null);
  const [history, setHistory] = useState<AssistantConversationMessage[]>([]);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [conversationQuery, setConversationQuery] = useState("");
  const [mobileHistory, setMobileHistory] = useState(false);
  const [streamedText, setStreamedText] = useState("");
  const [streamStage, setStreamStage] = useState("");
  const [pendingQuestion, setPendingQuestion] = useState<string | null>(null);
  const abortRef = useRef<AbortController | null>(null);
  const textareaRef = useRef<HTMLTextAreaElement | null>(null);
  const endRef = useRef<HTMLDivElement | null>(null);

  const loadConversations = async () => {
    if (capabilities && !capabilities.conversationHistoryEnabled) return;
    const result = await fetchWithAuth("/api/assistant/conversations", { noRetry: true });
    if (result.ok) setConversations((await result.json()).conversations);
  };

  const loadMessages = async (id: string) => {
    setHistoryLoading(true);
    try {
      const result = await fetchWithAuth(`/api/assistant/conversations/${id}/messages`, { noRetry: true });
      if (!result.ok) throw new Error(await readApiError(result, "Konuşma yüklenemedi."));
      setHistory((await result.json()).messages);
    } catch (error) { toast.error(error instanceof Error ? error.message : "Konuşma yüklenemedi."); }
    finally { setHistoryLoading(false); }
  };

  useEffect(() => { if (capabilities?.conversationHistoryEnabled) void loadConversations(); }, [capabilities?.conversationHistoryEnabled]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { endRef.current?.scrollIntoView({ behavior: loading ? "smooth" : "auto", block: "end" }); }, [history, loading, streamedText, response]);

  const createConversation = async () => {
    try {
      const result = await fetchWithAuth("/api/assistant/conversations", { method: "POST", noRetry: true });
      if (!result.ok) throw new Error(await readApiError(result, "Yeni konuşma oluşturulamadı."));
      const item = await result.json() as AssistantConversation;
      setConversationId(item.id); setHistory([]); setResponse(null); setStreamedText(""); setMobileHistory(false);
      await loadConversations();
      requestAnimationFrame(() => textareaRef.current?.focus());
      return item.id;
    } catch (error) { toast.error(error instanceof Error ? error.message : "Yeni konuşma oluşturulamadı."); return null; }
  };

  const selectConversation = (id: string) => {
    if (loading) abortRef.current?.abort();
    setConversationId(id); setResponse(null); setStreamedText(""); setPendingQuestion(null); setMobileHistory(false);
    void loadMessages(id);
  };

  const deleteConversation = async (item: AssistantConversation) => {
    if (!confirm(`“${item.title}” konuşması silinsin mi?`)) return;
    const result = await fetchWithAuth(`/api/assistant/conversations/${item.id}`, { method: "DELETE", noRetry: true });
    if (!result.ok) { toast.error(await readApiError(result, "Konuşma silinemedi.")); return; }
    if (conversationId === item.id) { setConversationId(null); setHistory([]); setResponse(null); }
    await loadConversations(); toast.success("Konuşma silindi.");
  };

  const clearConversations = async () => {
    if (!confirm("Tüm konuşma geçmişi kalıcı olarak silinsin mi?")) return;
    const result = await fetchWithAuth("/api/assistant/conversations", { method: "DELETE", noRetry: true });
    if (!result.ok) { toast.error(await readApiError(result, "Geçmiş temizlenemedi.")); return; }
    setConversations([]); setConversationId(null); setHistory([]); setResponse(null); toast.success("Konuşma geçmişi temizlendi.");
  };

  const execute = async (rawText: string) => {
    const text = rawText.trim();
    if (!text || loading) return;
    const controller = new AbortController(); abortRef.current = controller;
    setLoading(true); setPendingQuestion(text); setMessage(""); setResponse(null); setStreamedText(""); setStreamStage("Yetkili bilgi kapsamı denetleniyor");
    try {
      let activeConversation = conversationId;
      if (capabilities?.conversationHistoryEnabled && !activeConversation) {
        activeConversation = await createConversation();
        if (!activeConversation) throw new Error("Konuşma başlatılamadı.");
      }
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
          const blocks = buffer.split(/\r?\n\r?\n/); buffer = blocks.pop() ?? "";
          for (const block of blocks) {
            const lines = block.split(/\r?\n/);
            const event = lines.find(line => line.startsWith("event: "))?.slice(7);
            const dataLine = lines.find(line => line.startsWith("data: "))?.slice(6);
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
      else { setMessage(current => current || text); toast.error(error instanceof Error ? error.message : "Asistan isteği tamamlanamadı."); }
    } finally {
      if (abortRef.current === controller) abortRef.current = null;
      setLoading(false); setPendingQuestion(null); setStreamStage("");
    }
  };

  const visibleHistory = useMemo(() => {
    if (!response?.answer || history.length === 0) return history;
    const last = history.at(-1);
    return last?.role === "assistant" && last.content === response.answer ? history.slice(0, -1) : history;
  }, [history, response]);
  const filtered = useMemo(() => {
    const query = conversationQuery.trim().toLocaleLowerCase("tr-TR");
    return query ? conversations.filter(item => item.title.toLocaleLowerCase("tr-TR").includes(query)) : conversations;
  }, [conversations, conversationQuery]);
  const hasContent = visibleHistory.length > 0 || loading || !!response;
  const historyEnabled = capabilities?.conversationHistoryEnabled ?? false;

  return <div className="mx-auto flex h-[calc(100dvh-7rem)] min-h-[38rem] max-w-[1600px] flex-col overflow-hidden rounded-2xl border border-zinc-200 bg-zinc-50 shadow-sm dark:border-zinc-800 dark:bg-zinc-950">
    <AssistantHeader historyEnabled={historyEnabled} onOpen={() => setMobileHistory(true)} onNew={() => void createConversation()} />
    <div className="grid min-h-0 flex-1 lg:grid-cols-[17rem_minmax(0,1fr)] 2xl:grid-cols-[17rem_minmax(0,1fr)_22rem]">
      {historyEnabled && <ConversationSidebar className="hidden lg:flex" conversations={filtered} activeId={conversationId} query={conversationQuery} onQuery={setConversationQuery} onSelect={selectConversation} onDelete={deleteConversation} onClear={clearConversations} onNew={() => void createConversation()} />}
      <main className="flex min-h-0 min-w-0 flex-col bg-white dark:bg-zinc-900">
        <div className="min-h-0 flex-1 overflow-y-auto" aria-busy={loading}>
          <div className="mx-auto flex min-h-full w-full max-w-4xl flex-col px-4 py-6 sm:px-8 lg:px-10">
            {historyLoading ? <LoadingConversation /> : !hasContent ? <WelcomeState onQuestion={question => void execute(question)} /> :
              <div className="space-y-7" aria-live="polite">
                {visibleHistory.map(item => <HistoryMessage key={item.id} item={item} />)}
                {pendingQuestion && <UserMessage content={pendingQuestion} />}
                {loading && <StreamingAnswer text={streamedText} stage={streamStage} />}
                {response && <AssistantResult key={response.interactionId ?? response.traceId} response={response} feedbackEnabled={capabilities?.feedbackEnabled ?? true} />}
                <div ref={endRef} />
              </div>}
          </div>
        </div>
        <Composer inputRef={textareaRef} message={message} loading={loading} maxLength={capabilities?.maxMessageCharacters ?? 4000} onChange={setMessage} onSubmit={() => void execute(message)} onCancel={() => abortRef.current?.abort()} />
      </main>
      <aside className="hidden min-h-0 border-l border-zinc-200 bg-zinc-50 2xl:block dark:border-zinc-800 dark:bg-zinc-950"><div className="sticky top-0 max-h-[calc(100vh-8rem)] overflow-y-auto p-4">{response?.rag ? <EvidencePanel response={response} /> : <EvidenceEmptyState />}</div></aside>
    </div>
    {mobileHistory && historyEnabled && <div className="fixed inset-0 z-50 lg:hidden" role="dialog" aria-modal="true" aria-label="Konuşma geçmişi">
      <button type="button" className="absolute inset-0 bg-zinc-950/50 backdrop-blur-sm" onClick={() => setMobileHistory(false)} aria-label="Konuşmaları kapat" />
      <ConversationSidebar className="absolute inset-y-0 left-0 flex w-[min(22rem,88vw)] shadow-2xl" conversations={filtered} activeId={conversationId} query={conversationQuery} onQuery={setConversationQuery} onSelect={selectConversation} onDelete={deleteConversation} onClear={clearConversations} onNew={() => void createConversation()} onClose={() => setMobileHistory(false)} />
    </div>}
  </div>;
}

function AssistantHeader({ historyEnabled, onOpen, onNew }: { historyEnabled: boolean; onOpen: () => void; onNew: () => void }) {
  return <header className="flex min-h-16 items-center justify-between border-b border-zinc-200 bg-white px-4 dark:border-zinc-800 dark:bg-zinc-900 sm:px-5">
    <div className="flex min-w-0 items-center gap-3">
      {historyEnabled && <button type="button" onClick={onOpen} className="rounded-lg p-2 text-zinc-500 hover:bg-zinc-100 lg:hidden dark:hover:bg-zinc-800" aria-label="Konuşmaları aç"><Menu size={19} /></button>}
      <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-blue-600 text-white shadow-sm shadow-blue-600/20"><Sparkles size={18} /></div>
      <div className="min-w-0"><div className="flex items-center gap-2"><h1 className="truncate text-sm font-semibold text-zinc-950 dark:text-zinc-50 sm:text-base">Bilgi Asistanı</h1><span className="hidden items-center gap-1 rounded-full border border-emerald-200 bg-emerald-50 px-2 py-0.5 text-[10px] font-semibold text-emerald-700 sm:inline-flex dark:border-emerald-900 dark:bg-emerald-950/50 dark:text-emerald-300"><span className="h-1.5 w-1.5 rounded-full bg-emerald-500" />Kurumsal bilgiye bağlı</span></div><p className="truncate text-xs text-zinc-500">Yetkiniz kapsamındaki kaynaklardan izlenebilir yanıtlar</p></div>
    </div>
    {historyEnabled && <button type="button" onClick={onNew} className="inline-flex shrink-0 items-center gap-2 rounded-lg border border-zinc-200 px-3 py-2 text-xs font-medium text-zinc-700 hover:bg-zinc-50 dark:border-zinc-700 dark:text-zinc-200 dark:hover:bg-zinc-800"><Plus size={15} /><span className="hidden sm:inline">Yeni konuşma</span></button>}
  </header>;
}

type ConversationSidebarProps = { className?: string; conversations: AssistantConversation[]; activeId: string | null; query: string; onQuery: (value: string) => void; onSelect: (id: string) => void; onDelete: (item: AssistantConversation) => Promise<void>; onClear: () => Promise<void>; onNew: () => void; onClose?: () => void };
function ConversationSidebar({ className, conversations, activeId, query, onQuery, onSelect, onDelete, onClear, onNew, onClose }: ConversationSidebarProps) {
  return <aside className={cn("min-h-0 flex-col border-r border-zinc-200 bg-zinc-50 dark:border-zinc-800 dark:bg-zinc-950", className)}>
    <div className="border-b border-zinc-200 p-3 dark:border-zinc-800">
      <div className="mb-3 flex items-center justify-between"><h2 className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wider text-zinc-500"><History size={14} />Konuşmalar</h2>{onClose && <button type="button" onClick={onClose} className="rounded-md p-1.5 text-zinc-500 hover:bg-zinc-200 dark:hover:bg-zinc-800" aria-label="Kapat"><X size={16} /></button>}</div>
      <button type="button" onClick={onNew} className="mb-2 flex w-full items-center justify-center gap-2 rounded-lg bg-zinc-900 px-3 py-2.5 text-sm font-medium text-white hover:bg-zinc-700 dark:bg-zinc-100 dark:text-zinc-900"><Plus size={16} />Yeni konuşma</button>
      <label className="relative block"><Search size={14} className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-zinc-400" /><input value={query} onChange={event => onQuery(event.target.value)} placeholder="Konuşmalarda ara" className="w-full rounded-lg border border-zinc-200 bg-white py-2 pl-9 pr-3 text-xs outline-none focus:border-blue-500 focus:ring-2 focus:ring-blue-500/10 dark:border-zinc-700 dark:bg-zinc-900" /></label>
    </div>
    <div className="min-h-0 flex-1 overflow-y-auto p-2">{conversations.length ? <div className="space-y-1">{conversations.map(item => <div key={item.id} className={cn("group relative rounded-lg", activeId === item.id && "bg-white shadow-sm ring-1 ring-zinc-200 dark:bg-zinc-900 dark:ring-zinc-700")}>
      <button type="button" onClick={() => onSelect(item.id)} className="w-full rounded-lg px-3 py-2.5 pr-9 text-left text-zinc-600 hover:bg-white dark:text-zinc-400 dark:hover:bg-zinc-900"><span className="block truncate text-xs font-medium text-zinc-900 dark:text-zinc-100">{item.title}</span><span className="mt-1 flex items-center gap-1.5 text-[10px] text-zinc-400"><Clock3 size={10} />{formatConversationDate(item.updatedAt)} · {item.messageCount} mesaj</span></button>
      <button type="button" onClick={() => void onDelete(item)} className="absolute right-2 top-1/2 -translate-y-1/2 rounded-md p-1.5 text-zinc-400 opacity-0 hover:bg-red-50 hover:text-red-600 group-hover:opacity-100 focus:opacity-100 dark:hover:bg-red-950/50" aria-label={`${item.title} konuşmasını sil`}><Trash2 size={13} /></button>
    </div>)}</div> : <div className="px-4 py-10 text-center"><MessageSquare size={22} className="mx-auto mb-2 text-zinc-300 dark:text-zinc-700" /><p className="text-xs font-medium text-zinc-600 dark:text-zinc-400">{query ? "Eşleşen konuşma yok" : "Henüz konuşma yok"}</p><p className="mt-1 text-[11px] text-zinc-400">{query ? "Farklı bir arama deneyin." : "Sorularınız burada güvenle saklanır."}</p></div>}</div>
    {conversations.length > 0 && <div className="border-t border-zinc-200 p-3 dark:border-zinc-800"><button type="button" onClick={() => void onClear()} className="flex w-full items-center justify-center gap-2 rounded-lg px-3 py-2 text-xs text-zinc-500 hover:bg-red-50 hover:text-red-600 dark:hover:bg-red-950/30"><Trash2 size={13} />Geçmişi temizle</button></div>}
  </aside>;
}

function WelcomeState({ onQuestion }: { onQuestion: (question: string) => void }) {
  return <section className="my-auto py-8 sm:py-14"><div className="mx-auto max-w-2xl text-center"><div className="mx-auto mb-5 flex h-14 w-14 items-center justify-center rounded-2xl border border-blue-100 bg-blue-50 text-blue-600 dark:border-blue-900 dark:bg-blue-950/40 dark:text-blue-300"><Bot size={26} /></div><p className="mb-2 text-xs font-semibold uppercase tracking-[0.18em] text-blue-600 dark:text-blue-400">Kurumsal bilgi, tek bir yerde</p><h2 className="text-2xl font-semibold tracking-tight text-zinc-950 dark:text-zinc-50 sm:text-3xl">Nasıl yardımcı olabilirim?</h2><p className="mx-auto mt-3 max-w-xl text-sm leading-6 text-zinc-500">Politikaları özetleyin, süreçleri karşılaştırın veya bir kararın dayanağını sorun. Her yanıt erişebildiğiniz portal kaynaklarına bağlanır.</p></div>
    <div className="mx-auto mt-8 grid max-w-3xl gap-3 md:grid-cols-3">{starterQuestions.map(item => <button key={item.label} type="button" onClick={() => onQuestion(item.question)} className="group rounded-xl border border-zinc-200 bg-white p-4 text-left transition-all hover:-translate-y-0.5 hover:border-blue-300 hover:shadow-md dark:border-zinc-800 dark:bg-zinc-950 dark:hover:border-blue-800"><span className="mb-6 flex h-8 w-8 items-center justify-center rounded-lg bg-zinc-100 text-zinc-600 group-hover:bg-blue-50 group-hover:text-blue-600 dark:bg-zinc-800 dark:text-zinc-300"><item.icon size={16} /></span><span className="block text-xs font-semibold text-zinc-900 dark:text-zinc-100">{item.label}</span><span className="mt-1.5 block text-xs leading-5 text-zinc-500">{item.question}</span><span className="mt-3 flex items-center gap-1 text-[11px] font-medium text-blue-600 opacity-0 group-hover:opacity-100">Soruyu kullan <ChevronRight size={12} /></span></button>)}</div>
    <div className="mx-auto mt-7 flex max-w-xl items-start gap-2 rounded-lg bg-zinc-100/70 px-3 py-2 text-[11px] leading-4 text-zinc-500 dark:bg-zinc-800/50"><ShieldCheck size={14} className="mt-0.5 shrink-0 text-emerald-600" />Asistan yalnızca görme yetkiniz olan içerikleri kullanır. Kritik kararları kaynak bağlantılarından doğrulayın.</div>
  </section>;
}

function LoadingConversation() { return <div className="flex flex-1 items-center justify-center text-sm text-zinc-500"><Loader2 size={18} className="mr-2 animate-spin" />Konuşma yükleniyor</div>; }
function HistoryMessage({ item }: { item: AssistantConversationMessage }) {
  return item.role === "user" ? <UserMessage content={item.content} /> : <div className="flex items-start gap-3"><AssistantAvatar /><div className="min-w-0 flex-1 pt-1"><div className="mb-2 flex items-center gap-2"><span className="text-xs font-semibold text-zinc-800 dark:text-zinc-200">Bilgi Asistanı</span><span className="text-[10px] text-zinc-400">{formatMessageTime(item.createdAt)}</span></div><div className="prose prose-sm max-w-none text-zinc-700 dark:prose-invert dark:text-zinc-300"><ReactMarkdown remarkPlugins={[remarkGfm]}>{item.content}</ReactMarkdown></div></div></div>;
}
function UserMessage({ content }: { content: string }) { return <div className="flex justify-end"><div className="max-w-[88%] rounded-2xl rounded-br-md bg-zinc-900 px-4 py-3 text-sm leading-6 text-white shadow-sm dark:bg-zinc-100 dark:text-zinc-900 sm:max-w-[76%]">{content}</div></div>; }
function AssistantAvatar() { return <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-blue-600 text-white"><Sparkles size={15} /></span>; }
function StreamingAnswer({ text, stage }: { text: string; stage: string }) {
  return <div className="flex items-start gap-3"><AssistantAvatar /><div className="min-w-0 flex-1 pt-1"><div className="mb-3 flex items-center gap-2 text-xs font-semibold text-zinc-800 dark:text-zinc-200">Bilgi Asistanı <span className="font-normal text-blue-600">çalışıyor</span></div>{text ? <div className="prose prose-sm max-w-none text-zinc-700 dark:prose-invert dark:text-zinc-300"><ReactMarkdown remarkPlugins={[remarkGfm]}>{text}</ReactMarkdown><span className="ml-1 inline-block h-4 w-1 animate-pulse rounded-full bg-blue-500 align-middle" /></div> : <div className="rounded-xl border border-blue-100 bg-blue-50/60 px-4 py-3 dark:border-blue-900 dark:bg-blue-950/20"><div className="flex items-center gap-2 text-xs font-medium text-blue-700 dark:text-blue-300"><Loader2 size={14} className="animate-spin" />{stage || "Kaynaklar değerlendiriliyor"}</div><div className="mt-2 h-1 overflow-hidden rounded-full bg-blue-100 dark:bg-blue-950"><div className="h-full w-1/3 animate-pulse rounded-full bg-blue-500" /></div></div>}</div></div>;
}

type ComposerProps = { inputRef: React.RefObject<HTMLTextAreaElement | null>; message: string; loading: boolean; maxLength: number; onChange: (value: string) => void; onSubmit: () => void; onCancel: () => void };
function Composer({ inputRef, message, loading, maxLength, onChange, onSubmit, onCancel }: ComposerProps) {
  return <div className="border-t border-zinc-200 bg-white px-4 py-4 dark:border-zinc-800 dark:bg-zinc-900 sm:px-8 lg:px-10"><form onSubmit={event => { event.preventDefault(); onSubmit(); }} className="mx-auto max-w-4xl"><div className="rounded-xl border border-zinc-300 bg-white p-2 shadow-sm transition focus-within:border-blue-500 focus-within:ring-4 focus-within:ring-blue-500/10 dark:border-zinc-700 dark:bg-zinc-950"><textarea ref={inputRef} value={message} onChange={event => onChange(event.target.value)} onKeyDown={event => { if (event.key === "Enter" && !event.shiftKey && !event.nativeEvent.isComposing) { event.preventDefault(); event.currentTarget.form?.requestSubmit(); } }} maxLength={maxLength} rows={2} placeholder="Kurumsal bilginiz hakkında bir soru sorun…" className="max-h-40 min-h-12 w-full resize-none bg-transparent px-2 py-1.5 text-sm leading-6 outline-none placeholder:text-zinc-400" aria-label="Bilgi Asistanına sorun" /><div className="flex items-center justify-between gap-3 px-1"><div className="flex min-w-0 items-center gap-2 text-[10px] text-zinc-400 sm:text-[11px]"><ShieldCheck size={13} className="shrink-0 text-emerald-500" /><span className="truncate">Yetki kapsamınız korunur</span>{message.length > maxLength * .8 && <span className="shrink-0 tabular-nums">{message.length}/{maxLength}</span>}</div>{loading ? <button type="button" onClick={onCancel} className="inline-flex h-9 items-center gap-2 rounded-lg border border-red-200 px-3 text-xs font-medium text-red-600 hover:bg-red-50 dark:border-red-900"><Square size={12} />Durdur</button> : <button type="submit" disabled={!message.trim()} className="inline-flex h-9 items-center gap-2 rounded-lg bg-blue-600 px-3.5 text-xs font-semibold text-white shadow-sm hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-40"><Send size={14} />Gönder</button>}</div></div><p className="mt-2 text-center text-[10px] text-zinc-400">Enter ile gönderin · Shift + Enter ile yeni satır ekleyin · Önemli bilgileri bağlı kaynaklardan doğrulayın</p></form></div>;
}

function AssistantResult({ response, feedbackEnabled }: { response: AssistantResponse; feedbackEnabled: boolean }) {
  const { fetchWithAuth } = useApi();
  const [feedback, setFeedback] = useState<"helpful" | "not_helpful" | null>(null);
  const [feedbackReason, setFeedbackReason] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [copied, setCopied] = useState(false);
  const answerMarkdown = (response.answer ?? "").replace(/\[(S\d+)\](?!\()/g, "[$1](#assistant-evidence-$1)");
  const sendFeedback = async (helpful: boolean) => {
    if (!response.interactionId || submitting) return;
    setSubmitting(true);
    try { const result = await fetchWithAuth("/api/assistant/feedback", { method: "POST", noRetry: true, body: JSON.stringify({ interactionId: response.interactionId, helpful, reason: feedbackReason || null }) }); if (!result.ok) throw new Error(await readApiError(result, "Geri bildirim kaydedilemedi.")); setFeedback(helpful ? "helpful" : "not_helpful"); toast.success("Geri bildiriminiz kaydedildi."); }
    catch (error) { toast.error(error instanceof Error ? error.message : "Geri bildirim kaydedilemedi."); } finally { setSubmitting(false); }
  };
  const copyAnswer = async () => { if (!response.answer) return; try { await navigator.clipboard.writeText(response.answer); setCopied(true); setTimeout(() => setCopied(false), 1800); } catch { toast.error("Yanıt kopyalanamadı."); } };
  return <div className="flex items-start gap-3"><AssistantAvatar /><section className="min-w-0 flex-1 pt-1" aria-label="Asistan yanıtı">
    <div className="mb-3 flex flex-wrap items-center justify-between gap-2"><div className="flex flex-wrap items-center gap-2"><span className="text-xs font-semibold text-zinc-800 dark:text-zinc-200">Bilgi Asistanı</span>{response.rag && <GroundingBadge status={response.rag.groundingStatus} insufficient={response.rag.insufficientContext} />}<span className="text-[10px] text-zinc-400">{formatResponseTime(response.responseTimeMs)}</span>{response.cacheHit && <span className="rounded bg-zinc-100 px-1.5 py-0.5 text-[9px] uppercase text-zinc-500 dark:bg-zinc-800">önbellek</span>}</div>{response.answer && <button type="button" onClick={() => void copyAnswer()} className="inline-flex items-center gap-1.5 rounded-md px-2 py-1 text-[11px] text-zinc-500 hover:bg-zinc-100 dark:hover:bg-zinc-800">{copied ? <Check size={13} className="text-emerald-500" /> : <Copy size={13} />}{copied ? "Kopyalandı" : "Kopyala"}</button>}</div>
    {response.answer ? <div className="prose prose-sm max-w-none text-zinc-700 prose-a:font-semibold prose-a:text-blue-600 prose-a:no-underline hover:prose-a:underline dark:prose-invert dark:text-zinc-300 dark:prose-a:text-blue-400"><ReactMarkdown remarkPlugins={[remarkGfm]}>{answerMarkdown}</ReactMarkdown></div> : <div className="rounded-xl border border-zinc-200 bg-zinc-50 p-4 text-sm text-zinc-600 dark:border-zinc-700 dark:bg-zinc-800/40">Bu soru için yeterli ve güvenilir bir yanıt üretilemedi.</div>}
    {response.warnings.length > 0 && <div className="mt-4 space-y-2 rounded-xl border border-amber-200 bg-amber-50 p-3 text-xs leading-5 text-amber-800 dark:border-amber-900 dark:bg-amber-950/30 dark:text-amber-300">{response.warnings.map(warning => <p key={warning} className="flex items-start gap-2"><AlertTriangle size={14} className="mt-0.5 shrink-0" />{warning}</p>)}</div>}
    {response.rag && <div className="mt-5 2xl:hidden"><EvidencePanel response={response} /></div>}
    {feedbackEnabled && response.interactionId && <div className="mt-5 flex flex-wrap items-center gap-2 border-t border-zinc-100 pt-4 text-xs dark:border-zinc-800"><span className="mr-1 text-zinc-500">Bu yanıt yararlı mıydı?</span><FeedbackButton active={feedback === "helpful"} positive disabled={submitting} onClick={() => void sendFeedback(true)} /><FeedbackButton active={feedback === "not_helpful"} disabled={submitting} onClick={() => void sendFeedback(false)} /><select value={feedbackReason} onChange={event => setFeedbackReason(event.target.value)} className="h-8 rounded-lg border border-zinc-200 bg-white px-2 text-[11px] text-zinc-600 outline-none focus:border-blue-500 dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-300" aria-label="Geri bildirim nedeni"><option value="">Neden? (isteğe bağlı)</option><option value="incorrect">Yanlış bilgi</option><option value="incomplete">Eksik yanıt</option><option value="wrong_source">Yanlış kaynak</option><option value="outdated">Güncel değil</option><option value="no_answer">Yanıt yok</option><option value="other">Diğer</option></select></div>}
  </section></div>;
}

function FeedbackButton({ active, positive = false, disabled, onClick }: { active: boolean; positive?: boolean; disabled: boolean; onClick: () => void }) {
  const color = positive ? "hover:border-emerald-300 hover:bg-emerald-50 hover:text-emerald-700" : "hover:border-red-300 hover:bg-red-50 hover:text-red-700";
  return <button type="button" disabled={disabled} onClick={onClick} aria-pressed={active} className={cn("rounded-lg border border-zinc-200 p-2 text-zinc-500 dark:border-zinc-700", color, active && (positive ? "border-emerald-300 bg-emerald-50 text-emerald-700" : "border-red-300 bg-red-50 text-red-700"))} aria-label={positive ? "Yararlı" : "Yararlı değil"}>{positive ? <ThumbsUp size={14} /> : <ThumbsDown size={14} />}</button>;
}

function EvidencePanel({ response }: { response: AssistantResponse }) {
  const rag = response.rag; const { fetchWithAuth } = useApi(); if (!rag) return null;
  const citedIds = new Set(rag.sources.map(source => source.articleId));
  const sources = [...rag.sources, ...rag.consultedSources.filter(source => !citedIds.has(source.articleId))];
  const recordClick = (articleId: string) => { if (response.interactionId) void fetchWithAuth("/api/assistant/source-click", { method: "POST", noRetry: true, body: JSON.stringify({ interactionId: response.interactionId, articleId }) }).catch(() => undefined); };
  return <div className="space-y-4">
    <section className="rounded-xl border border-zinc-200 bg-white dark:border-zinc-800 dark:bg-zinc-900"><div className="flex items-center justify-between border-b border-zinc-100 px-4 py-3 dark:border-zinc-800"><div><h2 className="text-xs font-semibold">Yanıt güveni</h2><p className="mt-0.5 text-[10px] text-zinc-400">Kaynak ve iddia kapsamı</p></div><ShieldCheck size={17} className={rag.insufficientContext ? "text-amber-500" : "text-emerald-500"} /></div><div className="grid grid-cols-2 gap-3 p-4"><CoverageMetric label="Atıf kapsamı" value={rag.citationCoverage} /><CoverageMetric label="İddia desteği" value={rag.claimSupportCoverage} /></div>{(rag.partialResult || rag.insufficientContext) && <p className="border-t border-amber-100 bg-amber-50 px-4 py-2 text-[10px] leading-4 text-amber-700 dark:border-amber-900 dark:bg-amber-950/30 dark:text-amber-300">{rag.insufficientContext ? "Bu yanıt için kaynak kapsamı sınırlı." : "Yanıt mevcut kaynaklarla kısmi üretildi."}</p>}</section>
    {sources.length > 0 && <section className="rounded-xl border border-zinc-200 bg-white dark:border-zinc-800 dark:bg-zinc-900"><div className="flex items-center justify-between border-b border-zinc-100 px-4 py-3 dark:border-zinc-800"><div><h2 className="text-xs font-semibold">Kaynaklar</h2><p className="mt-0.5 text-[10px] text-zinc-400">{rag.sources.length} kullanıldı · {rag.consultedSources.length} incelendi</p></div><FileText size={16} className="text-zinc-400" /></div><div className="divide-y divide-zinc-100 dark:divide-zinc-800">{sources.map(source => <SourceRow key={source.articleId} source={source} cited={citedIds.has(source.articleId)} onClick={() => recordClick(source.articleId)} />)}</div></section>}
    {rag.evidence.length > 0 && <section className="rounded-xl border border-zinc-200 bg-white dark:border-zinc-800 dark:bg-zinc-900"><div className="border-b border-zinc-100 px-4 py-3 dark:border-zinc-800"><h2 className="text-xs font-semibold">Kanıt pasajları</h2><p className="mt-0.5 text-[10px] text-zinc-400">Yanıtı destekleyen özgün bölümler</p></div><div className="space-y-2 p-3">{rag.evidence.map((item, index) => <details id={`assistant-evidence-${item.sourceId}`} key={`${item.sourceId}-${item.chunkId ?? index}`} className="group scroll-mt-24 rounded-lg border border-zinc-200 bg-zinc-50 open:bg-white dark:border-zinc-700 dark:bg-zinc-950 dark:open:bg-zinc-900"><summary className="flex cursor-pointer list-none items-center gap-2 px-3 py-2.5"><span className="flex h-6 min-w-6 items-center justify-center rounded-md bg-blue-100 px-1 text-[10px] font-bold text-blue-700 dark:bg-blue-950 dark:text-blue-300">{item.sourceId}</span><span className="min-w-0 flex-1 truncate text-[11px] font-medium text-zinc-700 dark:text-zinc-300">{item.sourceName || item.title}</span><ChevronRight size={13} className="shrink-0 text-zinc-400 transition-transform group-open:rotate-90" /></summary><div className="border-t border-zinc-200 px-3 py-3 dark:border-zinc-700"><p className="whitespace-pre-wrap text-[11px] leading-5 text-zinc-600 dark:text-zinc-400">{item.passage}</p><Link to={`/articles/${item.slug}`} target="_blank" rel="noopener noreferrer" onClick={() => recordClick(item.articleId)} className="mt-3 inline-flex items-center gap-1 text-[10px] font-semibold text-blue-600 hover:underline">Kaynağı aç{item.pageNumber ? ` · sayfa ${item.pageNumber}` : ""}<ExternalLink size={11} /></Link></div></details>)}</div></section>}
    <p className="px-1 text-[10px] text-zinc-400">İzlenebilirlik: <span className="font-mono">{response.traceId.slice(0, 12)}</span></p>
  </div>;
}

function EvidenceEmptyState() { return <div className="rounded-xl border border-dashed border-zinc-300 px-5 py-8 text-center dark:border-zinc-700"><PanelRight size={22} className="mx-auto mb-3 text-zinc-300 dark:text-zinc-700" /><h2 className="text-xs font-semibold text-zinc-700 dark:text-zinc-300">Kaynak inceleme paneli</h2><p className="mt-2 text-[11px] leading-5 text-zinc-400">Kullanılan dokümanlar, güven sinyalleri ve kanıt pasajları burada görünür.</p></div>; }
function SourceRow({ source, cited, onClick }: { source: RagSource; cited: boolean; onClick: () => void }) {
  return <Link to={`/articles/${source.slug}`} target="_blank" rel="noopener noreferrer" onClick={onClick} className="group block px-4 py-3 hover:bg-zinc-50 dark:hover:bg-zinc-800/50"><div className="flex items-start gap-2.5"><span className={cn("mt-0.5 flex h-7 w-7 shrink-0 items-center justify-center rounded-lg", cited ? "bg-blue-50 text-blue-600 dark:bg-blue-950" : "bg-zinc-100 text-zinc-500 dark:bg-zinc-800")}><FileText size={14} /></span><span className="min-w-0 flex-1"><span className="flex items-start gap-1"><span className="line-clamp-2 flex-1 text-[11px] font-semibold leading-4 text-zinc-800 group-hover:text-blue-600 dark:text-zinc-200">{source.title}</span><ExternalLink size={11} className="mt-0.5 text-zinc-300" /></span><span className="mt-1.5 flex flex-wrap items-center gap-1.5"><span className={cn("rounded px-1.5 py-0.5 text-[9px] font-semibold", cited ? "bg-emerald-50 text-emerald-700 dark:bg-emerald-950/50" : "bg-zinc-100 text-zinc-500 dark:bg-zinc-800")}>{cited ? "Yanıtta kullanıldı" : "İncelendi"}</span>{source.approved && <span className="inline-flex items-center gap-0.5 text-[9px] text-zinc-400"><Check size={9} className="text-emerald-500" />Onaylı</span>}</span></span></div></Link>;
}
function CoverageMetric({ label, value }: { label: string; value: number }) { const percentage = Math.round(value * 100); return <div><div className="mb-1.5 flex justify-between gap-2"><span className="text-[10px] text-zinc-500">{label}</span><span className="text-[10px] font-semibold tabular-nums">%{percentage}</span></div><div className="h-1.5 overflow-hidden rounded-full bg-zinc-100 dark:bg-zinc-800"><div className={cn("h-full rounded-full", percentage >= 80 ? "bg-emerald-500" : percentage >= 50 ? "bg-amber-500" : "bg-red-500")} style={{ width: `${Math.min(100, Math.max(0, percentage))}%` }} /></div></div>; }
function GroundingBadge({ status, insufficient }: { status: string; insufficient: boolean }) { return <span className={cn("inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[9px] font-semibold capitalize", insufficient ? "bg-amber-50 text-amber-700 dark:bg-amber-950/50" : "bg-emerald-50 text-emerald-700 dark:bg-emerald-950/50")}><ShieldCheck size={10} />{insufficient ? "Sınırlı kaynak" : status.replaceAll("_", " ")}</span>; }
function formatConversationDate(value: string) { const date = new Date(value); return date.toDateString() === new Date().toDateString() ? date.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" }) : date.toLocaleDateString("tr-TR", { day: "2-digit", month: "short" }); }
function formatMessageTime(value: string) { return new Date(value).toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" }); }
function formatResponseTime(milliseconds: number) { return milliseconds >= 1000 ? `${(milliseconds / 1000).toFixed(1)} sn` : `${milliseconds} ms`; }
