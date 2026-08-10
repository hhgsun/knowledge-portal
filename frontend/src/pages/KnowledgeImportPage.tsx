import { useMemo, useState } from "react";
import { FileText, Paperclip, Upload, WandSparkles, X } from "lucide-react";
import { toast } from "sonner";
import TiptapEditor from "../components/editor/tiptap-editor";
import { useApi } from "../hooks/useApi";
import { useLookups } from "../hooks/useLookups";

type Draft = {
  sourceIndex: number; fileName: string; title: string; excerpt?: string; content: Record<string, unknown>;
  parsed: boolean; keepOriginal: boolean; processingMode: string; warning?: string;
  contentType: string; status: string; tags: string[];
};

export default function KnowledgeImportPage() {
  const { fetchWithAuth } = useApi();
  const { contentTypes } = useLookups();
  const [files, setFiles] = useState<File[]>([]);
  const [drafts, setDrafts] = useState<Draft[]>([]);
  const [selected, setSelected] = useState(0);
  const [busy, setBusy] = useState(false);
  const [bulkTags, setBulkTags] = useState("");
  const current = drafts[selected];
  const accepted = useMemo(() => ".txt,.md,.markdown,.csv,.tsv,.json,.yaml,.yml,.xlsx,.xls,.pdf,.docx,.pptx,.png,.jpg,.jpeg,.webp,.gif,.svg", []);

  const analyze = async () => {
    if (!files.length) return;
    setBusy(true);
    try {
      const body = new FormData(); files.forEach(file => body.append("files", file));
      const response = await fetchWithAuth("/api/source-imports/analyze", { method: "POST", body, noRetry: true });
      const data = await response.json();
      if (!response.ok) throw new Error(data.error || "Sources could not be analyzed");
      setDrafts(data.drafts.map((d: Draft) => ({ ...d, contentType: "reference", status: "draft", tags: [] })));
      setSelected(0); toast.success(`${data.drafts.length} source analyzed`);
    } catch (error) { toast.error(error instanceof Error ? error.message : "Analysis failed"); }
    finally { setBusy(false); }
  };

  const update = (changes: Partial<Draft>) => setDrafts(items => items.map((item, i) => i === selected ? { ...item, ...changes } : item));
  const applyBulk = () => {
    const tags = bulkTags.split(",").map(x => x.trim()).filter(Boolean);
    setDrafts(items => items.map(item => ({ ...item, tags: [...new Set([...item.tags, ...tags])] })));
    toast.success("Tags applied to all drafts");
  };
  const commit = async () => {
    setBusy(true);
    try {
      const body = new FormData(); files.forEach(file => body.append("files", file));
      body.append("manifest", JSON.stringify({ drafts: drafts.map(({ sourceIndex, title, content, excerpt, contentType, status, tags, keepOriginal }) => ({ sourceIndex, title, content, excerpt, contentType, status, tags, keepOriginal })) }));
      const response = await fetchWithAuth("/api/source-imports/commit", { method: "POST", body, noRetry: true });
      const data = await response.json();
      if (!response.ok) throw new Error(data.error || "Import failed");
      if (data.failed) toast.error(`${data.created} article imported, ${data.failed} failed`);
      else toast.success(`${data.created} article imported successfully`);
    } catch (error) { toast.error(error instanceof Error ? error.message : "Import failed"); }
    finally { setBusy(false); }
  };

  if (!drafts.length) return <div className="max-w-5xl mx-auto space-y-6">
    <div><h1 className="text-2xl font-bold">Bulk Knowledge Import</h1><p className="text-sm text-zinc-500 mt-1">Convert source files into editable articles and retain the originals as attachments.</p></div>
    <label className="block rounded-xl border-2 border-dashed border-zinc-300 dark:border-zinc-700 p-12 text-center cursor-pointer hover:border-blue-500">
      <Upload className="mx-auto mb-3 text-blue-600" size={34}/><strong>Select source files</strong><p className="text-sm text-zinc-500 mt-2">TXT, Markdown, CSV, Excel, PDF and Office files are parsed. Other supported files remain attachments.</p>
      <input multiple type="file" accept={accepted} className="hidden" onChange={e => setFiles(Array.from(e.target.files ?? []))}/>
    </label>
    {files.length > 0 && <div className="border rounded-xl divide-y dark:border-zinc-800">{files.map((file, i) => <div key={`${file.name}-${i}`} className="p-3 flex items-center gap-3"><FileText size={17}/><span className="flex-1 text-sm">{file.name}</span><span className="text-xs text-zinc-500">{(file.size / 1024).toFixed(1)} KB</span><button onClick={() => setFiles(x => x.filter((_, n) => n !== i))}><X size={16}/></button></div>)}</div>}
    <button disabled={!files.length || busy} onClick={analyze} className="px-5 py-2.5 rounded-lg bg-blue-600 text-white disabled:opacity-50 flex items-center gap-2"><WandSparkles size={17}/>{busy ? "Analyzing…" : "Analyze sources"}</button>
  </div>;

  return <div className="max-w-7xl mx-auto space-y-5">
    <div className="flex items-center justify-between"><div><h1 className="text-2xl font-bold">Review import drafts</h1><p className="text-sm text-zinc-500">Edit content and metadata before importing.</p></div><button disabled={busy} onClick={commit} className="px-5 py-2.5 rounded-lg bg-blue-600 text-white disabled:opacity-50">{busy ? "Importing…" : `Import ${drafts.length} articles`}</button></div>
    <div className="p-3 border rounded-xl flex gap-2 dark:border-zinc-800"><input value={bulkTags} onChange={e => setBulkTags(e.target.value)} placeholder="Tags for all drafts, comma separated" className="flex-1 px-3 py-2 bg-transparent border rounded-lg dark:border-zinc-700"/><button onClick={applyBulk} className="px-4 py-2 border rounded-lg">Apply to all</button></div>
    <div className="grid lg:grid-cols-[280px_1fr] gap-5">
      <aside className="border rounded-xl divide-y dark:border-zinc-800 h-fit">{drafts.map((draft, i) => <button key={`${draft.fileName}-${i}`} onClick={() => setSelected(i)} className={`w-full text-left p-3 ${selected === i ? "bg-blue-50 dark:bg-blue-950/30" : ""}`}><p className="font-medium text-sm truncate">{draft.title}</p><p className="text-xs text-zinc-500 truncate">{draft.fileName}</p>{draft.warning && <p className="text-xs text-amber-600 mt-1">Attachment only</p>}</button>)}</aside>
      {current && <section className="space-y-4">
        {current.warning && <div className="p-3 rounded-lg bg-amber-50 text-amber-800 dark:bg-amber-950/30 dark:text-amber-300 text-sm">{current.warning}</div>}
        <input value={current.title} onChange={e => update({ title: e.target.value })} className="w-full text-xl font-semibold px-3 py-2 border rounded-lg bg-transparent dark:border-zinc-700"/>
        <div className="grid sm:grid-cols-2 gap-3"><select value={current.contentType} onChange={e => update({ contentType: e.target.value })} className="px-3 py-2 border rounded-lg bg-white dark:bg-zinc-900 dark:border-zinc-700">{contentTypes.map(x => <option key={x.value} value={x.value}>{x.label}</option>)}</select><select value={current.status} onChange={e => update({ status: e.target.value })} className="px-3 py-2 border rounded-lg bg-white dark:bg-zinc-900 dark:border-zinc-700"><option value="draft">Draft</option><option value="pending">Pending</option><option value="published">Published</option></select></div>
        <input value={current.tags.join(", ")} onChange={e => update({ tags: e.target.value.split(",").map(x => x.trim()).filter(Boolean) })} placeholder="Tags, comma separated" className="w-full px-3 py-2 border rounded-lg bg-transparent dark:border-zinc-700"/>
        <textarea value={current.excerpt ?? ""} onChange={e => update({ excerpt: e.target.value })} placeholder="Excerpt" className="w-full px-3 py-2 border rounded-lg bg-transparent dark:border-zinc-700" rows={2}/>
        {current.parsed && <TiptapEditor content={current.content} onChange={content => update({ content })}/>} 
        <label className="flex items-center gap-2 p-3 border rounded-lg dark:border-zinc-800"><input type="checkbox" checked={current.keepOriginal} onChange={e => update({ keepOriginal: e.target.checked })}/><Paperclip size={17}/><span className="text-sm">Keep original <strong>{current.fileName}</strong> as an attachment</span></label>
      </section>}
    </div>
  </div>;
}
