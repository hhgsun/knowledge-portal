import { useRef, useState } from "react";
import { Upload, Loader2 } from "lucide-react";
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
