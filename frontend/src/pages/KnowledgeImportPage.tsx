import { lazy, Suspense, useMemo, useState } from "react";
import { ArrowLeft, Check, ChevronRight, FileText, Paperclip, Upload, WandSparkles, X } from "lucide-react";
import { Link, useNavigate } from "react-router-dom";
import { toast } from "sonner";
import { TagSelector } from "../components/editor/tag-selector";
import { useApi } from "../hooks/useApi";
import { useLookups } from "../hooks/useLookups";

const MilkdownEditor = lazy(() => import("../components/editor/milkdown-editor"));

type Draft = {
  sourceIndex: number; fileName: string; title: string; excerpt?: string; contentMarkdown: string;
  parsed: boolean; keepOriginal: boolean; processingMode: string; warning?: string;
  contentType: string; status: string; tags: string[];
};

export default function KnowledgeImportPage() {
  const navigate = useNavigate();
  const { fetchWithAuth } = useApi();
  const { contentTypes } = useLookups();
  const [files, setFiles] = useState<File[]>([]);
  const [drafts, setDrafts] = useState<Draft[]>([]);
  const [selected, setSelected] = useState(0);
  const [busy, setBusy] = useState(false);
  const [bulkTags, setBulkTags] = useState<string[]>([]);
  const [error, setError] = useState("");
  const current = drafts[selected];
  const accepted = useMemo(() => ".txt,.md,.markdown,.csv,.tsv,.json,.yaml,.yml,.xlsx,.pdf,.docx,.pptx,.png,.jpg,.jpeg,.webp,.gif,.svg", []);

  const analyze = async () => {
    if (!files.length) return;
    setBusy(true); setError("");
    try {
      const body = new FormData(); files.forEach(file => body.append("files", file));
      const response = await fetchWithAuth("/api/source-imports/analyze", { method: "POST", body, noRetry: true });
      const data = await response.json();
      if (!response.ok) throw new Error(data.error || "Sources could not be analyzed");
      setDrafts(data.drafts.map((draft: Draft) => ({ ...draft, contentType: "reference", status: "draft", tags: [] })));
      setSelected(0); toast.success(`${data.drafts.length} source analyzed`);
    } catch (cause) { setError(cause instanceof Error ? cause.message : "Analysis failed"); }
    finally { setBusy(false); }
  };

  const update = (changes: Partial<Draft>) => setDrafts(items => items.map((item, index) => index === selected ? { ...item, ...changes } : item));
  const applyBulk = () => {
    if (!bulkTags.length) return;
    setDrafts(items => items.map(item => ({ ...item, tags: [...new Set([...item.tags, ...bulkTags])] })));
    toast.success("Tags applied to all drafts");
  };
  const commit = async () => {
    const invalidDraft = drafts.find(draft => !draft.title.trim());
    if (invalidDraft) { setSelected(drafts.indexOf(invalidDraft)); setError("Title is required"); return; }
    setBusy(true); setError("");
    try {
      const body = new FormData(); files.forEach(file => body.append("files", file));
      body.append("manifest", JSON.stringify({ drafts: drafts.map(({ sourceIndex, title, contentMarkdown, excerpt, contentType, status, tags, keepOriginal }) => ({ sourceIndex, title: title.trim(), contentMarkdown, excerpt: excerpt?.trim() || undefined, contentType, status, tags, keepOriginal })) }));
      const response = await fetchWithAuth("/api/source-imports/commit", { method: "POST", body, noRetry: true });
      const data = await response.json();
      if (!response.ok) throw new Error(data.error || "Import failed");
      if (data.failed) { setError(`${data.created} article imported, ${data.failed} failed`); toast.error("Some articles could not be imported"); }
      else { toast.success(`${data.created} article imported successfully`); navigate("/articles"); }
    } catch (cause) { setError(cause instanceof Error ? cause.message : "Import failed"); }
    finally { setBusy(false); }
  };

  if (!drafts.length) return <div className="max-w-5xl mx-auto">
    <div className="flex items-center gap-3 mb-6">
      <Link to="/articles" className="p-2 rounded-lg hover:bg-zinc-100 dark:hover:bg-zinc-800"><ArrowLeft size={18}/></Link>
      <div><h1 className="text-xl font-bold text-zinc-900 dark:text-zinc-100">Bulk Knowledge Import</h1><p className="text-sm text-zinc-500 mt-0.5">Create editable articles from multiple source files.</p></div>
    </div>
    {error && <div className="mb-4 p-3 bg-red-50 dark:bg-red-950 border border-red-200 dark:border-red-800 rounded-lg text-sm text-red-600 dark:text-red-400">{error}</div>}
    <div className="space-y-4">
      <label className="block rounded-xl border-2 border-dashed border-zinc-300 dark:border-zinc-700 p-10 text-center cursor-pointer hover:border-blue-500 hover:bg-blue-50/40 dark:hover:bg-blue-950/10 transition-colors">
        <Upload className="mx-auto mb-3 text-blue-600" size={32}/><strong className="text-zinc-900 dark:text-zinc-100">Select source files</strong><p className="text-sm text-zinc-500 mt-2">TXT, Markdown, CSV, Excel, PDF and Office files are parsed. Other supported files remain attachments.</p>
        <input multiple type="file" accept={accepted} className="hidden" onChange={event => setFiles(Array.from(event.target.files ?? []))}/>
      </label>
      {files.length > 0 && <div className="border border-zinc-200 dark:border-zinc-800 rounded-xl divide-y divide-zinc-200 dark:divide-zinc-800">{files.map((file, index) => <div key={`${file.name}-${index}`} className="p-3 flex items-center gap-3"><FileText size={17} className="text-zinc-500"/><span className="flex-1 text-sm truncate">{file.name}</span><span className="text-xs text-zinc-500">{(file.size / 1024).toFixed(1)} KB</span><button type="button" aria-label={`Remove ${file.name}`} onClick={() => setFiles(items => items.filter((_, itemIndex) => itemIndex !== index))} className="p-1 rounded hover:bg-zinc-100 dark:hover:bg-zinc-800"><X size={16}/></button></div>)}</div>}
      <div className="flex justify-end"><button disabled={!files.length || busy} onClick={analyze} className="flex items-center gap-2 px-4 py-2 bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white text-sm font-medium rounded-lg transition-colors"><WandSparkles size={16}/>{busy ? "Analyzing..." : "Analyze Sources"}</button></div>
    </div>
  </div>;

  return <div className="max-w-7xl mx-auto">
    <div className="flex flex-wrap items-center justify-between gap-3 mb-6">
      <div className="flex items-center gap-3"><button type="button" onClick={() => setDrafts([])} className="p-2 rounded-lg hover:bg-zinc-100 dark:hover:bg-zinc-800"><ArrowLeft size={18}/></button><div><h1 className="text-xl font-bold text-zinc-900 dark:text-zinc-100">Review Import Drafts</h1><p className="text-sm text-zinc-500 mt-0.5">Review each article before importing.</p></div></div>
      <button disabled={busy} onClick={commit} className="flex items-center gap-2 px-4 py-2 bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white text-sm font-medium rounded-lg transition-colors"><Check size={16}/>{busy ? "Importing..." : `Import ${drafts.length} Articles`}</button>
    </div>
    {error && <div className="mb-4 p-3 bg-red-50 dark:bg-red-950 border border-red-200 dark:border-red-800 rounded-lg text-sm text-red-600 dark:text-red-400">{error}</div>}
    <div className="mb-5 p-4 border border-zinc-200 dark:border-zinc-800 rounded-xl"><div className="flex flex-wrap items-end gap-3"><div className="min-w-56 flex-1"><label className="text-xs font-medium text-zinc-500 mb-1.5 block">Tags for all drafts</label><TagSelector selectedTags={bulkTags} onChange={setBulkTags}/></div><button type="button" onClick={applyBulk} disabled={!bulkTags.length} className="px-4 py-2 text-sm font-medium border border-zinc-300 dark:border-zinc-700 rounded-lg disabled:opacity-50 hover:bg-zinc-50 dark:hover:bg-zinc-800">Apply to All</button></div></div>
    <div className="grid lg:grid-cols-[260px_minmax(0,1fr)] gap-6">
      <aside className="border border-zinc-200 dark:border-zinc-800 rounded-xl divide-y divide-zinc-200 dark:divide-zinc-800 h-fit overflow-hidden">{drafts.map((draft, index) => <button key={`${draft.fileName}-${index}`} onClick={() => { setSelected(index); setError(""); }} className={`w-full flex items-center gap-2 text-left p-3 transition-colors ${selected === index ? "bg-blue-50 dark:bg-blue-950/30" : "hover:bg-zinc-50 dark:hover:bg-zinc-900"}`}><div className="min-w-0 flex-1"><p className="font-medium text-sm truncate">{draft.title || "Untitled article"}</p><p className="text-xs text-zinc-500 truncate">{draft.fileName}</p>{draft.warning && <p className="text-xs text-amber-600 mt-1">Attachment only</p>}</div><ChevronRight size={15} className="shrink-0 text-zinc-400"/></button>)}</aside>
      {current && <section className="min-w-0 space-y-4">
        {current.warning && <div className="p-3 rounded-lg bg-amber-50 text-amber-800 dark:bg-amber-950/30 dark:text-amber-300 text-sm">{current.warning}</div>}
        <input value={current.title} onChange={event => update({ title: event.target.value })} placeholder="Makale başlığı..." className="w-full text-2xl font-bold bg-transparent border-none outline-none placeholder:text-zinc-300 dark:placeholder:text-zinc-600"/>
        <input value={current.excerpt ?? ""} onChange={event => update({ excerpt: event.target.value })} placeholder="Kısa açıklama (isteğe bağlı)..." className="w-full text-sm bg-transparent border-none outline-none placeholder:text-zinc-400 text-zinc-600 dark:text-zinc-400"/>
        <div className="flex flex-wrap gap-3 pb-4 border-b border-zinc-200 dark:border-zinc-800"><select value={current.contentType} onChange={event => update({ contentType: event.target.value })} className="px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800">{contentTypes.map(type => <option key={type.value} value={type.value}>{type.label}</option>)}</select><select value={current.status} onChange={event => update({ status: event.target.value })} className="px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800"><option value="draft">Taslak</option><option value="published">Yayımlandı</option></select></div>
        <div className="pb-4 border-b border-zinc-200 dark:border-zinc-800"><label className="text-xs font-medium text-zinc-500 mb-1.5 block">Tags</label><TagSelector selectedTags={current.tags} onChange={tags => update({ tags })}/></div>
        {current.parsed && <Suspense fallback={<div className="h-64 bg-zinc-50 dark:bg-zinc-900 rounded-lg animate-pulse"/>}><MilkdownEditor key={current.sourceIndex} contentMarkdown={current.contentMarkdown} onChange={contentMarkdown => update({ contentMarkdown })}/></Suspense>}
        <label className="flex items-center gap-2 p-3 border border-zinc-200 dark:border-zinc-800 rounded-lg"><input type="checkbox" checked={current.keepOriginal} onChange={event => update({ keepOriginal: event.target.checked })}/><Paperclip size={17}/><span className="text-sm">Keep original <strong>{current.fileName}</strong> as an attachment</span></label>
      </section>}
    </div>
  </div>;
}
