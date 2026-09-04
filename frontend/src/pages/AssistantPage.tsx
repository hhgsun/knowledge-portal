import { Fragment, useCallback, useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { AlertTriangle, Bot, BookOpen, Check, ChevronDown, ChevronRight, Copy, Database, ExternalLink, FileText, Loader2, Send, ShieldCheck, Sparkles, Square, ThumbsDown, ThumbsUp } from "lucide-react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { toast } from "sonner";
import { useCapabilities } from "../contexts/CapabilitiesContext";
import { useApi } from "../hooks/useApi";
import { readApiError, readApiJson } from "../lib/api-response";
import { cn } from "../lib/utils";
import { DropdownSelector } from "../components/ui/dropdown-selector";
import type { AssistantAnswerProfile, AssistantContentBlock, AssistantResponse, LlmModelSettings, RagSource } from "../types/api";

const assistantModelStorageKey = "knowledge-portal.assistant.model";
const assistantProfileStorageKey = "knowledge-portal.assistant.answer-profile";

const starterQuestions = [
  { icon: ShieldCheck, label: "Yetki ve kontroller", question: "Knowledge Portal'daki rollerin temel yetkileri ve API key erişim kısıtlamaları nelerdir?" },
  { icon: BookOpen, label: "Süreç özeti", question: "Yeni bir çalışan için ilk hafta tamamlanması gereken adımları özetle." },
  { icon: Database, label: "Karşılaştırmalı yanıt", question: "İlgili dokümanlardaki kuralları karşılaştır ve varsa çelişkileri belirt." },
];

interface SessionExchange {
  id: string;
  question: string;
  response: AssistantResponse;
}

export default function AssistantPage() {
  const { fetchWithAuth } = useApi();
  const { capabilities } = useCapabilities();
  const [searchParams] = useSearchParams();
  const [message, setMessage] = useState(() => searchParams.get("q")?.trim() ?? "");
  const [loading, setLoading] = useState(false);
  const [exchanges, setExchanges] = useState<SessionExchange[]>([]);
  const [streamedText, setStreamedText] = useState("");
  const [streamStage, setStreamStage] = useState("");
  const [pendingQuestion, setPendingQuestion] = useState<string | null>(null);
  const [modelSettings, setModelSettings] = useState<LlmModelSettings>();
  const [selectedModel, setSelectedModel] = useState("");
  const [selectedProfile, setSelectedProfile] = useState<AssistantAnswerProfile | "">(() => {
    const stored = localStorage.getItem(assistantProfileStorageKey);
    return stored === "compact" || stored === "balanced" || stored === "comprehensive" ? stored : "";
  });
  const conversationIdRef = useRef<string | null>(null);
  const abortRef = useRef<AbortController | null>(null);
  const textareaRef = useRef<HTMLTextAreaElement | null>(null);
  const endRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => () => abortRef.current?.abort(), []);
  useEffect(() => { endRef.current?.scrollIntoView({ behavior: loading ? "smooth" : "auto", block: "end" }); }, [exchanges, loading, streamedText]);
  useEffect(() => {
    let active = true;
    void (async () => {
      try {
        const response = await fetchWithAuth("/api/llm-models");
        if (!response.ok) return;
        const settings = await readApiJson<LlmModelSettings>(response);
        if (!active) return;
        setModelSettings(settings);
        const stored = localStorage.getItem(assistantModelStorageKey);
        const canonical = settings.models.find(model =>
          model.id.toLowerCase() === stored?.toLowerCase())?.id;
        if (canonical) setSelectedModel(canonical);
        else {
          if (stored && settings.catalogSource === "ollama")
            localStorage.removeItem(assistantModelStorageKey);
          setSelectedModel("");
        }
      } catch {
        // Assistant requests still use the server-side admin default if the catalog cannot load.
      }
    })();
    return () => { active = false; };
  }, [fetchWithAuth]);

  const changeModel = (model: string) => {
    setSelectedModel(model);
    if (model) localStorage.setItem(assistantModelStorageKey, model);
    else localStorage.removeItem(assistantModelStorageKey);
  };

  const changeProfile = (profile: AssistantAnswerProfile | "") => {
    setSelectedProfile(profile);
    if (profile) localStorage.setItem(assistantProfileStorageKey, profile);
    else localStorage.removeItem(assistantProfileStorageKey);
  };

  const ensureSessionConversation = useCallback(async () => {
    if (!capabilities?.conversationHistoryEnabled) return null;
    if (conversationIdRef.current) return conversationIdRef.current;
    try {
      const result = await fetchWithAuth("/api/assistant/conversations", { method: "POST", noRetry: true });
      if (!result.ok) throw new Error(await readApiError(result, "Oturum konuşması başlatılamadı."));
      const item = await result.json() as { id: string };
      conversationIdRef.current = item.id;
      return item.id;
    } catch (error) {
      toast.error(error instanceof Error ? error.message : "Oturum konuşması başlatılamadı.");
      return null;
    }
  }, [capabilities?.conversationHistoryEnabled, fetchWithAuth]);

  useEffect(() => {
    if (capabilities?.conversationHistoryEnabled) void ensureSessionConversation();
  }, [capabilities?.conversationHistoryEnabled, ensureSessionConversation]);

  const execute = async (rawText: string) => {
    const text = rawText.trim();
    if (!text || loading) return;
    const controller = new AbortController(); abortRef.current = controller;
    setLoading(true); setPendingQuestion(text); setMessage(""); setStreamedText(""); setStreamStage("Yetkili bilgi kapsamı denetleniyor");
    try {
      let activeConversation = conversationIdRef.current;
      if (capabilities?.conversationHistoryEnabled && !activeConversation) {
        activeConversation = await ensureSessionConversation();
        if (!activeConversation) throw new Error("Konuşma başlatılamadı.");
      }
      const streaming = capabilities?.streamingEnabled ?? true;
      const result = await fetchWithAuth(streaming ? "/api/assistant/stream" : "/api/assistant", {
        method: "POST", noRetry: true, signal: controller.signal,
        body: JSON.stringify({
          message: text, conversationId: activeConversation,
          model: selectedModel || undefined,
          answerProfile: selectedProfile || undefined
        }),
      });
      if (!result.ok) throw new Error(await readApiError(result, "Asistan isteği tamamlanamadı."));
      let completedResponse: AssistantResponse | null = null;
      if (!streaming || !result.body) completedResponse = await readApiJson<AssistantResponse>(result);
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
            if (event === "complete") completedResponse = data as AssistantResponse;
          }
          if (done) break;
        }
      }
      if (!completedResponse) throw new Error("Asistan yanıtı tamamlanamadı.");
      setExchanges(current => [...current, {
        id: completedResponse.interactionId ?? completedResponse.traceId,
        question: text,
        response: completedResponse,
      }]);
    } catch (error) {
      if (error instanceof DOMException && error.name === "AbortError") toast.info("Asistan isteği iptal edildi.");
      else { setMessage(current => current || text); toast.error(error instanceof Error ? error.message : "Asistan isteği tamamlanamadı."); }
    } finally {
      if (abortRef.current === controller) abortRef.current = null;
      setLoading(false); setPendingQuestion(null); setStreamedText(""); setStreamStage("");
    }
  };

  const hasContent = exchanges.length > 0 || loading;

  return <div className="flex h-full min-h-0 flex-col">
    <AssistantHeader settings={modelSettings} selectedModel={selectedModel}
      selectedProfile={selectedProfile} loading={loading} onModelChange={changeModel}
      profiles={capabilities?.answerProfiles ?? ["compact", "balanced", "comprehensive"]}
      defaultProfile={capabilities?.defaultAnswerProfile ?? "balanced"}
      onProfileChange={changeProfile} />
    <main className="subtle-scrollbar min-h-0 min-w-0 flex-1 overflow-y-auto" aria-busy={loading}>
      <div className="mx-auto flex min-h-full w-full max-w-4xl flex-col px-6 py-10">
        {!hasContent ? <WelcomeState onQuestion={question => void execute(question)} /> :
          <div className="space-y-7" aria-live="polite">
            {exchanges.map(exchange => <Fragment key={exchange.id}>
              <UserMessage content={exchange.question} />
              <AssistantResult response={exchange.response} feedbackEnabled={capabilities?.feedbackEnabled ?? true} />
            </Fragment>)}
            {pendingQuestion && <UserMessage content={pendingQuestion} />}
            {loading && <StreamingAnswer text={streamedText} stage={streamStage} />}
            <div ref={endRef} />
          </div>}
      </div>
    </main>
    <Composer inputRef={textareaRef} message={message} loading={loading} maxLength={capabilities?.maxMessageCharacters ?? 4000} onChange={setMessage} onSubmit={() => void execute(message)} onCancel={() => abortRef.current?.abort()} />
  </div>;
}

