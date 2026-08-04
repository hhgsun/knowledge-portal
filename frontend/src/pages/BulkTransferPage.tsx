import { useEffect, useState } from "react";
import { Download, FileCheck2, FileJson, FileSpreadsheet, Info, Upload } from "lucide-react";
import { toast } from "sonner";
import { useApi } from "../hooks/useApi";

interface ImportError { row: number; title?: string; error: string }
interface ImportResult {
  dryRun: boolean; total: number; created: number; updated: number;
  skipped: number; failed: number; errors: ImportError[];
}
interface ImportSchema {
  maxRecords: number;
  maxFileSizeMb: number;
  statuses: string[];
  contentTypes: { value: string; label: string }[];
  conflictPolicies: string[];
  attachmentsIncluded: boolean;
  fields: { name: string; required: boolean; description: string }[];
}
interface ExportAuthor { id: string; name: string; slug: string }
interface ExportTag { id: string; name: string; slug: string }

export default function BulkTransferPage() {
  const { fetchWithAuth } = useApi();
  const [file, setFile] = useState<File | null>(null);
  const [policy, setPolicy] = useState("skip");
  const [result, setResult] = useState<ImportResult | null>(null);
  const [busy, setBusy] = useState(false);
  const [format, setFormat] = useState("jsonl");
  const [status, setStatus] = useState("");
  const [contentType, setContentType] = useState("");
  const [authorId, setAuthorId] = useState("");
  const [tag, setTag] = useState("");
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [mine, setMine] = useState(false);
  const [schema, setSchema] = useState<ImportSchema | null>(null);
  const [authors, setAuthors] = useState<ExportAuthor[]>([]);
  const [tags, setTags] = useState<ExportTag[]>([]);

  useEffect(() => {
    fetchWithAuth("/api/bulk/import-schema")
      .then(async (response) => { if (response.ok) setSchema(await response.json()); })
      .catch(() => undefined);
  }, [fetchWithAuth]);

  useEffect(() => {
    Promise.all([fetchWithAuth("/api/search/authors"), fetchWithAuth("/api/tags")])
      .then(async ([authorsResponse, tagsResponse]) => {
        if (authorsResponse.ok) setAuthors(await authorsResponse.json());
        if (tagsResponse.ok) setTags(await tagsResponse.json());
      })
      .catch(() => undefined);
  }, [fetchWithAuth]);

  const downloadResponse = async (response: Response, fallbackName: string) => {
    const blob = await response.blob();
    const disposition = response.headers.get("content-disposition") || "";
    const name = disposition.match(/filename\*?=(?:UTF-8''|\")?([^\";]+)/i)?.[1] || fallbackName;
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a"); link.href = url; link.download = decodeURIComponent(name); link.click();
    URL.revokeObjectURL(url);
  };

  const downloadTemplate = async (templateFormat: "jsonl" | "csv") => {
    const response = await fetchWithAuth(`/api/bulk/templates/${templateFormat}`);
    if (!response.ok) { toast.error("Template download failed"); return; }
    await downloadResponse(response, `article-import-template.${templateFormat}`);
  };

  const runImport = async (dryRun: boolean) => {
    if (!file) { toast.error("Select a JSONL or CSV file"); return; }
    const body = new FormData();
    body.append("file", file);
    body.append("dryRun", String(dryRun));
    body.append("conflictPolicy", policy);
    setBusy(true);
    try {
      const response = await fetchWithAuth("/api/bulk/import", { method: "POST", body, noRetry: true });
      const data = await response.json();
      if (!response.ok) { toast.error(data.error || "Import failed"); return; }
      setResult(data);
      toast.success(dryRun ? "Validation completed" : "Import completed");
    } finally { setBusy(false); }
  };

  const runExport = async () => {
    setBusy(true);
    try {
      const params = new URLSearchParams({ format });
      if (status) params.set("status", status);
      if (contentType) params.set("contentType", contentType);
      if (authorId) params.set("authorId", authorId);
      if (tag) params.set("tag", tag);
      if (dateFrom) params.set("dateFrom", dateFrom);
      if (dateTo) params.set("dateTo", dateTo);
      if (mine) params.set("mine", "true");
      const response = await fetchWithAuth(`/api/bulk/export?${params}`);
      if (!response.ok) { const data = await response.json(); toast.error(data.error || "Export failed"); return; }
      await downloadResponse(response, `knowledge-portal.${format}`); toast.success("Export downloaded");
    } finally { setBusy(false); }
  };

  return (
    <div className="max-w-4xl mx-auto space-y-6">
      <div><h1 className="text-2xl font-bold text-zinc-900 dark:text-zinc-100">Bulk Import & Export</h1>
        <p className="text-sm text-zinc-500 mt-1">Move up to {schema?.maxRecords.toLocaleString() ?? "5,000"} articles at a time using JSONL or CSV.</p></div>

      <section className="p-5 border border-blue-200 dark:border-blue-900 bg-blue-50/50 dark:bg-blue-950/20 rounded-xl space-y-4">
        <div className="flex items-start gap-3">
          <Info size={20} className="text-blue-600 mt-0.5 shrink-0" />
          <div>
            <h2 className="font-semibold text-zinc-900 dark:text-zinc-100">Start with a template</h2>
            <p className="text-sm text-zinc-600 dark:text-zinc-400 mt-1">Download a sample, replace its example rows with your articles, then validate it before importing.</p>
          </div>
        </div>
        <div className="flex flex-wrap gap-2">
          <button onClick={() => downloadTemplate("jsonl")} className="flex items-center gap-2 px-4 py-2 bg-white dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-700 rounded-lg text-sm font-medium hover:border-blue-400">
            <FileJson size={17} className="text-blue-600" /> Download JSONL template
          </button>
          <button onClick={() => downloadTemplate("csv")} className="flex items-center gap-2 px-4 py-2 bg-white dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-700 rounded-lg text-sm font-medium hover:border-green-500">
            <FileSpreadsheet size={17} className="text-green-600" /> Download CSV template
          </button>
        </div>
        <details className="text-sm">
          <summary className="cursor-pointer font-medium text-blue-700 dark:text-blue-300">Formatting instructions and supported fields</summary>
          <div className="mt-3 space-y-4 text-zinc-600 dark:text-zinc-400">
            <div className="overflow-x-auto">
              <table className="w-full text-left border-collapse">
                <thead><tr className="border-b border-zinc-200 dark:border-zinc-700"><th className="py-2 pr-4">Field</th><th className="py-2 pr-4">Required</th><th className="py-2">Description</th></tr></thead>
                <tbody>
                  {(schema?.fields ?? []).map((field) => <FieldRow key={field.name} {...field} />)}
                </tbody>
              </table>
            </div>
            <ul className="list-disc pl-5 space-y-1">
              <li>JSONL keeps rich TipTap formatting and is recommended for backups and system transfers.</li>
              <li>Each physical line in a JSONL file must be one complete JSON object; do not wrap records in an array.</li>
              <li>CSV is best for editing in Excel or LibreOffice. Plain content is converted to a TipTap paragraph.</li>
              {schema && <li>Valid statuses: <code>{schema.statuses.join(", ")}</code>.</li>}
              {schema && <li>Active content types: <code>{schema.contentTypes.map((type) => type.value).join(", ")}</code>.</li>}
              <li>Import is limited to {schema?.maxRecords.toLocaleString() ?? "5,000"} records and a {schema?.maxFileSizeMb ?? 100} MB file. {!schema?.attachmentsIncluded && "Attachments are not included."}</li>
              <li>Run <strong>Validate</strong> first; validation does not save any data.</li>
            </ul>
          </div>
        </details>
      </section>

      <section className="p-5 border border-zinc-200 dark:border-zinc-800 rounded-xl space-y-4">
        <div className="flex items-center gap-2"><Upload size={19} /><h2 className="font-semibold">Import articles</h2></div>
        <input type="file" accept=".jsonl,.ndjson,.csv" onChange={(e) => { setFile(e.target.files?.[0] || null); setResult(null); }}
          className="block w-full text-sm file:mr-4 file:px-4 file:py-2 file:rounded-lg file:border-0 file:bg-blue-50 file:text-blue-700 dark:file:bg-blue-950 dark:file:text-blue-300" />
        <div><label className="block text-xs font-medium text-zinc-500 mb-1">When a matching slug exists</label>
          <select value={policy} onChange={(e) => setPolicy(e.target.value)} className="px-3 py-2 text-sm border rounded-lg bg-white dark:bg-zinc-900 border-zinc-300 dark:border-zinc-700">
            <option value="skip">Skip existing</option><option value="update">Update existing</option><option value="duplicate">Create a copy</option>
          </select></div>
        <div className="flex gap-2">
          <button disabled={busy || !file} onClick={() => runImport(true)} className="flex items-center gap-2 px-4 py-2 text-sm border rounded-lg disabled:opacity-50"><FileCheck2 size={16} /> Validate</button>
          <button disabled={busy || !file} onClick={() => runImport(false)} className="flex items-center gap-2 px-4 py-2 text-sm bg-blue-600 text-white rounded-lg disabled:opacity-50"><Upload size={16} /> Import</button>
        </div>
        {result && <div className="p-4 bg-zinc-50 dark:bg-zinc-900 rounded-lg text-sm">
          <div className="grid grid-cols-2 sm:grid-cols-5 gap-3">
            <Stat label="Total" value={result.total} /><Stat label={result.dryRun ? "Would create" : "Created"} value={result.created} />
            <Stat label={result.dryRun ? "Would update" : "Updated"} value={result.updated} /><Stat label="Skipped" value={result.skipped} /><Stat label="Failed" value={result.failed} />
          </div>
          {result.errors.length > 0 && <div className="mt-4 max-h-56 overflow-auto space-y-1 text-red-600 dark:text-red-400">
            {result.errors.map((error) => <p key={`${error.row}-${error.error}`}>Row {error.row}{error.title ? ` (${error.title})` : ""}: {error.error}</p>)}
          </div>}
        </div>}
      </section>

      <section className="p-5 border border-zinc-200 dark:border-zinc-800 rounded-xl space-y-4">
        <div className="flex items-center gap-2"><Download size={19} /><h2 className="font-semibold">Export articles</h2></div>
        <p className="text-sm text-zinc-500"><strong>JSONL</strong> preserves rich content and can be imported again. <strong>CSV</strong> is easier to review and edit in spreadsheets. Exports contain at most {schema?.maxRecords.toLocaleString() ?? "5,000"} articles{!schema?.attachmentsIncluded && " and do not include attachments"}.</p>
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
          <ExportSelect label="Format" value={format} onChange={setFormat}><option value="jsonl">JSONL</option><option value="csv">CSV</option></ExportSelect>
          <ExportSelect label="Status" value={status} onChange={setStatus}><option value="">All statuses</option>{(schema?.statuses ?? []).map((value) => <option key={value} value={value}>{value}</option>)}</ExportSelect>
          <ExportSelect label="Content type" value={contentType} onChange={setContentType}><option value="">All content types</option>{(schema?.contentTypes ?? []).map((type) => <option key={type.value} value={type.value}>{type.label}</option>)}</ExportSelect>
          <ExportSelect label="Author" value={authorId} onChange={setAuthorId} disabled={mine}><option value="">All authors</option>{authors.map((author) => <option key={author.id} value={author.id}>{author.name}</option>)}</ExportSelect>
          <ExportSelect label="Tag" value={tag} onChange={setTag}><option value="">All tags</option>{tags.map((item) => <option key={item.id} value={item.slug}>{item.name}</option>)}</ExportSelect>
          <label className="text-sm"><span className="block text-xs font-medium text-zinc-500 mb-1">Updated from</span><input type="date" value={dateFrom} max={dateTo || undefined} onChange={(e) => setDateFrom(e.target.value)} className="w-full px-3 py-2 border rounded-lg bg-white dark:bg-zinc-900 border-zinc-300 dark:border-zinc-700" /></label>
          <label className="text-sm"><span className="block text-xs font-medium text-zinc-500 mb-1">Updated to</span><input type="date" value={dateTo} min={dateFrom || undefined} onChange={(e) => setDateTo(e.target.value)} className="w-full px-3 py-2 border rounded-lg bg-white dark:bg-zinc-900 border-zinc-300 dark:border-zinc-700" /></label>
          <label className="flex items-center gap-2 text-sm self-end h-10"><input type="checkbox" checked={mine} onChange={(e) => { setMine(e.target.checked); if (e.target.checked) setAuthorId(""); }} /> Only my articles</label>
        </div>
        <button disabled={busy} onClick={runExport} className="flex items-center gap-2 px-4 py-2 text-sm bg-zinc-900 dark:bg-zinc-100 text-white dark:text-zinc-900 rounded-lg disabled:opacity-50"><Download size={16} /> Download export</button>
      </section>
    </div>
  );
}

function Stat({ label, value }: { label: string; value: number }) {
  return <div><p className="text-xs text-zinc-500">{label}</p><p className="text-xl font-semibold">{value}</p></div>;
}

function FieldRow({ name, required = false, description }: { name: string; required?: boolean; description: string }) {
  return <tr className="border-b last:border-0 border-zinc-100 dark:border-zinc-800"><td className="py-2 pr-4 font-mono text-xs">{name}</td><td className="py-2 pr-4">{required ? "Yes" : "No"}</td><td className="py-2">{description}</td></tr>;
}

function ExportSelect({ label, value, onChange, disabled = false, children }: { label: string; value: string; onChange: (value: string) => void; disabled?: boolean; children: React.ReactNode }) {
  return <label className="text-sm"><span className="block text-xs font-medium text-zinc-500 mb-1">{label}</span><select value={value} disabled={disabled} onChange={(event) => onChange(event.target.value)} className="w-full px-3 py-2 border rounded-lg bg-white dark:bg-zinc-900 border-zinc-300 dark:border-zinc-700 disabled:opacity-50">{children}</select></label>;
}
