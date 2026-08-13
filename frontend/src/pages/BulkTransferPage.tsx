import { useEffect, useState } from "react";
import { Download, FileCheck2, FileText, Info, Upload } from "lucide-react";
import { toast } from "sonner";
import { useApi } from "../hooks/useApi";

interface ImportResult { dryRun: boolean; total: number; created: number; updated: number; skipped: number; failed: number; errors: { row: number; title?: string; error: string }[] }
interface ImportSchema {
  maxRecords: number; maxFileSizeMb: number; statuses: string[];
  contentTypes: { value: string; label: string }[]; attachmentsIncluded: boolean;
  fields: { name: string; required: boolean; description: string }[];
}
interface Author { id: string; name: string }
interface Tag { id: string; name: string; slug: string }

export default function BulkTransferPage() {
  const { fetchWithAuth } = useApi();
  const [schema, setSchema] = useState<ImportSchema | null>(null);
  const [authors, setAuthors] = useState<Author[]>([]);
  const [tags, setTags] = useState<Tag[]>([]);
  const [file, setFile] = useState<File | null>(null);
  const [policy, setPolicy] = useState("skip");
  const [result, setResult] = useState<ImportResult | null>(null);
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState("");
  const [contentType, setContentType] = useState("");
  const [authorId, setAuthorId] = useState("");
  const [tag, setTag] = useState("");
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [mine, setMine] = useState(false);

  useEffect(() => {
    Promise.all([fetchWithAuth("/api/bulk/import-schema"), fetchWithAuth("/api/search/authors"), fetchWithAuth("/api/tags")])
      .then(async ([schemaResponse, authorsResponse, tagsResponse]) => {
        if (schemaResponse.ok) setSchema(await schemaResponse.json());
        if (authorsResponse.ok) setAuthors(await authorsResponse.json());
        if (tagsResponse.ok) setTags(await tagsResponse.json());
      }).catch(() => undefined);
  }, [fetchWithAuth]);

  const downloadResponse = async (response: Response, fallbackName: string) => {
    const blob = await response.blob();
    const disposition = response.headers.get("content-disposition") || "";
    const name = disposition.match(/filename\*?=(?:UTF-8''|\")?([^\";]+)/i)?.[1] || fallbackName;
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a"); link.href = url; link.download = decodeURIComponent(name); link.click();
    URL.revokeObjectURL(url);
  };

  const downloadTemplate = async () => {
    const response = await fetchWithAuth("/api/bulk/templates/md");
    if (!response.ok) { toast.error("Template download failed"); return; }
    await downloadResponse(response, "article-import-template.md");
  };

  const runImport = async (dryRun: boolean) => {
    if (!file) { toast.error("Select a Markdown file or Markdown ZIP archive"); return; }
    const body = new FormData(); body.append("file", file); body.append("dryRun", String(dryRun)); body.append("conflictPolicy", policy);
    setBusy(true);
    try {
      const response = await fetchWithAuth("/api/bulk/import", { method: "POST", body, noRetry: true });
      const data = await response.json();
      if (!response.ok) { toast.error(data.error || "Import failed"); return; }
      setResult(data); toast.success(dryRun ? "Export file validated" : "Portal data imported");
    } finally { setBusy(false); }
  };

  const runExport = async () => {
    const params = new URLSearchParams({ format: "markdown" });
    if (status) params.set("status", status); if (contentType) params.set("contentType", contentType);
    if (authorId) params.set("authorId", authorId); if (tag) params.set("tag", tag);
    if (dateFrom) params.set("dateFrom", dateFrom); if (dateTo) params.set("dateTo", dateTo); if (mine) params.set("mine", "true");
    setBusy(true);
    try {
      const response = await fetchWithAuth(`/api/bulk/export?${params}`);
      if (!response.ok) { const data = await response.json(); toast.error(data.error || "Export failed"); return; }
      await downloadResponse(response, "knowledge-portal.zip"); toast.success("Markdown archive downloaded");
    } finally { setBusy(false); }
  };

  return <div className="max-w-4xl mx-auto space-y-6">
    <div><h1 className="text-2xl font-bold text-zinc-900 dark:text-zinc-100">Knowledge Portal Data Transfer</h1>
      <p className="text-sm text-zinc-500 mt-1">Export articles from one Knowledge Portal and import them into another.</p></div>

    <section className="p-5 border border-blue-200 dark:border-blue-900 bg-blue-50/50 dark:bg-blue-950/20 rounded-xl space-y-4">
      <div className="flex items-start gap-3"><Info size={20} className="text-blue-600 mt-0.5 shrink-0" /><div><h2 className="font-semibold">Markdown transfer format</h2><p className="text-sm text-zinc-600 dark:text-zinc-400 mt-1">Each article is a canonical CommonMark/GFM <code>.md</code> file. Article metadata is kept as JSON-compatible front matter; multi-article exports are packaged as ZIP.</p></div></div>
      <div className="flex flex-wrap gap-2">
        <button onClick={downloadTemplate} className="flex items-center gap-2 px-4 py-2 bg-white dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-700 rounded-lg text-sm font-medium"><FileText size={17} className="text-violet-600" /> Download Markdown template</button>
      </div>
      <details className="text-sm"><summary className="cursor-pointer font-medium text-blue-700 dark:text-blue-300">Supported transfer fields</summary>
        <div className="mt-3 overflow-x-auto"><table className="w-full text-left"><thead><tr className="border-b"><th className="py-2">Field</th><th>Required</th><th>Description</th></tr></thead><tbody>{(schema?.fields ?? []).map((field) => <tr key={field.name} className="border-b last:border-0 border-zinc-100 dark:border-zinc-800"><td className="py-2 font-mono text-xs">{field.name}</td><td>{field.required ? "Yes" : "No"}</td><td>{field.description}</td></tr>)}</tbody></table></div>
      </details>
    </section>

    <section className="p-5 border border-zinc-200 dark:border-zinc-800 rounded-xl space-y-4">
      <div className="flex items-center gap-2"><Upload size={19} /><h2 className="font-semibold">Import from Knowledge Portal</h2></div>
      <p className="text-sm text-zinc-500">Select one Markdown file or a ZIP archive containing multiple Markdown articles. Attachments are not included.</p>
      <input type="file" accept=".md,.markdown,.zip" onChange={(event) => { setFile(event.target.files?.[0] || null); setResult(null); }} className="block w-full text-sm file:mr-4 file:px-4 file:py-2 file:rounded-lg file:border-0 file:bg-blue-50 file:text-blue-700" />
      <Select label="When a matching article exists" value={policy} onChange={setPolicy}><option value="skip">Skip existing</option><option value="update">Update existing</option><option value="duplicate">Create a copy</option></Select>
      <p className="text-xs text-zinc-500">Maximum {schema?.maxRecords.toLocaleString() ?? "5,000"} articles and {schema?.maxFileSizeMb ?? 100} MB per import. Validate before importing.</p>
      <div className="flex gap-2"><button disabled={busy || !file} onClick={() => runImport(true)} className="flex items-center gap-2 px-4 py-2 text-sm border rounded-lg disabled:opacity-50"><FileCheck2 size={16} /> Validate export</button><button disabled={busy || !file} onClick={() => runImport(false)} className="flex items-center gap-2 px-4 py-2 text-sm bg-blue-600 text-white rounded-lg disabled:opacity-50"><Upload size={16} /> Import portal data</button></div>
      {result && <Result result={result} />}
    </section>

    <section className="p-5 border border-zinc-200 dark:border-zinc-800 rounded-xl space-y-4">
      <div className="flex items-center gap-2"><Download size={19} /><h2 className="font-semibold">Export for another Knowledge Portal</h2></div>
      <p className="text-sm text-zinc-500">Create a filtered transfer file that can be validated and imported by another Knowledge Portal.</p>
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
        <Select label="Status" value={status} onChange={setStatus}><option value="">All statuses</option>{(schema?.statuses ?? []).map((value) => <option key={value}>{value}</option>)}</Select>
        <Select label="Content type" value={contentType} onChange={setContentType}><option value="">All content types</option>{(schema?.contentTypes ?? []).map((type) => <option key={type.value} value={type.value}>{type.label}</option>)}</Select>
        <Select label="Author" value={authorId} onChange={setAuthorId} disabled={mine}><option value="">All authors</option>{authors.map((author) => <option key={author.id} value={author.id}>{author.name}</option>)}</Select>
        <Select label="Tag" value={tag} onChange={setTag}><option value="">All tags</option>{tags.map((item) => <option key={item.id} value={item.slug}>{item.name}</option>)}</Select>
        <DateInput label="Updated from" value={dateFrom} onChange={setDateFrom} max={dateTo || undefined} />
        <DateInput label="Updated to" value={dateTo} onChange={setDateTo} min={dateFrom || undefined} />
        <label className="flex items-center gap-2 text-sm self-end h-10"><input type="checkbox" checked={mine} onChange={(event) => { setMine(event.target.checked); if (event.target.checked) setAuthorId(""); }} /> Only my articles</label>
      </div>
      <button disabled={busy} onClick={runExport} className="flex items-center gap-2 px-4 py-2 text-sm bg-zinc-900 dark:bg-zinc-100 text-white dark:text-zinc-900 rounded-lg disabled:opacity-50"><Download size={16} /> Download portal export</button>
    </section>
  </div>;
}

function Result({ result }: { result: ImportResult }) { return <div className="p-4 bg-zinc-50 dark:bg-zinc-900 rounded-lg text-sm"><div className="grid grid-cols-2 sm:grid-cols-5 gap-3">{[["Total", result.total], [result.dryRun ? "Would create" : "Created", result.created], [result.dryRun ? "Would update" : "Updated", result.updated], ["Skipped", result.skipped], ["Failed", result.failed]].map(([label, value]) => <div key={String(label)}><p className="text-xs text-zinc-500">{label}</p><p className="text-xl font-semibold">{value}</p></div>)}</div>{result.errors.length > 0 && <div className="mt-4 max-h-56 overflow-auto text-red-600 space-y-1">{result.errors.map((error) => <p key={`${error.row}-${error.error}`}>Row {error.row}{error.title ? ` (${error.title})` : ""}: {error.error}</p>)}</div>}</div>; }
function Select({ label, value, onChange, disabled = false, children }: { label: string; value: string; onChange: (value: string) => void; disabled?: boolean; children: React.ReactNode }) { return <label className="text-sm"><span className="block text-xs font-medium text-zinc-500 mb-1">{label}</span><select value={value} disabled={disabled} onChange={(event) => onChange(event.target.value)} className="w-full px-3 py-2 border rounded-lg bg-white dark:bg-zinc-900 border-zinc-300 dark:border-zinc-700 disabled:opacity-50">{children}</select></label>; }
function DateInput({ label, value, onChange, min, max }: { label: string; value: string; onChange: (value: string) => void; min?: string; max?: string }) { return <label className="text-sm"><span className="block text-xs font-medium text-zinc-500 mb-1">{label}</span><input type="date" value={value} min={min} max={max} onChange={(event) => onChange(event.target.value)} className="w-full px-3 py-2 border rounded-lg bg-white dark:bg-zinc-900 border-zinc-300 dark:border-zinc-700" /></label>; }
