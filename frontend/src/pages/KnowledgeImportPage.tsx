import { lazy, Suspense, useRef, useState } from "react";
import { AlertTriangle, ArrowLeft, Check, ChevronRight, FileText, Paperclip, Tag, Upload, WandSparkles, X } from "lucide-react";
import { Link, useNavigate } from "react-router-dom";
import { toast } from "sonner";
import { ContentTypeSelect } from "../components/editor/content-type-select";
import { TagSelector } from "../components/editor/tag-selector";
import { useApi } from "../hooks/useApi";
import { useAutoResizeTextArea } from "../hooks/useAutoResizeTextArea";
import { useLookups } from "../hooks/useLookups";
import { PendingFileList } from "../components/attachments/file-upload-zone";

const MilkdownEditor = lazy(() => import("../components/editor/milkdown-editor"));

const ACCEPTED_SOURCE_FILES = ".txt,.md,.markdown,.csv,.tsv,.json,.yaml,.yml,.xlsx,.pdf,.docx,.pptx,.png,.jpg,.jpeg,.webp,.gif,.svg";
const ACCEPTED_SOURCE_EXTENSIONS = new Set(ACCEPTED_SOURCE_FILES.split(","));

const STATUS_DESCRIPTIONS: Record<string, string> = {
  draft: "Henüz yayımlanmadı",
  published: "Okuyuculara açık",
};

type Draft = {
  sourceIndex: number; fileName: string; title: string; excerpt?: string; contentMarkdown: string;
  parsed: boolean; keepOriginal: boolean; processingMode: string; warning?: string;
  analysisError?: string;
  contentType: string; status: string; tags: string[];
};

type ImportIssue = {
  sourceIndex: number;
  fileName: string;
  reason: string;
  severity: "warning" | "error";
};

type AnalyzeResponse = {
  drafts?: Draft[];
  error?: string;
  detail?: string;
  title?: string;
  errors?: Record<string, string[]>;
};
type CommitResponse = {
  created?: number;
  failed?: number;
  error?: string;
  items?: Array<{ sourceIndex: number; fileName?: string; title: string; error?: string | null }>;
};

function getResponseError(data: AnalyzeResponse, status: number) {
  const validationError = Object.values(data.errors ?? {}).flat().find(Boolean);
  return data.error || data.detail || validationError || data.title || `The source could not be analyzed (HTTP ${status}).`;
}

function failedDraft(file: File, sourceIndex: number, reason: string): Draft {
  const title = file.name.replace(/\.[^.]+$/, "").replace(/[_-]+/g, " ").trim() || file.name;
  return {
    sourceIndex,
    fileName: file.name,
    title,
    contentMarkdown: "",
    parsed: false,
    keepOriginal: true,
    processingMode: "failed",
    analysisError: reason,
    contentType: "reference",
    status: "draft",
    tags: [],
  };
}

function issueMessage(items: ImportIssue[]) {
  const errorCount = items.filter(issue => issue.severity === "error").length;
  if (errorCount) return `${errorCount} source file${errorCount === 1 ? "" : "s"} need manual content. Add content or remove the failed article${errorCount === 1 ? "" : "s"} to continue.`;
  return items.length ? `${items.length} source file${items.length === 1 ? "" : "s"} need attention.` : "";
}