function AssistantHeader({ settings, selectedModel, selectedProfile, profiles, defaultProfile, loading, onModelChange, onProfileChange }: {
  settings?: LlmModelSettings;
  selectedModel: string;
  selectedProfile: AssistantAnswerProfile | "";
  profiles: AssistantAnswerProfile[];
  defaultProfile: AssistantAnswerProfile;
  loading: boolean;
  onModelChange: (model: string) => void;
  onProfileChange: (profile: AssistantAnswerProfile | "") => void;
}) {
  return <header className="relative z-10 mx-auto flex w-full max-w-5xl shrink-0 flex-col items-start justify-between gap-4 bg-white px-6 pb-2 pt-6 dark:bg-zinc-950 sm:flex-row">
    <div className="flex min-w-0 items-center gap-3">
      <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-blue-50 text-blue-600 dark:bg-blue-950/50 dark:text-blue-300"><Bot size={22} /></div>
      <div className="min-w-0"><h1 className="text-2xl font-bold text-zinc-900 dark:text-zinc-100">Bilgi Asistanı</h1><p className="mt-1 text-sm text-zinc-500">Yetkiniz kapsamındaki kaynaklardan izlenebilir yanıtlar</p></div>
    </div>
    <div className="flex w-full shrink-0 items-center gap-2 sm:w-auto">
      <span className="inline-flex items-center gap-1.5 rounded-full bg-zinc-100 px-2.5 py-1 text-[10px] font-medium text-zinc-500 dark:bg-zinc-800 dark:text-zinc-400">Tek oturum</span>
      <DropdownSelector id="assistant-model" label={`Varsayılan Model${settings ? ` — ${settings.defaultModel}` : ""}`}
        options={settings?.models.map(model => ({ value: model.id, label: `${model.label} (${model.id})`, searchText: model.id })) ?? []}
        selected={selectedModel ? [selectedModel] : []}
        onChange={values => onModelChange(values[0] ?? "")}
        disabled={!settings || loading}
        clearable
        searchable={(settings?.models.length ?? 0) > 10}
        title={settings?.catalogWarning ?? "Bu seçim yalnızca bu tarayıcıda saklanır."}
        panelAlign="end"
        className="min-w-0 flex-1 sm:max-w-56" />
      <DropdownSelector id="assistant-answer-profile" label={`Otomatik — ${answerProfileLabel(defaultProfile)}`}
        options={profiles.map(profile => ({ value: profile, label: answerProfileLabel(profile) }))}
        selected={selectedProfile ? [selectedProfile] : []}
        onChange={values => onProfileChange((values[0] ?? "") as AssistantAnswerProfile | "")}
        disabled={loading}
        clearable
        title="Otomatik seçim, detaylı ve kapsamlı soruları geniş yanıta yükseltir."
        panelAlign="end"
        className="min-w-32" />
    </div>
    <div aria-hidden="true" className="pointer-events-none absolute inset-x-0 top-full h-10 bg-gradient-to-b from-white to-transparent dark:from-zinc-950" />
  </header>;
}

