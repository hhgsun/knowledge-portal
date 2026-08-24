import { useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
import { useApi } from "../hooks/useApi";
import { toast } from "sonner";
import {
  AlertCircle, AlertTriangle, Bug, ChevronDown, ChevronRight,
  Clock, Download, FileJson, FileText, Filter, HardDrive, Info, RefreshCw,
  Search, Trash2, X,
} from "lucide-react";
import { cn } from "../lib/utils";

interface LogFile { fileName: string; sizeBytes: number; createdAt: string; lastModifiedAt: string; isToday: boolean; canDelete: boolean }
interface LogContent { fileName: string; totalLines: number; returnedLines: number; content: string }
type LogValue = string | number | boolean | null;
interface LogEvent { line: number; timestamp?: string; level: string; template?: string; message: string; exception?: string; source?: string; traceId?: string; properties: Record<string, LogValue>; raw: string; parseError: boolean }

const levelOrder = ["Fatal", "Error", "Warning", "Information", "Debug", "Verbose"];
const levelStyle: Record<string, string> = {
  Fatal: "bg-red-100 text-red-800 dark:bg-red-950/60 dark:text-red-300",
  Error: "bg-red-100 text-red-700 dark:bg-red-950/50 dark:text-red-300",
  Warning: "bg-amber-100 text-amber-800 dark:bg-amber-950/50 dark:text-amber-300",
  Information: "bg-blue-100 text-blue-700 dark:bg-blue-950/50 dark:text-blue-300",
  Debug: "bg-zinc-200 text-zinc-700 dark:bg-zinc-800 dark:text-zinc-300",
  Verbose: "bg-zinc-100 text-zinc-600 dark:bg-zinc-800 dark:text-zinc-400",
};

function normalizeLevel(value: unknown) {
  const level = String(value ?? "Information").toLowerCase();
  if (level === "fatal") return "Fatal";
  if (level === "error") return "Error";
  if (level === "warning" || level === "warn") return "Warning";
  if (level === "debug") return "Debug";
  if (level === "verbose" || level === "trace") return "Verbose";
  return "Information";
}

function renderTemplate(template: string, values: Record<string, unknown>) {
  return template.replace(/\{([^}:]+)(?::[^}]+)?\}/g, (match, key: string) => {
    const value = values[key];
    return value === undefined ? match : typeof value === "object" ? JSON.stringify(value) : String(value);
  });
}

function parseEvent(raw: string, line: number): LogEvent {
  try {
    const value = JSON.parse(raw) as Record<string, unknown>;
    const reserved = new Set(["@t", "@mt", "@m", "@l", "@x", "@tr", "@sp"]);
    const properties = Object.fromEntries(Object.entries(value).filter(([key, val]) => !reserved.has(key) && (val === null || ["string", "number", "boolean"].includes(typeof val)))) as Record<string, LogValue>;
    const template = typeof value["@mt"] === "string" ? value["@mt"] : undefined;
    const rendered = typeof value["@m"] === "string" ? value["@m"] : template ? renderTemplate(template, value) : "Log olayı";
    return { line, timestamp: typeof value["@t"] === "string" ? value["@t"] : undefined, level: normalizeLevel(value["@l"]), template, message: rendered, exception: typeof value["@x"] === "string" ? value["@x"] : undefined, source: typeof value.SourceContext === "string" ? value.SourceContext : undefined, traceId: typeof value["@tr"] === "string" ? value["@tr"] : undefined, properties, raw, parseError: false };
  } catch {
    return { line, level: "Information", message: raw || "(boş satır)", properties: {}, raw, parseError: true };
  }
}

function LevelIcon({ level }: { level: string }) {
  if (level === "Fatal" || level === "Error") return <AlertCircle size={17} />;
  if (level === "Warning") return <AlertTriangle size={17} />;
  if (level === "Debug" || level === "Verbose") return <Bug size={17} />;
  return <Info size={17} />;
}