function ImportFeedback({ message, issues }: { message: string; issues: ImportIssue[] }) {
  if (!message && !issues.length) return null;
  const hasError = issues.some(issue => issue.severity === "error") || (Boolean(message) && !issues.length);
  const colors = hasError
    ? "border-red-200 bg-red-50 text-red-700 dark:border-red-800 dark:bg-red-950 dark:text-red-300"
    : "border-amber-200 bg-amber-50 text-amber-800 dark:border-amber-800 dark:bg-amber-950/30 dark:text-amber-300";

  return <div role="alert" className={`mb-4 rounded-lg border p-3 text-sm ${colors}`}>
    {message && <p className="font-medium">{message}</p>}
    {issues.length > 0 && <ul className={`${message ? "mt-2" : ""} space-y-1.5`}>
      {issues.map((issue, index) => <li key={`${issue.sourceIndex}-${issue.fileName}-${index}`} className="flex items-start gap-2">
        <AlertTriangle size={15} className="mt-0.5 shrink-0"/>
        <span><strong className="break-all">{issue.fileName}</strong>: {issue.reason}</span>
      </li>)}
    </ul>}
  </div>;
}

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
  const [issues, setIssues] = useState<ImportIssue[]>([]);
  const [dragOver, setDragOver] = useState(false);
  const [additionalAttachments, setAdditionalAttachments] = useState<Record<number, File[]>>({});
  const fileInputRef = useRef<HTMLInputElement>(null);
  const current = drafts[selected];
  const currentAdditionalAttachments = current ? additionalAttachments[current.sourceIndex] ?? [] : [];
  const currentAnalysisResolved = Boolean(current?.analysisError && current.contentMarkdown.trim());
  const hasBlockingAnalysisErrors = drafts.some(draft => Boolean(draft.analysisError && !draft.contentMarkdown.trim()));
  const titleRef = useAutoResizeTextArea(current?.title ?? "");
  const excerptRef = useAutoResizeTextArea(current?.excerpt ?? "");

  const addSourceFiles = (incomingFiles: File[]) => {
    const supportedFiles = incomingFiles.filter(file => {
      const extensionStart = file.name.lastIndexOf(".");
      return extensionStart >= 0 && ACCEPTED_SOURCE_EXTENSIONS.has(file.name.slice(extensionStart).toLowerCase());
    });
    const unsupportedCount = incomingFiles.length - supportedFiles.length;
    if (unsupportedCount) toast.error(`${unsupportedCount} unsupported file${unsupportedCount === 1 ? " was" : "s were"} skipped`);
    if (!supportedFiles.length) return;

    setFiles(currentFiles => {
      const knownFiles = new Set(currentFiles.map(file => `${file.name}\u0000${file.size}\u0000${file.lastModified}`));
      const uniqueFiles = supportedFiles.filter(file => {
        const key = `${file.name}\u0000${file.size}\u0000${file.lastModified}`;
        if (knownFiles.has(key)) return false;
        knownFiles.add(key);
        return true;
      });
      return [...currentFiles, ...uniqueFiles];
    });
    setError("");
    setIssues([]);
    setAdditionalAttachments({});
  };

  const analyze = async () => {
    if (!files.length) return;
    setBusy(true); setError(""); setIssues([]); setAdditionalAttachments({});
    try {
      const analyzedDrafts: Draft[] = [];
      for (const [sourceIndex, file] of files.entries()) {
        try {
          const body = new FormData(); body.append("files", file);
          const response = await fetchWithAuth("/api/source-imports/analyze", { method: "POST", body, noRetry: true });
          const data = await response.json().catch(() => ({})) as AnalyzeResponse;
          if (!response.ok) {
            analyzedDrafts.push(failedDraft(file, sourceIndex, getResponseError(data, response.status)));
            continue;
          }
          const draft = data.drafts?.[0];
          analyzedDrafts.push(draft
            ? { ...draft, sourceIndex, fileName: file.name, contentType: "reference", status: "draft", tags: [] }
            : failedDraft(file, sourceIndex, "The server returned no analysis result for this file."));
        } catch (cause) {
          analyzedDrafts.push(failedDraft(file, sourceIndex, cause instanceof Error ? cause.message : "The source could not be analyzed."));
        }
      }
      const analysisIssues = analyzedDrafts
        .filter(draft => Boolean(draft.analysisError || draft.warning))
        .map(draft => ({
          sourceIndex: draft.sourceIndex,
          fileName: draft.fileName,
          reason: draft.analysisError || draft.warning!,
          severity: draft.analysisError ? "error" as const : "warning" as const,
        }));
      const failedCount = analysisIssues.filter(issue => issue.severity === "error").length;
      setDrafts(analyzedDrafts);
      setIssues(analysisIssues);
      setError(issueMessage(analysisIssues));
      setSelected(analysisIssues.length ? analyzedDrafts.findIndex(draft => draft.sourceIndex === analysisIssues[0].sourceIndex) : 0);
      if (failedCount) toast.error(`${failedCount} source file${failedCount === 1 ? "" : "s"} could not be analyzed`);
      else if (analysisIssues.length) toast.warning(`${analysisIssues.length} source file${analysisIssues.length === 1 ? "" : "s"} could not be converted`);
      else toast.success(`${analyzedDrafts.length} source${analyzedDrafts.length === 1 ? "" : "s"} analyzed`);
    } catch (cause) { setError(cause instanceof Error ? cause.message : "Analysis failed"); setIssues([]); }
    finally { setBusy(false); }
  };

  const update = (changes: Partial<Draft>) => {
    const nextDrafts = drafts.map((item, index) => index === selected ? { ...item, ...changes } : item);
    setDrafts(nextDrafts);
    if (changes.contentMarkdown !== undefined && current?.analysisError) {
      const hasManualContent = Boolean(changes.contentMarkdown.trim());
      const nextIssues = issues.map(issue => issue.sourceIndex === current.sourceIndex
        ? {
            ...issue,
            severity: hasManualContent ? "warning" as const : "error" as const,
            reason: hasManualContent
              ? `Manual content provided; the original analysis failed: ${current.analysisError}`
              : current.analysisError!,
          }
        : issue);
      setIssues(nextIssues);
      setError(issueMessage(nextIssues));
    }
  };
  const removeDraft = (draftIndex: number) => {
    const removedSourceIndex = drafts[draftIndex].sourceIndex;
    const remainingDrafts = drafts
      .filter((_, index) => index !== draftIndex)
      .map(draft => draft.sourceIndex > removedSourceIndex ? { ...draft, sourceIndex: draft.sourceIndex - 1 } : draft);
    const remainingIssues = issues
      .filter(issue => issue.sourceIndex !== removedSourceIndex)
      .map(issue => issue.sourceIndex > removedSourceIndex ? { ...issue, sourceIndex: issue.sourceIndex - 1 } : issue);
    setFiles(items => items.filter((_, index) => index !== removedSourceIndex));
    setAdditionalAttachments(items => {
      const next: Record<number, File[]> = {};
      for (const [sourceIndex, attachments] of Object.entries(items)) {
        const numericIndex = Number(sourceIndex);
        if (numericIndex === removedSourceIndex) continue;
        next[numericIndex > removedSourceIndex ? numericIndex - 1 : numericIndex] = attachments;
      }
      return next;
    });
    setDrafts(remainingDrafts);
    setIssues(remainingIssues);
    setError(issueMessage(remainingIssues));
    setSelected(currentIndex => currentIndex > draftIndex
      ? currentIndex - 1
      : Math.min(currentIndex, Math.max(remainingDrafts.length - 1, 0)));
  };
  const applyBulk = () => {
    if (!bulkTags.length) return;
    setDrafts(items => items.map(item => ({ ...item, tags: [...new Set([...item.tags, ...bulkTags])] })));
    toast.success("Tags applied to all drafts");
  };
  const commit = async () => {
    if (hasBlockingAnalysisErrors) {
      setError(issueMessage(issues));
      return;
    }
    const invalidDraft = drafts.find(draft => !draft.title.trim());
    if (invalidDraft) {
      setSelected(drafts.indexOf(invalidDraft));
      setError("One source cannot be imported until the error below is fixed.");
      setIssues([{ sourceIndex: invalidDraft.sourceIndex, fileName: invalidDraft.fileName, reason: "Article title is required.", severity: "error" }]);
      return;
    }
    setBusy(true); setError(""); setIssues([]);
    try {
      const body = new FormData(); files.forEach(file => body.append("files", file));
      const attachmentFiles: File[] = [];
      const manifestDrafts = drafts.map(({ sourceIndex, title, contentMarkdown, excerpt, contentType, status, tags, keepOriginal }) => {
        const additionalAttachmentIndexes = (additionalAttachments[sourceIndex] ?? []).map(file => {
          const attachmentIndex = attachmentFiles.length;
          attachmentFiles.push(file);
          return attachmentIndex;
        });
        return { sourceIndex, title: title.trim(), contentMarkdown, excerpt: excerpt?.trim() || undefined, contentType, status, tags, keepOriginal, additionalAttachmentIndexes };
      });
      attachmentFiles.forEach(file => body.append("attachments", file));
      body.append("manifest", JSON.stringify({ drafts: manifestDrafts }));
      const response = await fetchWithAuth("/api/source-imports/commit", { method: "POST", body, noRetry: true });
      const data = await response.json().catch(() => ({})) as CommitResponse;
      if (!response.ok) throw new Error(data.error || "Import failed");
      if (data.failed) {
        const commitIssues = (data.items ?? []).filter(item => Boolean(item.error)).map(item => ({
          sourceIndex: item.sourceIndex,
          fileName: item.fileName || drafts.find(draft => draft.sourceIndex === item.sourceIndex)?.fileName || `Source #${item.sourceIndex + 1}`,
          reason: item.error || "The article could not be imported.",
          severity: "error" as const,
        }));
        setError(`${data.created ?? 0} article${data.created === 1 ? "" : "s"} imported; ${data.failed} failed. Successfully imported articles were kept; only failed drafts remain for correction and retry.`);
        setIssues(commitIssues);
        if (commitIssues.length) {
          const failedSourceIndexes = new Set(commitIssues.map(issue => issue.sourceIndex));
          setDrafts(items => items.filter(draft => failedSourceIndexes.has(draft.sourceIndex)));
          setAdditionalAttachments(items => Object.fromEntries(
            Object.entries(items).filter(([sourceIndex]) => failedSourceIndexes.has(Number(sourceIndex)))
          ));
          setSelected(0);
        }
        toast.error("Some articles could not be imported");
      }
      else { toast.success(`${data.created ?? 0} article${data.created === 1 ? "" : "s"} imported successfully`); navigate("/articles"); }
    } catch (cause) { setError(cause instanceof Error ? cause.message : "Import failed"); setIssues([]); }
    finally { setBusy(false); }
  };

  if (!drafts.length) return <div className="max-w-5xl mx-auto">
    <div className="flex items-center gap-3 mb-6">
      <Link to="/articles" className="p-2 rounded-lg hover:bg-zinc-100 dark:hover:bg-zinc-800"><ArrowLeft size={18}/></Link>
      <div><h1 className="text-xl font-bold text-zinc-900 dark:text-zinc-100">Bulk Knowledge Import</h1><p className="text-sm text-zinc-500 mt-0.5">Create editable articles from multiple source files.</p></div>
    </div>
    <ImportFeedback message={error} issues={issues}/>
    <div className="space-y-4">
      <label
        role="button"
        tabIndex={busy ? -1 : 0}
        aria-disabled={busy}
        onKeyDown={event => {
          if (!busy && (event.key === "Enter" || event.key === " ")) {
            event.preventDefault();
            fileInputRef.current?.click();
          }
        }}
        onDragEnter={event => {
          event.preventDefault();
          if (!busy && Array.from(event.dataTransfer.types).includes("Files")) setDragOver(true);
        }}
        onDragOver={event => {
          event.preventDefault();
          if (!busy && Array.from(event.dataTransfer.types).includes("Files")) {
            event.dataTransfer.dropEffect = "copy";
            setDragOver(true);
          }
        }}
        onDragLeave={event => {
          event.preventDefault();
          if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setDragOver(false);
        }}
        onDrop={event => {
          event.preventDefault();
          setDragOver(false);
          if (!busy) addSourceFiles(Array.from(event.dataTransfer.files));
        }}
        className={`block rounded-xl border-2 border-dashed p-10 text-center transition-colors focus:outline-none focus:ring-2 focus:ring-blue-500/40 ${busy ? "cursor-not-allowed opacity-60" : "cursor-pointer"} ${dragOver ? "border-blue-500 bg-blue-50/70 dark:bg-blue-950/20" : "border-zinc-300 hover:border-blue-500 hover:bg-blue-50/40 dark:border-zinc-700 dark:hover:bg-blue-950/10"}`}
      >
        <Upload className={`mx-auto mb-3 ${dragOver ? "text-blue-500" : "text-blue-600"}`} size={32}/><strong className="text-zinc-900 dark:text-zinc-100">{dragOver ? "Drop source files here" : "Select or drag & drop source files"}</strong><p className="text-sm text-zinc-500 mt-2">TXT, Markdown, CSV, Excel, PDF and Office files are parsed. Other supported files remain attachments.</p>
        <input ref={fileInputRef} multiple type="file" accept={ACCEPTED_SOURCE_FILES} disabled={busy} className="hidden" onChange={event => { addSourceFiles(Array.from(event.target.files ?? [])); event.target.value = ""; }}/>
      </label>
      {files.length > 0 && <div className="border border-zinc-200 dark:border-zinc-800 rounded-xl divide-y divide-zinc-200 dark:divide-zinc-800">{files.map((file, index) => <div key={`${file.name}-${index}`} className="p-3 flex items-center gap-3"><FileText size={17} className="text-zinc-500"/><span className="flex-1 text-sm truncate">{file.name}</span><span className="text-xs text-zinc-500">{(file.size / 1024).toFixed(1)} KB</span><button type="button" aria-label={`Remove ${file.name}`} onClick={() => setFiles(items => items.filter((_, itemIndex) => itemIndex !== index))} className="p-1 rounded hover:bg-zinc-100 dark:hover:bg-zinc-800"><X size={16}/></button></div>)}</div>}
      <div className="flex justify-end"><button disabled={!files.length || busy} onClick={analyze} className="flex items-center gap-2 px-4 py-2 bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white text-sm font-medium rounded-lg transition-colors"><WandSparkles size={16}/>{busy ? "Analyzing..." : "Analyze Sources"}</button></div>
    </div>
  </div>;

  return <div className="max-w-7xl mx-auto">
    <div className="flex flex-wrap items-center justify-between gap-3 mb-6">
      <div className="flex items-center gap-3"><button type="button" onClick={() => { setDrafts([]); setError(""); setIssues([]); setAdditionalAttachments({}); }} className="p-2 rounded-lg hover:bg-zinc-100 dark:hover:bg-zinc-800"><ArrowLeft size={18}/></button><div><h1 className="text-xl font-bold text-zinc-900 dark:text-zinc-100">Review Import Drafts</h1><p className="text-sm text-zinc-500 mt-0.5">Review each article before importing.</p></div></div>
      <div className="flex flex-col items-end gap-1">
        <button disabled={busy || hasBlockingAnalysisErrors} onClick={commit} className="flex items-center gap-2 px-4 py-2 bg-blue-600 hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50 text-white text-sm font-medium rounded-lg transition-colors"><Check size={16}/>{busy ? "Importing..." : `Import ${drafts.length} Article${drafts.length === 1 ? "" : "s"}`}</button>
        {hasBlockingAnalysisErrors && <span className="text-xs font-medium text-red-600 dark:text-red-400">Add manual content or remove failed articles to enable import.</span>}
      </div>
    </div>
    <ImportFeedback message={error} issues={issues}/>
    <div className="mb-5 p-4 border border-zinc-200 dark:border-zinc-800 rounded-xl"><div className="flex flex-wrap items-end gap-3"><div className="min-w-56 flex-1"><label className="text-xs font-medium text-zinc-500 mb-1.5 block">Tags for all drafts</label><TagSelector selectedTags={bulkTags} onChange={setBulkTags}/></div><button type="button" onClick={applyBulk} disabled={!bulkTags.length} className="px-4 py-2 text-sm font-medium border border-zinc-300 dark:border-zinc-700 rounded-lg disabled:opacity-50 hover:bg-zinc-50 dark:hover:bg-zinc-800">Apply to All</button></div></div>
    <div className="grid lg:grid-cols-[260px_minmax(0,1fr)] gap-6">
      <aside className="border border-zinc-200 dark:border-zinc-800 rounded-xl divide-y divide-zinc-200 dark:divide-zinc-800 h-fit overflow-hidden">{drafts.map((draft, index) => {
        const issue = issues.find(item => item.sourceIndex === draft.sourceIndex);
        const tone = issue?.severity === "error"
          ? "bg-red-50 dark:bg-red-950/30"
          : selected === index ? "bg-blue-50 dark:bg-blue-950/30" : "hover:bg-zinc-50 dark:hover:bg-zinc-900";
        return <div key={`${draft.fileName}-${index}`} className={`flex items-stretch transition-colors ${tone}`}>
          <button type="button" onClick={() => setSelected(index)} className="flex min-w-0 flex-1 items-center gap-2 p-3 text-left">
            <div className="min-w-0 flex-1"><p className="font-medium text-sm truncate">{draft.title || "Untitled article"}</p><p className="text-xs text-zinc-500 truncate">{draft.fileName}</p>{issue && <p className={`mt-1 flex items-center gap-1 text-xs ${issue.severity === "error" ? "font-medium text-red-600 dark:text-red-400" : "text-amber-600 dark:text-amber-400"}`}><AlertTriangle size={12} className="shrink-0"/><span className="truncate">{issue.reason}</span></p>}</div><ChevronRight size={15} className="shrink-0 text-zinc-400"/>
          </button>
          <button type="button" aria-label={`Remove ${draft.fileName}`} title={`Remove ${draft.fileName}`} onClick={() => removeDraft(index)} className={`m-2 ml-0 self-center rounded p-1.5 ${issue?.severity === "error" ? "text-red-600 hover:bg-red-100 dark:text-red-400 dark:hover:bg-red-900/50" : "text-zinc-400 hover:bg-zinc-100 hover:text-zinc-700 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"}`}><X size={16}/></button>
        </div>;
      })}</aside>
      {current && <section className="min-w-0">
        {current.analysisError && !currentAnalysisResolved && <div role="alert" className="flex items-start justify-between gap-3 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700 dark:border-red-800 dark:bg-red-950 dark:text-red-300"><span><strong className="block break-all">{current.fileName} could not be analyzed</strong><span className="mt-1 block">{current.analysisError}</span><span className="mt-1 block font-medium">Enter the article content manually below or remove this article.</span></span><button type="button" onClick={() => removeDraft(selected)} className="shrink-0 rounded-md border border-red-300 px-2.5 py-1.5 text-xs font-medium hover:bg-red-100 dark:border-red-700 dark:hover:bg-red-900/50">Remove article</button></div>}
        {current.analysisError && currentAnalysisResolved && <div role="status" className="rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800 dark:border-amber-800 dark:bg-amber-950/30 dark:text-amber-300"><strong className="block break-all">Manual content will be used for {current.fileName}</strong><span className="mt-1 block">The original analysis failed, but this draft is now ready to import. Keep the attachment option selected to retain the source file.</span></div>}
        {!current.analysisError && current.warning && <div className="p-3 rounded-lg bg-amber-50 text-amber-800 dark:bg-amber-950/30 dark:text-amber-300 text-sm">{current.warning}</div>}
        <div className={current.analysisError || current.warning ? "mt-5 mb-6" : "mb-6"}>
          <textarea ref={titleRef} rows={1} value={current.title} onChange={event => update({ title: event.target.value.replace(/\r?\n/g, " ") })} placeholder="Makale başlığı..." aria-label="Makale başlığı" maxLength={300} className="w-full resize-none overflow-hidden bg-transparent text-3xl font-bold leading-tight text-zinc-900 outline-none placeholder:text-zinc-300 dark:text-zinc-100 dark:placeholder:text-zinc-600"/>
          <textarea ref={excerptRef} rows={1} value={current.excerpt ?? ""} onChange={event => update({ excerpt: event.target.value.replace(/\r?\n/g, " ") })} placeholder="Kısa açıklama (isteğe bağlı)..." aria-label="Kısa açıklama" className="mt-2 block w-full resize-none overflow-hidden bg-transparent text-base leading-relaxed text-zinc-500 outline-none placeholder:text-zinc-400 dark:text-zinc-400"/>

          <div className="mt-4 flex flex-wrap items-center gap-3 text-sm text-zinc-500">
            <ContentTypeSelect options={contentTypes} value={current.contentType} onChange={contentType => update({ contentType })}/>
            <div className="inline-flex min-w-0 items-center gap-2">
              <label>
                <span className="sr-only">Yayın durumu</span>
                <select value={current.status} onChange={event => update({ status: event.target.value })} aria-describedby="import-article-status-description" className="rounded-md border border-zinc-300 bg-white px-2 py-1 text-xs font-medium text-zinc-700 outline-none transition-colors focus:border-blue-500 focus:ring-2 focus:ring-blue-500/20 dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-200"><option value="draft">Taslak</option><option value="published">Yayımlandı</option></select>
              </label>
              <span id="import-article-status-description" className="text-xs text-zinc-400 dark:text-zinc-500">{STATUS_DESCRIPTIONS[current.status] ?? ""}</span>
            </div>
          </div>

          <div className="mt-3 flex items-start gap-2">
            <Tag size={14} className="mt-2 shrink-0 text-zinc-400"/>
            <TagSelector selectedTags={current.tags} onChange={tags => update({ tags })}/>
          </div>
        </div>

        <label className="mb-5 flex cursor-pointer items-center gap-3 rounded-xl border border-zinc-200 bg-zinc-50 p-3.5 transition-colors hover:border-blue-300 hover:bg-blue-50/50 dark:border-zinc-800 dark:bg-zinc-900 dark:hover:border-blue-800 dark:hover:bg-blue-950/20">
          <input type="checkbox" checked={current.keepOriginal} onChange={event => update({ keepOriginal: event.target.checked })} className="size-4 accent-blue-600"/>
          <Paperclip size={18} className="shrink-0 text-zinc-500"/>
          <span className="min-w-0 text-sm text-zinc-700 dark:text-zinc-300">Orijinal <strong className="break-all">{current.fileName}</strong> dosyasını ek dosya olarak sakla</span>
        </label>

        <PendingFileList
          title="Additional attachments"
          files={currentAdditionalAttachments}
          onAdd={newFiles => setAdditionalAttachments(items => ({
            ...items,
            [current.sourceIndex]: [...(items[current.sourceIndex] ?? []), ...newFiles],
          }))}
          onRemove={attachmentIndex => setAdditionalAttachments(items => ({
            ...items,
            [current.sourceIndex]: (items[current.sourceIndex] ?? []).filter((_, index) => index !== attachmentIndex),
          }))}
        />

        <div className="mt-6"><Suspense fallback={<div className="h-64 animate-pulse rounded-lg bg-zinc-50 dark:bg-zinc-900"/>}><MilkdownEditor key={current.sourceIndex} contentMarkdown={current.contentMarkdown} onChange={contentMarkdown => update({ contentMarkdown })}/></Suspense></div>
      </section>}
    </div>
  </div>;
}
