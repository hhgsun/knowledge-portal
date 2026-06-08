import { useRef, useState } from "react";
import { Upload, Loader2, File as FileIcon, FileText, Image, Trash2, Clock } from "lucide-react";
import { cn } from "../../lib/utils";

const ALLOWED_EXTENSIONS = ".png,.jpg,.jpeg,.gif,.webp,.pdf,.md,.txt,.docx,.xlsx,.yaml,.json,.csv,.svg";

interface FileUploadZoneProps {
  onUpload: (files: File[]) => Promise<void>;
}

export default function FileUploadZone({ onUpload }: FileUploadZoneProps) {
  const [dragOver, setDragOver] = useState(false);
  const [uploading, setUploading] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const handleFiles = async (files: File[]) => {
    if (files.length === 0) return;
    setUploading(true);
    await onUpload(files);
    setUploading(false);
    if (fileInputRef.current) fileInputRef.current.value = "";
  };

  return (
    <div
      onDrop={(e) => { e.preventDefault(); setDragOver(false); if (!uploading) handleFiles(Array.from(e.dataTransfer.files)); }}
      onDragOver={(e) => { e.preventDefault(); if (!uploading) setDragOver(true); }}
      onDragLeave={(e) => { e.preventDefault(); setDragOver(false); }}
      className={cn(
        "mt-6 border rounded-xl overflow-hidden transition-colors",
        dragOver
          ? "border-blue-400 dark:border-blue-500 bg-blue-50/50 dark:bg-blue-900/10"
          : "border-zinc-200 dark:border-zinc-800"
      )}
    >
      <div className="flex items-center justify-between px-4 py-3 bg-zinc-50 dark:bg-zinc-900 border-b border-zinc-200 dark:border-zinc-800">
        <h3 className="text-sm font-medium text-zinc-700 dark:text-zinc-300">
          Attachments
        </h3>
        <label
          className={cn(
            "inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded-lg cursor-pointer transition-colors",
            "bg-blue-50 text-blue-700 hover:bg-blue-100 dark:bg-blue-900/30 dark:text-blue-400 dark:hover:bg-blue-900/50",
            uploading && "opacity-50 pointer-events-none"
          )}
        >
          {uploading ? <Loader2 size={14} className="animate-spin" /> : <Upload size={14} />}
          {uploading ? "Uploading..." : "Upload"}
          <input
            ref={fileInputRef}
            type="file"
            accept={ALLOWED_EXTENSIONS}
            multiple
            onChange={(e) => handleFiles(Array.from(e.target.files || []))}
            className="hidden"
            disabled={uploading}
          />
        </label>
      </div>

      <div className={cn(
        "px-4 py-6 text-center text-sm",
        dragOver ? "text-blue-500" : "text-zinc-400"
      )}>
        {dragOver
          ? "Drop files here to upload"
          : "No attachments yet. Drag & drop files here or click Upload."}
      </div>
    </div>
  );
}

// --- Helpers for PendingFileList ---

function getFileIconForType(type: string) {
  if (type.startsWith("image/")) return <Image size={16} className="text-blue-500" />;
  if (type === "application/pdf") return <FileText size={16} className="text-red-500" />;
  return <FileIcon size={16} className="text-zinc-500" />;
}

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

// Pending file list for deferred upload mode
interface PendingFileListProps {
  files: File[];
  onAdd: (files: File[]) => void;
  onRemove: (index: number) => void;
}

export function PendingFileList({ files, onAdd, onRemove }: PendingFileListProps) {
  const [dragOver, setDragOver] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  return (
    <div
      onDrop={(e) => { e.preventDefault(); setDragOver(false); onAdd(Array.from(e.dataTransfer.files)); }}
      onDragOver={(e) => { e.preventDefault(); setDragOver(true); }}
      onDragLeave={(e) => { e.preventDefault(); setDragOver(false); }}
      className={cn(
        "mt-6 border rounded-xl overflow-hidden transition-colors",
        dragOver
          ? "border-blue-400 dark:border-blue-500 bg-blue-50/50 dark:bg-blue-900/10"
          : "border-zinc-200 dark:border-zinc-800"
      )}
    >
      <div className="flex items-center justify-between px-4 py-3 bg-zinc-50 dark:bg-zinc-900 border-b border-zinc-200 dark:border-zinc-800">
        <h3 className="text-sm font-medium text-zinc-700 dark:text-zinc-300">
          Attachments {files.length > 0 && `(${files.length})`}
        </h3>
        <label
          className={cn(
            "inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded-lg cursor-pointer transition-colors",
            "bg-blue-50 text-blue-700 hover:bg-blue-100 dark:bg-blue-900/30 dark:text-blue-400 dark:hover:bg-blue-900/50"
          )}
        >
          <Upload size={14} />
          Add Files
          <input
            ref={fileInputRef}
            type="file"
            accept={ALLOWED_EXTENSIONS}
            multiple
            onChange={(e) => { onAdd(Array.from(e.target.files || [])); e.target.value = ""; }}
            className="hidden"
          />
        </label>
      </div>

      {files.length === 0 ? (
        <div className={cn(
          "px-4 py-6 text-center text-sm",
          dragOver ? "text-blue-500" : "text-zinc-400"
        )}>
          {dragOver
            ? "Drop files here to add"
            : "No attachments yet. Drag & drop files here or click Add Files."}
        </div>
      ) : (
        <ul className="divide-y divide-zinc-100 dark:divide-zinc-800">
          {files.map((file, index) => (
            <li key={`${file.name}-${index}`} className="flex items-center gap-3 px-4 py-2.5 bg-emerald-50/50 dark:bg-emerald-950/20 hover:bg-emerald-50 dark:hover:bg-emerald-950/30 transition-colors">
              {getFileIconForType(file.type)}
              <span className="flex-1 text-sm text-zinc-700 dark:text-zinc-300 truncate" title={file.name}>
                {file.name}
              </span>
              <span className="inline-flex items-center gap-1 px-1.5 py-0.5 text-[10px] font-medium rounded bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-400">
                <Clock size={10} />
                Kaydedilince yüklenecek
              </span>
              <span className="text-xs text-zinc-400 whitespace-nowrap">
                {formatFileSize(file.size)}
              </span>
              <button
                onClick={() => onRemove(index)}
                className="p-1 rounded hover:bg-red-100 dark:hover:bg-red-900/30 transition-colors"
                title="Remove"
              >
                <Trash2 size={14} className="text-red-500" />
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