function Highlight({ text, term }: { text: string; term: string }): ReactNode {
  if (!term.trim()) return text;
  const escaped = term.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  return text.split(new RegExp(`(${escaped})`, "ig")).map((part, index) =>
    part.toLowerCase() === term.toLowerCase() ? <mark key={index} className="rounded bg-yellow-300 px-0.5 text-zinc-900">{part}</mark> : part
  );
}

export default function LogsPage() {
  const { fetchWithAuth } = useApi();
  const [files, setFiles] = useState<LogFile[]>([]);
  const [selectedFile, setSelectedFile] = useState<string | null>(null);
  const [logContent, setLogContent] = useState<LogContent | null>(null);
  const [loading, setLoading] = useState(true);
  const [contentLoading, setContentLoading] = useState(false);
  const [tail, setTail] = useState(500);
  const [searchTerm, setSearchTerm] = useState("");
  const [level, setLevel] = useState("All");
  const [source, setSource] = useState("All");
  const [rawView, setRawView] = useState(false);
  const [expanded, setExpanded] = useState<Set<number>>(new Set());

  const loadFiles = useCallback(async () => {
    setLoading(true);
    try {
      const res = await fetchWithAuth("/api/logs");
      if (res.ok) { const data = await res.json(); setFiles(data.files); setSelectedFile((current) => current ?? data.files[0]?.fileName ?? null); }
    } finally { setLoading(false); }
  }, [fetchWithAuth]);

  const loadContent = useCallback(async (fileName: string) => {
    setContentLoading(true);
    try { const res = await fetchWithAuth(`/api/logs/${fileName}?tail=${tail}`); if (res.ok) setLogContent(await res.json()); }
    finally { setContentLoading(false); }
  }, [fetchWithAuth, tail]);

  useEffect(() => { void loadFiles(); }, [loadFiles]);
  useEffect(() => { if (selectedFile) void loadContent(selectedFile); }, [selectedFile, loadContent]);

  const events = useMemo(() => {
    if (!logContent) return [];
    const offset = logContent.totalLines - logContent.returnedLines;
    return logContent.content.split("\n").filter((line) => line.trim()).map((line, index) => parseEvent(line, offset + index + 1)).reverse();
  }, [logContent]);
  const rawLines = useMemo(() => {
    if (!logContent) return [];
    const offset = logContent.totalLines - logContent.returnedLines;
    return logContent.content.split("\n").map((text, index) => ({ number: offset + index + 1, text }));
  }, [logContent]);
  const sources = useMemo(() => [...new Set(events.map((event) => event.source).filter(Boolean) as string[])].sort(), [events]);
  const levels = useMemo(() => levelOrder.filter((name) => events.some((event) => event.level === name)), [events]);
  const filtered = useMemo(() => {
    const query = searchTerm.trim().toLocaleLowerCase("tr-TR");
    return events.filter((event) => (level === "All" || event.level === level) && (source === "All" || event.source === source) && (!query || `${event.message} ${event.exception ?? ""} ${event.source ?? ""} ${event.traceId ?? ""} ${JSON.stringify(event.properties)}`.toLocaleLowerCase("tr-TR").includes(query)));
  }, [events, level, source, searchTerm]);

  const formatSize = (bytes: number) => bytes < 1024 ? `${bytes} B` : bytes < 1048576 ? `${(bytes / 1024).toFixed(1)} KB` : `${(bytes / 1048576).toFixed(1)} MB`;
  const formatTime = (value?: string) => value ? new Intl.DateTimeFormat("tr-TR", { day: "2-digit", month: "short", hour: "2-digit", minute: "2-digit", second: "2-digit", fractionalSecondDigits: 3 }).format(new Date(value)) : "Zaman yok";
  const shortSource = (value?: string) => value?.split(".").slice(-2).join(".");
  const toggleExpanded = (line: number) => setExpanded((current) => {
    const next = new Set(current);
    if (next.has(line)) next.delete(line);
    else next.add(line);
    return next;
  });

  const handleDelete = async (fileName: string) => {
    if (!confirm(`"${fileName}" dosyası silinecek. Emin misiniz?`)) return;
    const res = await fetchWithAuth(`/api/logs/${fileName}`, { method: "DELETE" });
    if (res.ok) { toast.success("Log dosyası silindi"); setFiles((current) => current.filter((file) => file.fileName !== fileName)); if (selectedFile === fileName) { setSelectedFile(null); setLogContent(null); } }
    else { const data = await res.json(); toast.error(data.error || "Silinemedi"); }
  };
  const download = () => {
    if (!logContent) return;
    const url = URL.createObjectURL(new Blob([logContent.content], { type: "application/x-ndjson" }));
    const anchor = document.createElement("a"); anchor.href = url; anchor.download = logContent.fileName; anchor.click(); URL.revokeObjectURL(url);
  };

  return <div className="mx-auto max-w-[1500px] space-y-5">
    <div className="flex flex-wrap items-start justify-between gap-3">
      <div><h1 className="text-2xl font-bold text-zinc-900 dark:text-zinc-100">Sistem Logları</h1><p className="mt-1 text-sm text-zinc-500 dark:text-zinc-400">Olayları filtreleyin, hata ayrıntılarını inceleyin ve istekleri trace kimliğiyle takip edin.</p></div>
      <button onClick={() => void loadFiles()} disabled={loading} className="flex items-center gap-2 rounded-lg border border-zinc-200 px-3 py-2 text-sm hover:bg-zinc-50 dark:border-zinc-700 dark:hover:bg-zinc-800"><RefreshCw size={16} className={cn(loading && "animate-spin")} />Dosyaları yenile</button>
    </div>
    <div className="grid grid-cols-1 gap-5 xl:grid-cols-[250px_minmax(0,1fr)]">
      <aside className="space-y-2"><div className="flex items-center justify-between px-1"><h2 className="text-xs font-semibold uppercase tracking-wide text-zinc-500">Log dosyaları</h2><span className="text-xs text-zinc-400">{files.length}</span></div>
        <div className="max-h-[76vh] space-y-2 overflow-y-auto pr-1">
          {loading ? [1,2,3].map((i) => <div key={i} className="h-16 animate-pulse rounded-lg bg-zinc-100 dark:bg-zinc-800" />) : files.map((file) => <button key={file.fileName} onClick={() => setSelectedFile(file.fileName)} className={cn("group w-full rounded-xl border p-3 text-left transition", selectedFile === file.fileName ? "border-blue-500 bg-blue-50 dark:bg-blue-950/30" : "border-zinc-200 hover:bg-zinc-50 dark:border-zinc-800 dark:hover:bg-zinc-900")}> 
            <div className="flex items-center gap-2"><FileText size={14} className="shrink-0 text-zinc-400"/><span className="min-w-0 flex-1 truncate text-sm font-medium">{file.fileName}</span>{file.isToday && <span className="rounded bg-emerald-100 px-1.5 py-0.5 text-[10px] font-semibold text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300">CANLI</span>}</div>
            <div className="mt-2 flex items-center text-xs text-zinc-500"><HardDrive size={11} className="mr-1"/>{formatSize(file.sizeBytes)}<span className="ml-auto">{new Date(file.lastModifiedAt).toLocaleDateString("tr-TR")}</span>{file.canDelete && <span role="button" tabIndex={0} title="Sil" onClick={(event) => { event.stopPropagation(); void handleDelete(file.fileName); }} className="ml-2 rounded p-1 text-red-500 opacity-0 hover:bg-red-50 group-hover:opacity-100 dark:hover:bg-red-950"><Trash2 size={13}/></span>}</div>
          </button>)}
        </div>
      </aside>
      <main className="min-w-0">
        {!selectedFile || !logContent ? <div className="flex h-64 items-center justify-center rounded-xl border border-dashed border-zinc-300 text-sm text-zinc-500 dark:border-zinc-700"><FileText size={28} className="mr-3 opacity-50"/>Görüntülemek için bir log dosyası seçin</div> : <div className="space-y-3">
          <div className="rounded-xl border border-zinc-200 bg-white p-3 shadow-sm dark:border-zinc-800 dark:bg-zinc-900">
            <div className="flex flex-wrap items-center gap-2">
              <div className="relative min-w-[220px] flex-1"><Search size={15} className="absolute left-3 top-1/2 -translate-y-1/2 text-zinc-400"/><input value={searchTerm} onChange={(e) => setSearchTerm(e.target.value)} placeholder="Mesaj, hata, servis veya trace ara..." className="w-full rounded-lg border border-zinc-200 bg-transparent py-2 pl-9 pr-8 text-sm outline-none focus:border-blue-500 dark:border-zinc-700"/>{searchTerm && <button onClick={() => setSearchTerm("")} className="absolute right-2.5 top-1/2 -translate-y-1/2 text-zinc-400"><X size={14}/></button>}</div>
              <Filter size={14} className="ml-1 text-zinc-400"/><select value={level} onChange={(e) => setLevel(e.target.value)} className="rounded-lg border border-zinc-200 bg-white px-2.5 py-2 text-sm dark:border-zinc-700 dark:bg-zinc-900"><option value="All">Tüm seviyeler</option>{levels.map((item) => <option key={item}>{item}</option>)}</select>
              <select value={source} onChange={(e) => setSource(e.target.value)} className="max-w-[220px] rounded-lg border border-zinc-200 bg-white px-2.5 py-2 text-sm dark:border-zinc-700 dark:bg-zinc-900"><option value="All">Tüm servisler</option>{sources.map((item) => <option key={item} value={item}>{shortSource(item)}</option>)}</select>
              <select value={tail} onChange={(e) => setTail(Number(e.target.value))} className="rounded-lg border border-zinc-200 bg-white px-2.5 py-2 text-sm dark:border-zinc-700 dark:bg-zinc-900"><option value={100}>Son 100</option><option value={500}>Son 500</option><option value={1000}>Son 1000</option><option value={0}>Tümü</option></select>
              <button onClick={() => setRawView((value) => !value)} className={cn("flex items-center gap-1.5 rounded-lg border px-2.5 py-2 text-sm", rawView ? "border-blue-500 bg-blue-50 text-blue-700 dark:bg-blue-950" : "border-zinc-200 dark:border-zinc-700")}><FileJson size={14}/>Ham</button>
              <button onClick={download} title="İndir" className="rounded-lg border border-zinc-200 p-2 dark:border-zinc-700"><Download size={16}/></button>
              <button onClick={() => void loadContent(selectedFile)} title="Yenile" className="rounded-lg border border-zinc-200 p-2 dark:border-zinc-700"><RefreshCw size={16} className={cn(contentLoading && "animate-spin")}/></button>
            </div>
            <div className="mt-2 flex flex-wrap items-center gap-3 text-xs text-zinc-500"><span className="font-medium text-zinc-700 dark:text-zinc-300">{selectedFile}</span><span>{filtered.length} / {events.length} olay</span>{events.filter((event) => event.level === "Error" || event.level === "Fatal").length > 0 && <span className="text-red-600">{events.filter((event) => event.level === "Error" || event.level === "Fatal").length} hata</span>}<span className="ml-auto">Yeni olaylar üstte</span></div>
          </div>
          <div className="relative min-h-32">{contentLoading && <div className="absolute inset-0 z-20 flex items-center justify-center rounded-xl bg-white/60 dark:bg-zinc-950/60"><RefreshCw className="animate-spin text-blue-500"/></div>}
            {rawView ? <div className="max-h-[70vh] overflow-auto rounded-xl border border-zinc-800 bg-zinc-950 font-mono text-xs leading-5 text-zinc-200"><table className="w-full border-collapse"><tbody>{rawLines.map((line) => <tr key={line.number} className="group align-top hover:bg-zinc-900"><td className="sticky left-0 w-px select-none whitespace-nowrap border-r border-zinc-800 bg-zinc-950 px-3 py-0.5 text-right text-zinc-500 group-hover:bg-zinc-900">{line.number}</td><td className="whitespace-pre-wrap break-all px-3 py-0.5"><Highlight text={line.text || "\u00a0"} term={searchTerm}/></td></tr>)}</tbody></table></div> : <div className="max-h-[70vh] space-y-2 overflow-y-auto pr-1">
              {filtered.length === 0 ? <div className="rounded-xl border border-dashed border-zinc-300 p-12 text-center text-sm text-zinc-500 dark:border-zinc-700">Filtrelerle eşleşen olay bulunamadı.</div> : filtered.map((event) => { const open = expanded.has(event.line); const hasDetails = Boolean(event.exception || event.traceId || Object.keys(event.properties).length); return <article key={event.line} className={cn("overflow-hidden rounded-xl border bg-white dark:bg-zinc-900", event.level === "Error" || event.level === "Fatal" ? "border-red-200 dark:border-red-900/70" : event.level === "Warning" ? "border-amber-200 dark:border-amber-900/60" : "border-zinc-200 dark:border-zinc-800")}> 
                <button onClick={() => hasDetails && toggleExpanded(event.line)} className={cn("flex w-full items-start gap-3 p-3 text-left", hasDetails && "hover:bg-zinc-50 dark:hover:bg-zinc-800/50")}>
                  <span className={cn("mt-0.5 rounded-lg p-2", levelStyle[event.level])}><LevelIcon level={event.level}/></span>
                  <span className="min-w-0 flex-1"><span className="flex flex-wrap items-center gap-x-2 gap-y-1"><span className={cn("rounded px-1.5 py-0.5 text-[10px] font-bold uppercase", levelStyle[event.level])}>{event.level}</span><span className="text-xs text-zinc-500"><Clock size={11} className="mr-1 inline"/>{formatTime(event.timestamp)}</span>{event.source && <span title={event.source} className="truncate rounded bg-zinc-100 px-1.5 py-0.5 font-mono text-[10px] text-zinc-600 dark:bg-zinc-800 dark:text-zinc-400">{shortSource(event.source)}</span>}<span className="text-[10px] text-zinc-400">#{event.line}</span></span><span className="mt-1.5 block break-words text-sm font-medium leading-5 text-zinc-900 dark:text-zinc-100"><Highlight text={event.message} term={searchTerm}/></span>{event.exception && !open && <span className="mt-1 block truncate text-xs text-red-600 dark:text-red-400">{event.exception.split("\n")[0]}</span>}</span>
                  {hasDetails && (open ? <ChevronDown size={17} className="mt-1 shrink-0 text-zinc-400"/> : <ChevronRight size={17} className="mt-1 shrink-0 text-zinc-400"/>)}
                </button>
                {open && <div className="space-y-3 border-t border-zinc-200 bg-zinc-50/70 px-4 py-3 text-xs dark:border-zinc-800 dark:bg-zinc-950/40">{event.exception && <section><h3 className="mb-1.5 font-semibold text-red-700 dark:text-red-300">Hata ve stack trace</h3><pre className="max-h-72 overflow-auto whitespace-pre-wrap break-words rounded-lg bg-zinc-950 p-3 leading-5 text-zinc-200"><Highlight text={event.exception} term={searchTerm}/></pre></section>}<div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-3">{event.traceId && <div className="rounded-lg border border-zinc-200 bg-white p-2 dark:border-zinc-800 dark:bg-zinc-900"><span className="block text-[10px] uppercase text-zinc-400">Trace ID</span><code className="break-all">{event.traceId}</code></div>}{Object.entries(event.properties).map(([key, value]) => <div key={key} className="rounded-lg border border-zinc-200 bg-white p-2 dark:border-zinc-800 dark:bg-zinc-900"><span className="block text-[10px] uppercase text-zinc-400">{key}</span><span className="break-all text-zinc-700 dark:text-zinc-300">{String(value)}</span></div>)}</div>{event.template && event.template !== event.message && <div><span className="text-zinc-400">Mesaj şablonu: </span><code>{event.template}</code></div>}</div>}
              </article>; })}
            </div>}
          </div>
        </div>}
      </main>
    </div>
  </div>;
}
