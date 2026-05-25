import { useState, useEffect, lazy, Suspense } from "react";
import { useParams, useNavigate, Link } from "react-router-dom";
import { Save, ArrowLeft, Loader2, Send } from "lucide-react";
import { TagSelector } from "../components/editor/tag-selector";
import { useApi } from "../hooks/useApi";
import { useAuth } from "../contexts/AuthContext";

const TiptapEditor = lazy(() => import("../components/editor/tiptap-editor"));

interface Article {
  id: string;
  title: string;
  slug: string;
  content: Record<string, unknown> | null;
  excerpt: string | null;
  status: string;
  contentType: string;
  difficulty: string;
  audience: string | null;
}

export default function EditArticlePage() {
  const params = useParams();
  const navigate = useNavigate();
  const { fetchWithAuth } = useApi();
  const { user } = useAuth();
  const isViewer = user?.role === "viewer";
  const [article, setArticle] = useState<Article | null>(null);
  const [title, setTitle] = useState("");
  const [content, setContent] = useState<Record<string, unknown> | null>(null);
  const [excerpt, setExcerpt] = useState("");
  const [contentType, setContentType] = useState("reference");
  const [difficulty, setDifficulty] = useState("beginner");
  const [status, setStatus] = useState("draft");
  const [changeSummary, setChangeSummary] = useState("");
  const [tags, setTags] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    if (params.slug) {
      fetchWithAuth(`/api/articles/${params.slug}`)
        .then((res) => res.json())
        .then((data) => {
          if (data.error) {
            setError("Article not found");
          } else {
            setArticle(data);
            setTitle(data.title);
            setContent(data.content);
            setExcerpt(data.excerpt || "");
            setContentType(data.contentType);
            setDifficulty(data.difficulty);
            setStatus(data.status);
            setTags((data.tags || []).map((t: { id: string }) => t.id));
          }
          setLoading(false);
        })
        .catch(() => {
          setError("Failed to load article");
          setLoading(false);
        });
    }
  }, [params.slug, fetchWithAuth]);

  const handleSave = async () => {
    if (!title.trim()) {
      setError("Title is required");
      return;
    }
    if (!article) return;

    setSaving(true);
    setError("");

    const res = await fetchWithAuth(`/api/articles/${article.id}`, {
      method: "PUT",
      body: JSON.stringify({
        title: title.trim(),
        content,
        excerpt: excerpt.trim() || undefined,
        contentType,
        difficulty,
        status,
        changeSummary: changeSummary.trim() || undefined,
        tags,
      }),
    });

    if (res.ok) {
      const updated = await res.json();
      navigate(`/articles/${updated.slug}`);
    } else {
      const data = await res.json();
      setError(data.error || "Failed to save article");
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center py-12">
        <Loader2 size={24} className="animate-spin text-zinc-400" />
      </div>
    );
  }

  if (!article) {
    return (
      <div className="text-center py-12">
        <p className="text-zinc-500">{error || "Article not found"}</p>
        <Link to="/articles" className="text-blue-600 text-sm mt-2 inline-block">
          Back to articles
        </Link>
      </div>
    );
  }

  return (
    <div className="max-w-4xl mx-auto">
      <div className="flex items-center justify-between mb-6">
        <div className="flex items-center gap-3">
          <Link to={`/articles/${article.slug}`} className="p-2 rounded-lg hover:bg-zinc-100 dark:hover:bg-zinc-800">
            <ArrowLeft size={18} />
          </Link>
          <h1 className="text-xl font-bold text-zinc-900 dark:text-zinc-100">Edit Article</h1>
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
            {saving ? "Saving..." : "Save Changes"}
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
                <option value="in_review">In Review</option>
                <option value="published">Published</option>
                <option value="archived">Archived</option>
              </>
            )}
          </select>
        </div>

        <div className="pb-4 border-b border-zinc-200 dark:border-zinc-800">
          <label className="text-xs font-medium text-zinc-500 mb-1.5 block">Tags</label>
          <TagSelector selectedTags={tags} onChange={setTags} />
        </div>

        <div>
          <input
            type="text"
            value={changeSummary}
            onChange={(e) => setChangeSummary(e.target.value)}
            placeholder="Change summary (e.g., 'Fixed typos', 'Added new section')..."
            className="w-full text-sm px-3 py-2 bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded-lg placeholder:text-zinc-400"
          />
        </div>

        <Suspense fallback={<div className="h-64 bg-zinc-50 dark:bg-zinc-900 rounded-lg animate-pulse" />}>
          <TiptapEditor content={content} onChange={(json) => setContent(json)} />
        </Suspense>
      </div>
    </div>
  );
}
