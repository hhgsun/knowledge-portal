import { lazy, Suspense, useState, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { Save, ArrowLeft, Tag, X } from "lucide-react";
import { TagSelector } from "./tag-selector";
import { ContentTypeSelect } from "./content-type-select";
import { useLookups } from "../../hooks/useLookups";
import { useAutoResizeTextArea } from "../../hooks/useAutoResizeTextArea";

const MilkdownEditor = lazy(() => import("./milkdown-editor"));

const STATUS_DESCRIPTIONS: Record<string, string> = {
  draft: "Henüz yayımlanmadı",
  published: "Okuyuculara açık",
  archived: "Yayından kaldırıldı",
};

export interface ArticleFormProps {
  mode: "create" | "edit";
  title: string;
  onTitleChange: (v: string) => void;
  contentMarkdown: string;
  onContentMarkdownChange: (v: string) => void;
  excerpt: string;
  onExcerptChange: (v: string) => void;
  contentType: string;
  onContentTypeChange: (v: string) => void;
  status: string;
  onStatusChange: (v: string) => void;
  tags: string[];
  onTagsChange: (v: string[]) => void;
  saving: boolean;
  error: string;
  onSave: () => void;
  isViewer: boolean;
  backLink: string;
  // Editor props
  articleId?: string;
  uploadImage: (file: File) => Promise<string | null>;
  deleteImage: (src: string) => Promise<void>;
  // Edit-specific
  changeSummary?: string;
  onChangeSummaryChange?: (v: string) => void;
  // Attachment section rendered by parent
  attachmentSection?: ReactNode;
}

export function ArticleForm({
  mode,
  title,
  onTitleChange,
  contentMarkdown,
  onContentMarkdownChange,
  excerpt,
  onExcerptChange,
  contentType,
  onContentTypeChange,
  status,
  onStatusChange,
  tags,
  onTagsChange,
  saving,
  error,
  onSave,
  isViewer,
  backLink,
  articleId,
  uploadImage,
  deleteImage,
  changeSummary,
  onChangeSummaryChange,
  attachmentSection,
}: ArticleFormProps) {
  const { contentTypes } = useLookups();
  const titleRef = useAutoResizeTextArea(title);
  const excerptRef = useAutoResizeTextArea(excerpt);
  const [showSaveDialog, setShowSaveDialog] = useState(false);

  const requestSave = () => {
    if (mode === "edit" && onChangeSummaryChange && title.trim()) {
      setShowSaveDialog(true);
      return;
    }
    onSave();
  };

  return (
    <div className="max-w-5xl mx-auto">
      <div className="mb-6">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <textarea
            ref={titleRef}
            rows={1}
            value={title}
            onChange={(e) => onTitleChange(e.target.value.replace(/\r?\n/g, " "))}
            placeholder="Makale başlığı..."
            aria-label="Makale başlığı"
            maxLength={300}
            className="min-w-0 flex-1 resize-none overflow-hidden bg-transparent text-3xl font-bold leading-tight text-zinc-900 outline-none placeholder:text-zinc-300 dark:text-zinc-100 dark:placeholder:text-zinc-600"
          />
          <div className="flex shrink-0 items-center gap-2 self-end sm:self-auto">
            <Link
              to={backLink}
              className="flex items-center gap-1 rounded-lg border border-zinc-300 px-3 py-2 text-sm transition-colors hover:bg-zinc-50 dark:border-zinc-700 dark:hover:bg-zinc-800"
            >
              <ArrowLeft size={14} />
              İptal
            </Link>
            <button
              type="button"
              onClick={requestSave}
              disabled={saving}
              className="flex items-center gap-2 px-4 py-2 bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white text-sm font-medium rounded-lg transition-colors"
            >
              <Save size={16} />
              {saving ? "Saving..." : mode === "create" ? "Save" : "Değişiklikleri Kaydet"}
            </button>
          </div>
        </div>

        <textarea
          ref={excerptRef}
          rows={1}
          value={excerpt}
          onChange={(e) => onExcerptChange(e.target.value.replace(/\r?\n/g, " "))}
          placeholder="Kısa açıklama (isteğe bağlı)..."
          aria-label="Kısa açıklama"
          className="mt-2 block w-full resize-none overflow-hidden bg-transparent text-base leading-relaxed text-zinc-500 outline-none placeholder:text-zinc-400 dark:text-zinc-400"
        />

        <div className="mt-4 flex flex-wrap items-center gap-3 text-sm text-zinc-500">
          <ContentTypeSelect
            options={contentTypes}
            value={contentType}
            onChange={onContentTypeChange}
          />
          <div className="inline-flex min-w-0 items-center gap-2">
            <label>
              <span className="sr-only">Yayın durumu</span>
              <select
                value={status}
                onChange={(e) => onStatusChange(e.target.value)}
                aria-describedby="article-status-description"
                className="rounded-md border border-zinc-300 bg-white px-2 py-1 text-xs font-medium text-zinc-700 outline-none transition-colors focus:border-blue-500 focus:ring-2 focus:ring-blue-500/20 dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-200"
              >
                <option value="draft">Taslak</option>
                <option value="published">Yayımlandı</option>
                {!isViewer && mode === "edit" && <option value="archived">Arşivlendi</option>}
              </select>
            </label>
            <span id="article-status-description" className="text-xs text-zinc-400 dark:text-zinc-500">
              {STATUS_DESCRIPTIONS[status] ?? ""}
            </span>
          </div>
        </div>

        <div className="mt-3 flex items-start gap-2">
          <Tag size={14} className="mt-2 shrink-0 text-zinc-400" />
          <TagSelector selectedTags={tags} onChange={onTagsChange} />
        </div>
      </div>

      {error && (
        <div className="mb-4 p-3 bg-red-50 dark:bg-red-950 border border-red-200 dark:border-red-800 rounded-lg text-sm text-red-600 dark:text-red-400">
          {error}
        </div>
      )}

      <div className="xl:-mx-24">
        <Suspense fallback={<div className="h-64 bg-zinc-50 dark:bg-zinc-900 rounded-lg animate-pulse" />}>
          <MilkdownEditor
            contentMarkdown={contentMarkdown}
            onChange={onContentMarkdownChange}
            articleId={articleId}
            uploadImage={uploadImage}
            deleteImage={deleteImage}
          />
        </Suspense>

        {attachmentSection}
      </div>

      {showSaveDialog && onChangeSummaryChange && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
          <button
            type="button"
            tabIndex={-1}
            aria-label="Kayıt penceresini kapat"
            onClick={() => { if (!saving) setShowSaveDialog(false); }}
            className="absolute inset-0 bg-zinc-950/45 backdrop-blur-[2px]"
          />
          <form
            role="dialog"
            aria-modal="true"
            aria-labelledby="save-dialog-title"
            onSubmit={(event) => {
              event.preventDefault();
              if (!saving) onSave();
            }}
            onKeyDown={(event) => {
              if (event.key === "Escape" && !saving) setShowSaveDialog(false);
            }}
            className="relative w-full max-w-md rounded-2xl border border-zinc-200 bg-white p-5 shadow-2xl dark:border-zinc-700 dark:bg-zinc-900"
          >
            <div className="flex items-start justify-between gap-4">
              <div>
                <h2 id="save-dialog-title" className="text-base font-semibold text-zinc-900 dark:text-zinc-100">
                  Değişiklikleri kaydet
                </h2>
                <p className="mt-1 text-sm text-zinc-500 dark:text-zinc-400">
                  Bu sürümde nelerin değiştiğini kısaca belirtin.
                </p>
              </div>
              <button
                type="button"
                onClick={() => setShowSaveDialog(false)}
                disabled={saving}
                aria-label="Kapat"
                className="rounded-lg p-1.5 text-zinc-400 transition-colors hover:bg-zinc-100 hover:text-zinc-700 disabled:opacity-50 dark:hover:bg-zinc-800 dark:hover:text-zinc-200"
              >
                <X size={17} />
              </button>
            </div>

            <label htmlFor="article-change-summary" className="mb-1.5 mt-5 block text-xs font-medium text-zinc-600 dark:text-zinc-400">
              Değişiklik özeti <span className="font-normal text-zinc-400">(isteğe bağlı)</span>
            </label>
            <input
              id="article-change-summary"
              type="text"
              autoFocus
              value={changeSummary ?? ""}
              onChange={(e) => onChangeSummaryChange(e.target.value)}
              placeholder="Örn. Yazım hataları düzeltildi, yeni bölüm eklendi..."
              className="w-full rounded-lg border border-zinc-300 bg-white px-3 py-2.5 text-sm outline-none placeholder:text-zinc-400 focus:border-blue-500 focus:ring-2 focus:ring-blue-500/20 dark:border-zinc-700 dark:bg-zinc-950"
            />

            {error && (
              <p className="mt-2 text-xs text-red-600 dark:text-red-400">{error}</p>
            )}

            <div className="mt-5 flex justify-end gap-2">
              <button
                type="button"
                onClick={() => setShowSaveDialog(false)}
                disabled={saving}
                className="rounded-lg border border-zinc-300 px-3.5 py-2 text-sm font-medium text-zinc-700 transition-colors hover:bg-zinc-50 disabled:opacity-50 dark:border-zinc-700 dark:text-zinc-200 dark:hover:bg-zinc-800"
              >
                Vazgeç
              </button>
              <button
                type="submit"
                disabled={saving}
                className="flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-blue-700 disabled:opacity-50"
              >
                <Save size={15} />
                {saving ? "Kaydediliyor..." : "Kaydet"}
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
}
