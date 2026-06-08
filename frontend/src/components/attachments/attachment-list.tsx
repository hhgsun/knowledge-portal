import { useCallback, useEffect, useRef, useState } from "react";
import { useApi } from "../../hooks/useApi";
import { toast } from "sonner";
import {
  File,
  FileText,
  Image,
  Download,
  Trash2,
  Upload,
  Loader2,
  Clock,
  Undo2,
} from "lucide-react";
import { cn } from "../../lib/utils";
import type { ArticleAttachment, AttachmentListResponse } from "../../types/api";

interface AttachmentListProps {
  articleId: string;
  canEdit: boolean;
  /** When set, delete is deferred — calls this instead of immediate API delete */
  onDeferredDelete?: (attachment: ArticleAttachment) => void;
  /** Callback to undo a deferred delete */
  onUndoDelete?: (attachmentId: string) => void;
  /** Hide the upload button (use pendingFiles + onAddFiles instead) */
  hideUpload?: boolean;
  /** Attachment IDs marked for deletion (shown with strikethrough) */
  deletedIds?: Set<string>;
  /** Pending files to show inline (not yet uploaded) */
  pendingFiles?: File[];
  /** Callback to add new files to pending queue */
  onAddFiles?: (files: File[]) => void;
  /** Callback to remove a pending file by index */
  onRemovePendingFile?: (index: number) => void;
}

const ALLOWED_EXTENSIONS = ".png,.jpg,.jpeg,.gif,.webp,.pdf,.md,.txt,.docx,.xlsx,.yaml,.json,.csv,.svg";

