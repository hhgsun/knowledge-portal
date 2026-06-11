import { lazy, Suspense, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { Save, ArrowLeft, Send } from "lucide-react";
import { TagSelector } from "./tag-selector";
import { useLookups } from "../../hooks/useLookups";

const TiptapEditor = lazy(() => import("./tiptap-editor"));

export interface ArticleFormProps {
  mode: "create" | "edit";
  title: string;
  onTitleChange: (v: string) => void;
  content: Record<string, unknown> | null;
  onContentChange: (v: Record<string, unknown>) => void;
  excerpt: string;
  onExcerptChange: (v: string) => void;
  contentType: string;
  onContentTypeChange: (v: string) => void;
  difficulty: string;
  onDifficultyChange: (v: string) => void;
  status: string;
  onStatusChange: (v: string) => void;
  tags: string[];
  onTagsChange: (v: string[]) => void;
  saving: boolean;
  error: string;
  onSave: () => void;
  onSubmitForReview?: () => void;
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
  content,
  onContentChange,
  excerpt,
  onExcerptChange,
  contentType,
  onContentTypeChange,
  difficulty,
  onDifficultyChange,
  status,
  onStatusChange,
  tags,
  onTagsChange,
  saving,
  error,
  onSave,
  onSubmitForReview,
  isViewer,
  backLink,
  articleId,
  uploadImage,
  deleteImage,
  changeSummary,
  onChangeSummaryChange,
  attachmentSection,
}: ArticleFormProps) {
  const { contentTypes, difficulties } = useLookups();

  return (
    <div className="max-w-4xl mx-auto">
      <div className="flex items-center justify-between mb-6">
        <div className="flex items-center gap-3">
          <Link to={backLink} className="p-2 rounded-lg hover:bg-zinc-100 dark:hover:bg-zinc-800">
            <ArrowLeft size={18} />
          </Link>
          <h1 className="text-xl font-bold text-zinc-900 dark:text-zinc-100">
            {mode === "create" ? "New Article" : "Edit Article"}
          </h1>
        </div>
        <div className="flex items-center gap-2">
          {isViewer && onSubmitForReview && (
            <button
              onClick={onSubmitForReview}
              disabled={saving}
              className="flex items-center gap-2 px-4 py-2 bg-amber-500 hover:bg-amber-600 disabled:opacity-50 text-white text-sm font-medium rounded-lg transition-colors"
            >
              <Send size={16} />
              {saving ? "Submitting..." : "Submit for Review"}
            </button>
          )}
          <button
            onClick={onSave}
            disabled={saving}
            className="flex items-center gap-2 px-4 py-2 bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white text-sm font-medium rounded-lg transition-colors"
          >
            <Save size={16} />
            {saving ? "Saving..." : mode === "create" ? "Save" : "Save Changes"}
          </button>
        </div>
      </div>

      {error && (
        <div className="mb-4 p-3 bg-red-50 dark:bg-red-950 border border-red-200 dark:border-red-800 rounded-lg text-sm text-red-600 dark:text-red-400">
          {error}
        </div>
      )}

      <div className="space-y-4">
        <div>
          <input
            type="text"
            value={title}
            onChange={(e) => onTitleChange(e.target.value)}
            placeholder="Article title..."
            className="w-full text-2xl font-bold bg-transparent border-none outline-none placeholder:text-zinc-300 dark:placeholder:text-zinc-600 text-zinc-900 dark:text-zinc-100"
          />
        </div>

        <div>
          <input
            type="text"
            value={excerpt}
            onChange={(e) => onExcerptChange(e.target.value)}
            placeholder="Brief description (optional)..."
            className="w-full text-sm bg-transparent border-none outline-none placeholder:text-zinc-400 text-zinc-600 dark:text-zinc-400"
          />
        </div>

        <div className="flex flex-wrap gap-3 pb-4 border-b border-zinc-200 dark:border-zinc-800">
          <select value={contentType} onChange={(e) => onContentTypeChange(e.target.value)} className="px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800">
            {contentTypes.map((ct) => (
              <option key={ct.value} value={ct.value}>{ct.label}</option>
            ))}
          </select>
          <select value={difficulty} onChange={(e) => onDifficultyChange(e.target.value)} className="px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800">
            {difficulties.map((d) => (
              <option key={d.value} value={d.value}>{d.label}</option>
            ))}
          </select>
          <select value={status} onChange={(e) => onStatusChange(e.target.value)} className="px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800">
            <option value="draft">Draft</option>
            {isViewer ? (
              <option value="pending">Pending Review</option>
            ) : (
              <>
                <option value="pending">Pending Review</option>
                <option value="published">Published</option>
                {mode === "edit" && <option value="archived">Archived</option>}
              </>
            )}
          </select>
        </div>

        <div className="pb-4 border-b border-zinc-200 dark:border-zinc-800">
          <label className="text-xs font-medium text-zinc-500 mb-1.5 block">Tags</label>
          <TagSelector selectedTags={tags} onChange={onTagsChange} />
        </div>

        {mode === "edit" && onChangeSummaryChange && (
          <div>
            <input
              type="text"
              value={changeSummary ?? ""}
              onChange={(e) => onChangeSummaryChange(e.target.value)}
              placeholder="Change summary (e.g., 'Fixed typos', 'Added new section')..."
              className="w-full text-sm px-3 py-2 bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded-lg placeholder:text-zinc-400"
            />
          </div>
        )}

        <Suspense fallback={<div className="h-64 bg-zinc-50 dark:bg-zinc-900 rounded-lg animate-pulse" />}>
          <TiptapEditor
            content={content}
            onChange={(json) => onContentChange(json)}
            articleId={articleId}
            uploadImage={uploadImage}
            deleteImage={deleteImage}
            deferredUpload={true}
          />
        </Suspense>

        {attachmentSection}
      </div>
    </div>
  );
}