function WelcomeState({ onQuestion }: { onQuestion: (question: string) => void }) {
  return <section className="my-auto py-8 sm:py-14">
    <div className="mx-auto max-w-2xl text-center">
      {/* <div className="mx-auto mb-5 flex h-14 w-14 items-center justify-center rounded-2xl border border-blue-100 bg-blue-50 text-blue-600 dark:border-blue-900 dark:bg-blue-950/40 dark:text-blue-300">
        <Bot size={26} />
      </div> */}
      {/* <p className="mb-2 text-xs font-semibold uppercase tracking-[0.18em] text-blue-600 dark:text-blue-400">Kurumsal bilgi, tek bir yerde</p> */}
      <h2 className="text-2xl font-semibold tracking-tight text-zinc-950 dark:text-zinc-50 sm:text-3xl">Nasıl yardımcı olabilirim?</h2>
      <p className="mx-auto mt-3 max-w-xl text-sm leading-6 text-zinc-500">Politikaları özetleyin, süreçleri karşılaştırın veya bir kararın dayanağını sorun. Her yanıt erişebildiğiniz portal kaynaklarına bağlanır.</p>
    </div>
    <div className="mx-auto mt-8 grid max-w-3xl gap-3 md:grid-cols-3">{starterQuestions.map(item => <button key={item.label} type="button" onClick={() => onQuestion(item.question)} className="group rounded-xl border border-zinc-200 bg-white p-4 text-left transition-all hover:-translate-y-0.5 hover:border-blue-300 hover:shadow-md dark:border-zinc-800 dark:bg-zinc-950 dark:hover:border-blue-800"><span className="mb-6 flex h-8 w-8 items-center justify-center rounded-lg bg-zinc-100 text-zinc-600 group-hover:bg-blue-50 group-hover:text-blue-600 dark:bg-zinc-800 dark:text-zinc-300"><item.icon size={16} /></span><span className="block text-xs font-semibold text-zinc-900 dark:text-zinc-100">{item.label}</span><span className="mt-1.5 block text-xs leading-5 text-zinc-500">{item.question}</span><span className="mt-3 flex items-center gap-1 text-[11px] font-medium text-blue-600 opacity-0 group-hover:opacity-100">Soruyu kullan <ChevronRight size={12} /></span></button>)}</div>
    <div className="mx-auto mt-7 flex max-w-xl items-start gap-2 rounded-lg bg-zinc-100/70 px-3 py-2 text-[11px] leading-4 text-zinc-500 dark:bg-zinc-800/50"><ShieldCheck size={14} className="mt-0.5 shrink-0 text-emerald-600" />Asistan yalnızca yayımlanmış makaleleri kullanır; size ait taslak ve arşivlenmiş makaleler de kaynaklara dahil edilmez. Kritik kararları kaynak bağlantılarından doğrulayın.</div>
  </section>;
}