function getFileIcon(contentType: string) {
  if (contentType.startsWith("image/")) return <Image size={16} className="text-blue-500" />;
  if (contentType === "application/pdf") return <FileText size={16} className="text-red-500" />;
  return <File size={16} className="text-zinc-500" />;
}

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export default function AttachmentList({ articleId, canEdit, onDeferredDelete, onUndoDelete, hideUpload, deletedIds, pendingFiles, onAddFiles, onRemovePendingFile }: AttachmentListProps) {
  const { fetchWithAuth } = useApi();
  const [attachments, setAttachments] = useState<ArticleAttachment[]>([]);
  const [loading, setLoading] = useState(true);
  const [uploading, setUploading] = useState(false);
  const [dragOver, setDragOver] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const loadAttachments = useCallback(async () => {
    try {
      const res = await fetchWithAuth(`/api/articles/${articleId}/attachments`);
      if (res.ok) {
        const data: AttachmentListResponse = await res.json();
        setAttachments(data.attachments);
      }
    } catch {
      // silent fail
    } finally {
      setLoading(false);
    }
  }, [fetchWithAuth, articleId]);

  useEffect(() => {
    loadAttachments();
  }, [loadAttachments]);

  const uploadFiles = async (files: File[]) => {
    if (files.length === 0) return;
    setUploading(true);
    let successCount = 0;
    let lastError = "";

    for (const file of files) {
      try {
        const formData = new FormData();
        formData.append("file", file);

        const res = await fetchWithAuth(`/api/articles/${articleId}/attachments`, {
          method: "POST",
          body: formData,
        });

        if (res.ok) {
          successCount++;
        } else {
          const err = await res.json();
          lastError = err.error || "Upload failed";
        }
      } catch {
        lastError = "Upload failed";
      }
    }

    if (successCount > 0) {
      toast.success(successCount === 1 ? "File uploaded successfully" : `${successCount} files uploaded successfully`);
      await loadAttachments();
    }
    if (lastError && successCount < files.length) {
      const failCount = files.length - successCount;
      toast.error(failCount === 1 ? lastError : `${failCount} files failed: ${lastError}`);
    }

    setUploading(false);
    if (fileInputRef.current) fileInputRef.current.value = "";
  };

  const handleUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = e.target.files;
    if (!files || files.length === 0) return;
    await uploadFiles(Array.from(files));
  };

  const handleDrop = async (e: React.DragEvent) => {
    e.preventDefault();
    setDragOver(false);
    if (!canEdit || uploading) return;
    const files = Array.from(e.dataTransfer.files);
    if (files.length === 0) return;
    // If deferred mode (onAddFiles), queue files; otherwise upload immediately
    if (onAddFiles) {
      onAddFiles(files);
    } else {
      await uploadFiles(files);
    }
  };

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault();
    if (canEdit && !uploading) setDragOver(true);
  };

  const handleDragLeave = (e: React.DragEvent) => {
    e.preventDefault();
    setDragOver(false);
  };

  const handleDelete = async (attachment: ArticleAttachment) => {
    if (onDeferredDelete) {
      // Deferred mode: just notify parent, don't call API
      onDeferredDelete(attachment);
      return;
    }

    if (!confirm(`Delete "${attachment.fileName}"?`)) return;

    try {
      const res = await fetchWithAuth(`/api/articles/${articleId}/attachments/${attachment.id}`, {
        method: "DELETE",
      });

      if (res.ok) {
        toast.success("Attachment deleted");
        setAttachments((prev) => prev.filter((a) => a.id !== attachment.id));
      } else {
        const err = await res.json();
        toast.error(err.error || "Delete failed");
      }
    } catch {
      toast.error("Delete failed");
    }
  };

  const handleDownload = async (attachment: ArticleAttachment) => {
    try {
      const res = await fetchWithAuth(attachment.downloadUrl);
      if (!res.ok) {
        toast.error("Download failed");
        return;
      }
      const blob = await res.blob();
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = attachment.fileName;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    } catch {
      toast.error("Download failed");
    }
  };

  if (loading) {
    return (
      <div className="flex items-center gap-2 text-zinc-500 text-sm py-4">
        <Loader2 size={14} className="animate-spin" />
        Loading attachments...
      </div>
    );
  }

  const visibleAttachments = attachments;
  const totalCount = visibleAttachments.length + (pendingFiles?.length || 0);

  if (totalCount === 0 && !canEdit) return null;

  return (
    <div
      className={cn(
        "mt-6 border rounded-xl overflow-hidden transition-colors",
        dragOver
          ? "border-blue-400 dark:border-blue-500 bg-blue-50/50 dark:bg-blue-900/10"
          : "border-zinc-200 dark:border-zinc-800"
      )}
      onDrop={handleDrop}
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
    >
      <div className="flex items-center justify-between px-4 py-3 bg-zinc-50 dark:bg-zinc-900 border-b border-zinc-200 dark:border-zinc-800">
        <h3 className="text-sm font-medium text-zinc-700 dark:text-zinc-300">
          Attachments {totalCount > 0 && `(${totalCount})`}
        </h3>
        {canEdit && !hideUpload && (
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
              onChange={handleUpload}
              className="hidden"
              disabled={uploading}
            />
          </label>
        )}
        {canEdit && onAddFiles && (
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
              onChange={(e) => { onAddFiles(Array.from(e.target.files || [])); e.target.value = ""; }}
              className="hidden"
            />
          </label>
        )}
      </div>

      {totalCount === 0 ? (
        <div className={cn(
          "px-4 py-6 text-center text-sm",
          dragOver ? "text-blue-500" : "text-zinc-400"
        )}>
          {dragOver
            ? "Drop files here to add"
            : canEdit
              ? "No attachments yet. Drag & drop files here or click Add Files."
              : "No attachments yet."}
        </div>
      ) : (
        <ul className="divide-y divide-zinc-100 dark:divide-zinc-800">
          {visibleAttachments.map((attachment) => {
            const isDeleted = deletedIds?.has(attachment.id);
            return (
            <li key={attachment.id} className={cn(
              "flex items-center gap-3 px-4 py-2.5 transition-colors",
              isDeleted
                ? "bg-red-50/50 dark:bg-red-950/20 opacity-60"
                : "hover:bg-zinc-50 dark:hover:bg-zinc-900/50"
            )}>
              {getFileIcon(attachment.contentType)}
              <span className={cn(
                "flex-1 text-sm truncate",
                isDeleted
                  ? "line-through text-zinc-400 dark:text-zinc-500"
                  : "text-zinc-700 dark:text-zinc-300"
              )} title={attachment.fileName}>
                {attachment.fileName}
              </span>
              {isDeleted && (
                <span className="inline-flex items-center gap-1 px-1.5 py-0.5 text-[10px] font-medium rounded bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-400">
                  Kaydedilince silinecek
                </span>
              )}
              <span className="text-xs text-zinc-400 whitespace-nowrap">
                {formatFileSize(attachment.sizeBytes)}
              </span>
              {!isDeleted && (
                <button
                  onClick={() => handleDownload(attachment)}
                  className="p-1 rounded hover:bg-zinc-200 dark:hover:bg-zinc-700 transition-colors"
                  title="Download"
                >
                  <Download size={14} className="text-zinc-500" />
                </button>
              )}
              {canEdit && !isDeleted && (
                <button
                  onClick={() => handleDelete(attachment)}
                  className="p-1 rounded hover:bg-red-100 dark:hover:bg-red-900/30 transition-colors"
                  title="Delete"
                >
                  <Trash2 size={14} className="text-red-500" />
                </button>
              )}
              {canEdit && isDeleted && onUndoDelete && (
                <button
                  onClick={() => onUndoDelete(attachment.id)}
                  className="p-1 rounded hover:bg-zinc-200 dark:hover:bg-zinc-700 transition-colors"
                  title="Geri al"
                >
                  <Undo2 size={14} className="text-zinc-600 dark:text-zinc-400" />
                </button>
              )}
            </li>
            );
          })}
          {pendingFiles && pendingFiles.map((file, index) => (
            <li key={`pending-${file.name}-${index}`} className="flex items-center gap-3 px-4 py-2.5 bg-emerald-50/50 dark:bg-emerald-950/20 hover:bg-emerald-50 dark:hover:bg-emerald-950/30 transition-colors">
              {getFileIcon(file.type || "application/octet-stream")}
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
              {onRemovePendingFile && (
                <button
                  onClick={() => onRemovePendingFile(index)}
                  className="p-1 rounded hover:bg-red-100 dark:hover:bg-red-900/30 transition-colors"
                  title="Remove"
                >
                  <Trash2 size={14} className="text-red-500" />
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
