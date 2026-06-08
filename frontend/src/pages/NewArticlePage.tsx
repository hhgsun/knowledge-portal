import { useState, useCallback, useRef, lazy, Suspense } from "react";
import { useNavigate, Link } from "react-router-dom";
import { Save, ArrowLeft, Send } from "lucide-react";
import { TagSelector } from "../components/editor/tag-selector";
import { useApi } from "../hooks/useApi";
import { useAuth } from "../contexts/AuthContext";
import { toast } from "sonner";
import AttachmentList from "../components/attachments/attachment-list";
import FileUploadZone from "../components/attachments/file-upload-zone";

const TiptapEditor = lazy(() => import("../components/editor/tiptap-editor"));

export default function NewArticlePage() {
  const navigate = useNavigate();
  const { fetchWithAuth } = useApi();
  const { user } = useAuth();
  const isViewer = user?.role === "viewer";
  const [title, setTitle] = useState("");
  const [content, setContent] = useState<Record<string, unknown> | null>(null);
  const [excerpt, setExcerpt] = useState("");
  const [contentType, setContentType] = useState("reference");
  const [difficulty, setDifficulty] = useState("beginner");
  const [status, setStatus] = useState("draft");
  const [tags, setTags] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [articleId, setArticleId] = useState<string | null>(null);
  const [articleSlug, setArticleSlug] = useState<string | null>(null);
  const contentRef = useRef(content);
  contentRef.current = content;
  const articleIdRef = useRef<string | null>(null);

  // Auto-save as draft and return article ID (for upload support)
  // Does NOT set articleId state — callers decide when to trigger render switch
  const ensureArticleSaved = useCallback(async (): Promise<string | null> => {
    if (articleIdRef.current) return articleIdRef.current;

    if (!title.trim()) {
      toast.error("Please enter a title before uploading files");
      return null;
    }

    const res = await fetchWithAuth("/api/articles", {
      method: "POST",
      body: JSON.stringify({
        title: title.trim(),
        content: contentRef.current,
        excerpt: excerpt.trim() || undefined,
        contentType,
        difficulty,
        status: "draft",
        tags,
      }),
    });

    if (res.ok) {
      const article = await res.json();
      articleIdRef.current = article.id;
      setArticleSlug(article.slug);
      toast.success("Article auto-saved as draft");
      return article.id;
    } else {
      const data = await res.json();
      toast.error(data.error || "Failed to auto-save article");
      return null;
    }
  }, [title, excerpt, contentType, difficulty, tags, fetchWithAuth]);

  const uploadImage = useCallback(async (file: File): Promise<string | null> => {
    const id = await ensureArticleSaved();
    if (!id) return null;

    const formData = new FormData();
    formData.append("file", file);
    try {
      const res = await fetchWithAuth(`/api/articles/${id}/attachments`, {
        method: "POST",
        body: formData,
      });
      if (res.ok) {
        const data = await res.json();
        setArticleId(id);
        return data.downloadUrl;
      } else {
        const err = await res.json();
        toast.error(err.error || "Image upload failed");
        return null;
      }
    } catch {
      toast.error("Image upload failed");
      return null;
    }
  }, [ensureArticleSaved, fetchWithAuth]);

  const deleteImage = useCallback(async (src: string) => {
    const id = articleIdRef.current;
    if (!id) return;
    const match = src.match(/\/api\/attachments\/([^/]+)\/download/);
    if (!match) return;
    const attachmentId = match[1];
    try {
      await fetchWithAuth(`/api/articles/${id}/attachments/${attachmentId}`, {
        method: "DELETE",
      });
    } catch {
      // Silent fail
    }
  }, [fetchWithAuth]);

  const handleFileUpload = useCallback(async (files: File[]) => {
    const id = await ensureArticleSaved();
    if (!id) return;

    let successCount = 0;
    let lastError = "";
    for (const file of files) {
      const formData = new FormData();
      formData.append("file", file);
      try {
        const res = await fetchWithAuth(`/api/articles/${id}/attachments`, {
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
      toast.success(successCount === 1 ? "File uploaded" : `${successCount} files uploaded`);
      setArticleId(id); // Switch to AttachmentList AFTER uploads complete
    }
    if (lastError && successCount < files.length) {
      toast.error(lastError);
    }
  }, [ensureArticleSaved, fetchWithAuth]);

  const handleSave = async () => {
    if (!title.trim()) {
      setError("Title is required");
      return;
    }

    setSaving(true);
    setError("");

    // If article was auto-saved (for upload), update it instead of creating
    if (articleIdRef.current) {
      const res = await fetchWithAuth(`/api/articles/${articleIdRef.current}`, {
        method: "PUT",
        body: JSON.stringify({
          title: title.trim(),
          content,
          excerpt: excerpt.trim() || undefined,
          contentType,
          difficulty,
          status,
          tags,
        }),
      });

      if (res.ok) {
        const updated = await res.json();
        toast.success("Article saved successfully");
        navigate(`/articles/${updated.slug}`);
      } else {
        const data = await res.json();
        setError(data.error || "Failed to save article");
        setSaving(false);
      }
      return;
    }

    const res = await fetchWithAuth("/api/articles", {
      method: "POST",
      body: JSON.stringify({
        title: title.trim(),
        content,
        excerpt: excerpt.trim() || undefined,
        contentType,
        difficulty,
        status,
        tags,
      }),
    });

    if (res.ok) {
      const article = await res.json();
      toast.success("Article created successfully");
      navigate(`/articles/${article.slug}`);
    } else {
      const data = await res.json();
      setError(data.error || "Failed to save article");
      setSaving(false);
    }
  };

  return (
    <div className="max-w-4xl mx-auto">
      <div className="flex items-center justify-between mb-6">
        <div className="flex items-center gap-3">
          <Link to="/articles" className="p-2 rounded-lg hover:bg-zinc-100 dark:hover:bg-zinc-800">
            <ArrowLeft size={18} />
          </Link>
          <h1 className="text-xl font-bold text-zinc-900 dark:text-zinc-100">New Article</h1>
        </div>
        <div className="flex items-center gap-2">
          {isViewer && (
            <button
              onClick={() => { setStatus("pending"); handleSave(); }}
              disabled={saving}
              className="flex items-center gap-2 px-4 py-2 bg-amber-500 hover:bg-amber-600 disabled:opacity-50 text-white text-sm font-medium rounded-lg transition-colors"
            >
              <Send size={16} />
              {saving ? "Submitting..." : "Submit for Review"}
            </button>
          )}
          <button
            onClick={handleSave}
            disabled={saving}
            className="flex items-center gap-2 px-4 py-2 bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white text-sm font-medium rounded-lg transition-colors"
          >
            <Save size={16} />
            {saving ? "Saving..." : "Save"}
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
            onChange={(e) => setTitle(e.target.value)}
            placeholder="Article title..."
            className="w-full text-2xl font-bold bg-transparent border-none outline-none placeholder:text-zinc-300 dark:placeholder:text-zinc-600 text-zinc-900 dark:text-zinc-100"
          />
        </div>

        <div>
          <input
            type="text"
            value={excerpt}
            onChange={(e) => setExcerpt(e.target.value)}
            placeholder="Brief description (optional)..."
            className="w-full text-sm bg-transparent border-none outline-none placeholder:text-zinc-400 text-zinc-600 dark:text-zinc-400"
          />
        </div>

        <div className="flex flex-wrap gap-3 pb-4 border-b border-zinc-200 dark:border-zinc-800">
          <select value={contentType} onChange={(e) => setContentType(e.target.value)} className="px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800">
            <option value="reference">Reference</option>
            <option value="how-to">How-To Guide</option>
            <option value="adr">ADR</option>
            <option value="runbook">Runbook</option>
            <option value="faq">FAQ</option>
            <option value="policy">Policy</option>
            <option value="onboarding">Onboarding</option>
          </select>
          <select value={difficulty} onChange={(e) => setDifficulty(e.target.value)} className="px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800">
            <option value="beginner">Beginner</option>
            <option value="intermediate">Intermediate</option>
            <option value="advanced">Advanced</option>
          </select>
          <select value={status} onChange={(e) => setStatus(e.target.value)} className="px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800">
            <option value="draft">Draft</option>
            {isViewer ? (
              <option value="pending">Pending Review</option>
            ) : (
              <>
                <option value="pending">Pending Review</option>
                <option value="published">Published</option>
              </>
            )}
          </select>
        </div>

        <div className="pb-4 border-b border-zinc-200 dark:border-zinc-800">
          <label className="text-xs font-medium text-zinc-500 mb-1.5 block">Tags</label>
          <TagSelector selectedTags={tags} onChange={setTags} />
        </div>

        <Suspense fallback={<div className="h-64 bg-zinc-50 dark:bg-zinc-900 rounded-lg animate-pulse" />}>
          <TiptapEditor content={content} onChange={(json) => setContent(json)} articleId={articleId ?? undefined} uploadImage={uploadImage} deleteImage={deleteImage} />
        </Suspense>

        {articleId ? (
          <AttachmentList articleId={articleId} canEdit={true} />
        ) : (
          <FileUploadZone onUpload={handleFileUpload} />
        )}
      </div>
    </div>
  );
}