function UserMessage({ content }: { content: string }) { return <div className="flex justify-end"><div className="max-w-[88%] rounded-2xl rounded-br-md bg-zinc-900 px-4 py-3 text-sm leading-6 text-white shadow-sm dark:bg-zinc-100 dark:text-zinc-900 sm:max-w-[76%]">{content}</div></div>; }
function AssistantAvatar() { return <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-blue-600 text-white"><Sparkles size={15} /></span>; }
function StreamingAnswer({ text, stage }: { text: string; stage: string }) {
  return <div className="flex items-start gap-3"><AssistantAvatar /><div className="min-w-0 flex-1 pt-1"><div className="mb-3 flex items-center gap-2 text-xs font-semibold text-zinc-800 dark:text-zinc-200">Bilgi Asistanı <span className="font-normal text-blue-600">çalışıyor</span></div>{text ? <div className="prose prose-sm max-w-none text-zinc-700 dark:prose-invert dark:text-zinc-300"><ReactMarkdown remarkPlugins={[remarkGfm]}>{text}</ReactMarkdown><span className="ml-1 inline-block h-4 w-1 animate-pulse rounded-full bg-blue-500 align-middle" /></div> : <div className="rounded-xl border border-blue-100 bg-blue-50/60 px-4 py-3 dark:border-blue-900 dark:bg-blue-950/20"><div className="flex items-center gap-2 text-xs font-medium text-blue-700 dark:text-blue-300"><Loader2 size={14} className="animate-spin" />{stage || "Kaynaklar değerlendiriliyor"}</div><div className="mt-2 h-1 overflow-hidden rounded-full bg-blue-100 dark:bg-blue-950"><div className="h-full w-1/3 animate-pulse rounded-full bg-blue-500" /></div></div>}</div></div>;
}

type ComposerProps = { inputRef: React.RefObject<HTMLTextAreaElement | null>; message: string; loading: boolean; maxLength: number; onChange: (value: string) => void; onSubmit: () => void; onCancel: () => void };
function Composer({ inputRef, message, loading, maxLength, onChange, onSubmit, onCancel }: ComposerProps) {
  return <div className="relative z-10 shrink-0 bg-white px-6 pb-6 pt-0 dark:bg-zinc-950"><div aria-hidden="true" className="pointer-events-none absolute inset-x-0 bottom-full h-10 bg-gradient-to-t from-white to-transparent dark:from-zinc-950" />
    <form onSubmit={event => { event.preventDefault(); onSubmit(); }} className="mx-auto max-w-4xl relative z-1"><div className="rounded-xl border border-zinc-300 bg-white p-2 shadow-sm transition focus-within:border-blue-500 focus-within:ring-4 focus-within:ring-blue-500/10 dark:border-zinc-700 dark:bg-zinc-900"><textarea ref={inputRef} value={message} onChange={event => onChange(event.target.value)} onKeyDown={event => { if (event.key === "Enter" && !event.shiftKey && !event.nativeEvent.isComposing) { event.preventDefault(); event.currentTarget.form?.requestSubmit(); } }} maxLength={maxLength} rows={2} placeholder="Kurumsal bilginiz hakkında bir soru sorun…" className="subtle-scrollbar max-h-40 min-h-12 w-full resize-none bg-transparent px-2 py-1.5 text-sm leading-6 outline-none placeholder:text-zinc-400" aria-label="Bilgi Asistanına sorun" /><div className="flex items-end justify-between gap-3 px-1"><div className="flex min-w-0 items-center gap-2 text-[10px] text-zinc-400 sm:text-[11px]"><ShieldCheck size={13} className="shrink-0 text-emerald-500" /><span className="truncate">Yalnızca yayımlanmış makaleler kullanılır</span>{message.length > maxLength * .8 && <span className="shrink-0 tabular-nums">{message.length}/{maxLength}</span>}</div>{loading ? <button type="button" onClick={onCancel} className="inline-flex h-9 items-center gap-2 rounded-lg border border-red-200 px-3 text-xs font-medium text-red-600 hover:bg-red-50 dark:border-red-900"><Square size={12} />Durdur</button> : <button type="submit" disabled={!message.trim()} className="inline-flex h-9 items-center gap-2 rounded-lg bg-blue-600 px-3.5 text-xs font-semibold text-white shadow-sm hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-40"><Send size={14} />Gönder</button>}</div></div><p className="mt-2 text-center text-[10px] text-zinc-400">Enter ile gönderin · Shift + Enter ile yeni satır ekleyin · Önemli bilgileri bağlı kaynaklardan doğrulayın</p>
    </form>
  </div>;
}

function AssistantResult({ response, feedbackEnabled }: { response: AssistantResponse; feedbackEnabled: boolean }) {
  const { fetchWithAuth } = useApi();
  const [feedback, setFeedback] = useState<"helpful" | "not_helpful" | null>(null);
  const [feedbackReason, setFeedbackReason] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [copied, setCopied] = useState(false);
  const sourceLinks = new Map(response.rag?.evidence.map(item => [item.sourceId, `/articles/${item.slug}`]) ?? []);
  const answerMarkdown = linkCitations(response.answer ?? "", sourceLinks);
  const sendFeedback = async (helpful: boolean) => {
    if (!response.interactionId || submitting) return;
    setSubmitting(true);
    try { const result = await fetchWithAuth("/api/assistant/feedback", { method: "POST", noRetry: true, body: JSON.stringify({ interactionId: response.interactionId, helpful, reason: feedbackReason || null }) }); if (!result.ok) throw new Error(await readApiError(result, "Geri bildirim kaydedilemedi.")); setFeedback(helpful ? "helpful" : "not_helpful"); toast.success("Geri bildiriminiz kaydedildi."); }
    catch (error) { toast.error(error instanceof Error ? error.message : "Geri bildirim kaydedilemedi."); } finally { setSubmitting(false); }
  };
  const copyAnswer = async () => { if (!response.answer) return; try { await navigator.clipboard.writeText(response.answer); setCopied(true); setTimeout(() => setCopied(false), 1800); } catch { toast.error("Yanıt kopyalanamadı."); } };
  return <div className="flex items-start gap-3"><AssistantAvatar /><section className="min-w-0 flex-1 pt-1" aria-label="Asistan yanıtı">
    <div className="mb-3 flex flex-wrap items-center justify-between gap-2"><div className="flex flex-wrap items-center gap-2"><span className="text-xs font-semibold text-zinc-800 dark:text-zinc-200">Bilgi Asistanı</span>{response.rag && <GroundingBadge status={response.rag.groundingStatus} insufficient={response.rag.insufficientContext} />}<span className="rounded bg-blue-50 px-1.5 py-0.5 text-[9px] font-medium text-blue-600 dark:bg-blue-950/40 dark:text-blue-300">{answerProfileLabel(response.answerProfile)}</span><span className="text-[10px] text-zinc-400">{formatResponseTime(response.responseTimeMs)}</span><span className="text-[10px] tabular-nums text-zinc-400" title={`${response.tokenUsage.estimated ? "Tahmini · " : ""}Girdi: ${response.tokenUsage.inputTokens.toLocaleString("tr-TR")} · Çıktı: ${response.tokenUsage.outputTokens.toLocaleString("tr-TR")}`} aria-label={`${response.tokenUsage.estimated ? "Tahmini " : ""}token kullanımı: ${response.tokenUsage.totalTokens}; girdi ${response.tokenUsage.inputTokens}, çıktı ${response.tokenUsage.outputTokens}`}>{response.tokenUsage.estimated && "~"}{response.tokenUsage.totalTokens.toLocaleString("tr-TR")} token</span>{response.cacheHit && <span className="rounded bg-zinc-100 px-1.5 py-0.5 text-[9px] uppercase text-zinc-500 dark:bg-zinc-800">önbellek</span>}</div>{response.answer && <button type="button" onClick={() => void copyAnswer()} className="inline-flex items-center gap-1.5 rounded-md px-2 py-1 text-[11px] text-zinc-500 hover:bg-zinc-100 dark:hover:bg-zinc-800">{copied ? <Check size={13} className="text-emerald-500" /> : <Copy size={13} />}{copied ? "Kopyalandı" : "Kopyala"}</button>}</div>
    {response.answer ? <div className="prose prose-sm max-w-none text-zinc-700 prose-a:font-semibold prose-a:text-blue-600 prose-a:no-underline hover:prose-a:underline dark:prose-invert dark:text-zinc-300 dark:prose-a:text-blue-400">{response.contentBlocks?.length ? <AssistantContentBlocks blocks={response.contentBlocks} sourceLinks={sourceLinks} /> : <ReactMarkdown remarkPlugins={[remarkGfm]}>{answerMarkdown}</ReactMarkdown>}</div> : <div className="rounded-xl border border-zinc-200 bg-zinc-50 p-4 text-sm text-zinc-600 dark:border-zinc-700 dark:bg-zinc-800/40">Bu soru için yeterli ve güvenilir bir yanıt üretilemedi.</div>}
    <AnswerSources response={response} />
    <AnswerWarnings warnings={response.warnings} />
    {feedbackEnabled && response.interactionId && <div className="mt-5 flex flex-wrap items-center gap-2 border-t border-zinc-100 pt-4 text-xs dark:border-zinc-800"><span className="mr-1 text-zinc-500">Bu yanıt yararlı mıydı?</span><FeedbackButton active={feedback === "helpful"} positive disabled={submitting} onClick={() => void sendFeedback(true)} /><FeedbackButton active={feedback === "not_helpful"} disabled={submitting} onClick={() => void sendFeedback(false)} /><DropdownSelector label="Neden? (isteğe bağlı)" options={[{ value: "incorrect", label: "Yanlış bilgi" }, { value: "incomplete", label: "Eksik yanıt" }, { value: "wrong_source", label: "Yanlış kaynak" }, { value: "outdated", label: "Güncel değil" }, { value: "no_answer", label: "Yanıt yok" }, { value: "other", label: "Diğer" }]} selected={feedbackReason ? [feedbackReason] : []} onChange={values => setFeedbackReason(values[0] ?? "")} clearable compact /></div>}
  </section></div>;
}

function linkCitations(text: string, sourceLinks: Map<string, string>) {
  return text.replace(/\[(S\d+)\](?!\()/g, (citation, sourceId: string) => {
    const href = sourceLinks.get(sourceId);
    return href ? `[${sourceId}](${href})` : citation;
  });
}

function AssistantContentBlocks({ blocks, sourceLinks }: { blocks: AssistantContentBlock[]; sourceLinks: Map<string, string> }) {
  const markdown = (value: string) => <ReactMarkdown remarkPlugins={[remarkGfm]}>{linkCitations(value, sourceLinks)}</ReactMarkdown>;
  return <div className="space-y-4">{blocks.map((block, blockIndex) => {
    if (block.type === "ordered_list" || block.type === "bullet_list") {
      const List = block.type === "ordered_list" ? "ol" : "ul";
      return <List key={blockIndex} className={block.type === "ordered_list" ? "list-decimal" : "list-disc"}>{(block.items ?? []).map((item, index) => <li key={index}>{markdown(item)}</li>)}</List>;
    }
    if (block.type === "table") return <div key={blockIndex} className="not-prose overflow-x-auto rounded-lg border border-zinc-200 dark:border-zinc-700">{block.title && <div className="border-b border-zinc-200 bg-zinc-50 px-3 py-2 text-xs font-semibold dark:border-zinc-700 dark:bg-zinc-800/50">{block.title}</div>}<table className="w-full text-left text-xs"><thead><tr>{(block.headers ?? []).map((header, index) => <th key={index} className="border-b border-zinc-200 px-3 py-2 font-semibold dark:border-zinc-700">{header}</th>)}</tr></thead><tbody>{(block.rows ?? []).map((row, rowIndex) => <tr key={rowIndex} className="border-b border-zinc-100 last:border-0 dark:border-zinc-800">{row.map((cell, cellIndex) => <td key={cellIndex} className="px-3 py-2 align-top">{markdown(cell)}</td>)}</tr>)}</tbody></table></div>;
    if (block.type === "infographic") return <div key={blockIndex} className="not-prose"><div className="mb-2 text-xs font-semibold text-zinc-500">{block.title}</div><div className="grid gap-3 sm:grid-cols-2">{(block.items ?? []).map((item, index) => <div key={index} className="rounded-xl border border-blue-100 bg-gradient-to-br from-blue-50 to-white p-4 dark:border-blue-900/60 dark:from-blue-950/40 dark:to-zinc-900"><div className="mb-2 text-2xl font-bold text-blue-600 dark:text-blue-400">{String(index + 1).padStart(2, "0")}</div><div className="text-sm text-zinc-700 dark:text-zinc-200">{markdown(item)}</div></div>)}</div></div>;
    if (block.type === "process_flow") return <div key={blockIndex} className="not-prose"><div className="space-y-2">{(block.items ?? []).map((item, index) => <div key={index} className="flex items-stretch gap-2"><div className="flex w-7 shrink-0 items-center justify-center rounded-md bg-blue-600 text-xs font-bold text-white">{index + 1}</div><div className="min-w-0 flex-1 rounded-lg border border-zinc-200 bg-zinc-50 px-3 py-2 text-sm dark:border-zinc-700 dark:bg-zinc-800/40">{markdown(item)}</div>{index < (block.items?.length ?? 0) - 1 && <ChevronRight size={14} className="hidden self-center text-zinc-400 sm:block" />}</div>)}</div></div>;
    return <div key={blockIndex}>{markdown(block.text ?? "")}</div>;
  })}</div>;
}

function FeedbackButton({ active, positive = false, disabled, onClick }: { active: boolean; positive?: boolean; disabled: boolean; onClick: () => void }) {
  const color = positive ? "hover:border-emerald-300 hover:bg-emerald-50 hover:text-emerald-700" : "hover:border-red-300 hover:bg-red-50 hover:text-red-700";
  return <button type="button" disabled={disabled} onClick={onClick} aria-pressed={active} className={cn("rounded-lg border border-zinc-200 p-2 text-zinc-500 dark:border-zinc-700", color, active && (positive ? "border-emerald-300 bg-emerald-50 text-emerald-700" : "border-red-300 bg-red-50 text-red-700"))} aria-label={positive ? "Yararlı" : "Yararlı değil"}>{positive ? <ThumbsUp size={14} /> : <ThumbsDown size={14} />}</button>;
}

function AnswerSources({ response }: { response: AssistantResponse }) {
  const { fetchWithAuth } = useApi();
  const sources = response.rag?.sources ?? [];
  const consultedSources = (response.rag?.consultedSources ?? []).filter(source =>
    !sources.some(cited => cited.articleId === source.articleId));
  const recordClick = (articleId: string) => { if (response.interactionId) void fetchWithAuth("/api/assistant/source-click", { method: "POST", noRetry: true, body: JSON.stringify({ interactionId: response.interactionId, articleId }) }).catch(() => undefined); };
  return <section className="mt-5 border-t border-zinc-100 pt-4 dark:border-zinc-800" aria-label="Yanıt kaynakları">
    <div className="mb-1 flex items-center gap-2"><FileText size={14} className="text-zinc-400" /><h3 className="text-xs font-semibold text-zinc-700 dark:text-zinc-300">Kullanılan kaynaklar</h3></div>
    <p className="mb-2.5 text-[10px] text-zinc-400">Cevaptaki iddiaları doğrudan destekleyen makaleler.</p>
    {sources.length > 0 ? <div className="grid gap-2 sm:grid-cols-2">{sources.map(source => <AnswerSourceLink key={source.articleId} source={source} sourceIds={sourceIdsFor(response, source)} onClick={() => recordClick(source.articleId)} />)}</div>
      : <p className="rounded-lg bg-zinc-50 px-3 py-2 text-[11px] text-zinc-500 dark:bg-zinc-800/50 dark:text-zinc-400">Bu yanıtla ilişkilendirilen bir kaynak bulunamadı.</p>}
    {consultedSources.length > 0 && <details className="mt-3 rounded-lg border border-zinc-200 dark:border-zinc-700"><summary className="cursor-pointer px-3 py-2 text-[11px] font-medium text-zinc-600 dark:text-zinc-300">İncelenen diğer kaynaklar ({consultedSources.length})</summary><p className="border-t border-zinc-200 px-3 py-2 text-[10px] text-zinc-400 dark:border-zinc-700">Asistan bu makaleleri değerlendirdi, ancak cevaptaki iddialar için doğrudan dayanak olarak kullanmadı.</p><div className="grid gap-2 border-t border-zinc-100 p-2 dark:border-zinc-800 sm:grid-cols-2">{consultedSources.map(source => <AnswerSourceLink key={`consulted-${source.articleId}`} source={source} sourceIds={[]} onClick={() => recordClick(source.articleId)} />)}</div></details>}
  </section>;
}

function sourceIdsFor(response: AssistantResponse, source: RagSource) {
  const sourceIds = response.rag?.evidence.filter(item => item.articleId === source.articleId).map(item => item.sourceId) ?? [];
  return [...new Set(sourceIds)];
}

function AnswerSourceLink({ source, sourceIds, onClick }: { source: RagSource; sourceIds: string[]; onClick: () => void }) {
  return <Link to={`/articles/${source.slug}`} target="_blank" rel="noopener noreferrer" onClick={onClick} className="group flex min-w-0 items-center gap-2.5 rounded-lg border border-zinc-200 bg-zinc-50 px-3 py-2.5 hover:border-blue-300 hover:bg-blue-50/50 dark:border-zinc-700 dark:bg-zinc-800/40 dark:hover:border-blue-800 dark:hover:bg-blue-950/20">
    <span className="flex min-w-8 shrink-0 items-center justify-center rounded-md bg-blue-100 px-1.5 py-1 text-[9px] font-bold text-blue-700 dark:bg-blue-950 dark:text-blue-300">{sourceIds.length > 0 ? sourceIds.join(", ") : <FileText size={12} />}</span>
    <span className="min-w-0 flex-1"><span className="line-clamp-2 block text-[11px] font-semibold leading-4 text-zinc-800 group-hover:text-blue-700 dark:text-zinc-200 dark:group-hover:text-blue-300">{source.title}</span>{source.approved && <span className="mt-1 inline-flex items-center gap-0.5 text-[9px] text-zinc-400"><Check size={9} className="text-emerald-500" />Onaylı</span>}</span>
    <ExternalLink size={12} className="shrink-0 text-zinc-300 group-hover:text-blue-500" />
  </Link>;
}
function GroundingBadge({ status, insufficient }: { status: string; insufficient: boolean }) { return <span title={groundingDescription(status, insufficient)} className={cn("inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[9px] font-semibold", insufficient ? "bg-amber-50 text-amber-700 dark:bg-amber-950/50" : "bg-emerald-50 text-emerald-700 dark:bg-emerald-950/50")}><ShieldCheck size={10} />{groundingLabel(status, insufficient)}</span>; }
function groundingLabel(status: string, insufficient: boolean) {
  if (insufficient) return "Yeterli kaynak bulunamadı";
  return status === "lexically_grounded" ? "Kaynaklarla doğrulandı" : status === "partially_grounded" ? "Kısmen doğrulandı" : "Kaynak kontrolü tamamlandı";
}
function groundingDescription(status: string, insufficient: boolean) {
  if (insufficient) return "Asistan güvenilir bir cevap oluşturmak için yeterli destek bulamadı.";
  return status === "lexically_grounded" ? "Cevaptaki iddialar portal kaynaklarıyla eşleşiyor." : status === "partially_grounded" ? "Cevabın bazı bölümleri kaynaklarla destekleniyor; dikkatle kontrol edin." : "Cevap kaynak kontrolünden geçirildi.";
}
function AnswerWarnings({ warnings }: { warnings: string[] }) {
  if (warnings.length === 0) return null;
  return <details className="group mt-4 rounded-lg border border-amber-200 bg-amber-50/70 text-xs text-amber-800 dark:border-amber-900 dark:bg-amber-950/20 dark:text-amber-300">
    <summary className="flex cursor-pointer list-none items-center gap-2 rounded-lg px-3 py-2 font-medium outline-none hover:bg-amber-100/70 focus-visible:ring-2 focus-visible:ring-amber-500 dark:hover:bg-amber-950/40">
      <AlertTriangle size={14} className="shrink-0" />
      <span>{warnings.length} doğrulama uyarısı</span>
      <span className="ml-auto text-[10px] font-normal opacity-75 group-open:hidden">Ayrıntıları göster</span>
      <span className="ml-auto hidden text-[10px] font-normal opacity-75 group-open:inline">Ayrıntıları gizle</span>
      <ChevronDown size={14} className="shrink-0 transition-transform group-open:rotate-180" />
    </summary>
    <div className="max-h-56 space-y-2 overflow-y-auto border-t border-amber-200 px-3 py-3 leading-5 dark:border-amber-900">
      {warnings.map((warning, index) => <p key={`${index}-${warning}`} className="flex items-start gap-2"><AlertTriangle size={13} className="mt-0.5 shrink-0" /><span>{userWarning(warning)}</span></p>)}
    </div>
  </details>;
}
function userWarning(warning: string) {
  if (warning === "At least one source is past its review date and may be outdated.") return "En az bir kaynak gözden geçirme tarihini geçmiş olabilir ve güncel olmayabilir.";
  if (warning === "At least one source is approaching its review date.") return "En az bir kaynağın gözden geçirme tarihi yaklaşıyor.";
  if (warning === "At least one source has no recorded review date.") return "En az bir kaynak için gözden geçirme tarihi kaydedilmemiş.";
  if (warning === "Some sources have not been formally approved.") return "Bazı kaynaklar resmi olarak onaylanmamış.";
  if (warning === "Sources contain competing policy or numeric statements; review the cited sources before acting.") return "Kaynaklarda farklı politika veya sayısal bilgiler bulundu; işlem yapmadan önce kaynakları kontrol edin.";
  return warning;
}
function answerProfileLabel(profile: AssistantAnswerProfile) { return profile === "compact" ? "Kısa" : profile === "comprehensive" ? "Kapsamlı" : "Dengeli"; }
function formatResponseTime(milliseconds: number) { return milliseconds >= 1000 ? `${(milliseconds / 1000).toFixed(1)} sn` : `${milliseconds} ms`; }
