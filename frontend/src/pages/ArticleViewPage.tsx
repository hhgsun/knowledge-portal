import { useEffect, useState } from "react";
import { useParams, Link } from "react-router-dom";
import { ArrowLeft, Edit, Clock, User, Tag, Key, ThumbsUp, ThumbsDown, CheckCircle, XCircle } from "lucide-react";
import { TiptapRenderer } from "../components/editor/tiptap-renderer";
import { useApi } from "../hooks/useApi";
import { useAuth } from "../contexts/AuthContext";
import { toast } from "sonner";
import type { Article } from "../types/api";

export default function ArticleViewPage() {
  const params = useParams();
  const { fetchWithAuth } = useApi();
  const { user } = useAuth();
  const [article, setArticle] = useState<Article | null>(null);
  const [loading, setLoading] = useState(true);
  const [actionLoading, setActionLoading] = useState(false);

  const isApprover = user?.role === "admin" || user?.role === "editor";

  useEffect(() => {
    if (params.slug) {
      fetchWithAuth(`/api/articles/${params.slug}`)
        .then((res) => res.json())
        .then((data) => {
          if (data.error) {
            setArticle(null);
          } else {
            setArticle(data);
          }
          setLoading(false);
        })
        .catch(() => setLoading(false));
    }
  }, [params.slug, fetchWithAuth]);

  const handleFeedback = async (helpful: boolean) => {
    if (!article) return;
    const res = await fetchWithAuth(`/api/articles/${article.id}/feedback`, {
      method: "POST",
      body: JSON.stringify({ helpful }),
    });
    if (res.ok) {
      toast.success("Thanks for your feedback!");
    } else {
      toast.error("Failed to submit feedback");
    }
  };

  const handleApprove = async () => {
    if (!article) return;
    setActionLoading(true);
    const res = await fetchWithAuth(`/api/articles/${article.id}/approve`, { method: "POST" });
    if (res.ok) {
      setArticle({ ...article, status: "published" });
      toast.success("Article approved and published");
    } else {
      const data = await res.json().catch(() => ({}));
      toast.error(data.error || "Failed to approve article");
    }
    setActionLoading(false);
  };

  const handleReject = async () => {
    if (!article) return;
    setActionLoading(true);
    const res = await fetchWithAuth(`/api/articles/${article.id}/reject`, { method: "POST" });
    if (res.ok) {
      setArticle({ ...article, status: "draft" });
      toast.success("Article rejected and returned to draft");
    } else {
      const data = await res.json().catch(() => ({}));
      toast.error(data.error || "Failed to reject article");
    }
    setActionLoading(false);
  };

  if (loading) {
    return <div className="text-center py-12 text-zinc-500">Loading...</div>;
  }

  if (!article) {
    return (
      <div className="text-center py-12">
        <p className="text-zinc-500">Article not found</p>
        <Link to="/articles" className="text-blue-600 hover:underline text-sm mt-2 inline-block">
          Back to articles
        </Link>
      </div>
    );
  }

  return (
    <div className="max-w-4xl mx-auto">
      <div className="flex items-center gap-2 mb-6">
        <Link to="/articles" className="flex items-center gap-1 text-sm text-zinc-500 hover:text-zinc-700">
          <ArrowLeft size={14} />
          Articles
        </Link>
        <span className="text-zinc-300">/</span>
        <span className="text-sm text-zinc-700 dark:text-zinc-300">{article.title}</span>
      </div>

      {article.status === "pending" && (
        <div className="mb-6 p-4 bg-amber-50 dark:bg-amber-950 border border-amber-200 dark:border-amber-800 rounded-xl">
          <div className="flex items-center justify-between">
            <div>
              <p className="text-sm font-medium text-amber-800 dark:text-amber-200">Pending Approval</p>
              <p className="text-xs text-amber-600 dark:text-amber-400 mt-0.5">This article is waiting for editor or admin approval before publishing.</p>
            </div>
            {isApprover && (
              <div className="flex items-center gap-2">
                <button
                  onClick={handleApprove}
                  disabled={actionLoading}
                  className="flex items-center gap-1 px-3 py-1.5 text-sm bg-green-600 hover:bg-green-700 disabled:opacity-50 text-white rounded-lg transition-colors"
                >
                  <CheckCircle size={14} />
                  Approve
                </button>
                <button
                  onClick={handleReject}
                  disabled={actionLoading}
                  className="flex items-center gap-1 px-3 py-1.5 text-sm bg-red-600 hover:bg-red-700 disabled:opacity-50 text-white rounded-lg transition-colors"
                >
                  <XCircle size={14} />
                  Reject
                </button>
              </div>
            )}
          </div>
        </div>
      )}

      <div className="mb-6">
        <div className="flex items-start justify-between">
          <h1 className="text-3xl font-bold text-zinc-900 dark:text-zinc-100">{article.title}</h1>
          <Link
            to={`/articles/${article.slug}/edit`}
            className="flex items-center gap-1 px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg hover:bg-zinc-50 dark:hover:bg-zinc-800"
          >
            <Edit size={14} />
            Edit
          </Link>
        </div>
        {article.excerpt && <p className="text-zinc-500 mt-2">{article.excerpt}</p>}
        <div className="flex items-center gap-4 mt-4 text-sm text-zinc-500">
          <span className="flex items-center gap-1">
            <Clock size={14} />
            {new Date(article.updatedAt).toLocaleDateString()}
          </span>
          <span className="flex items-center gap-1">
            <Tag size={14} />
            {article.contentType}
          </span>
          <span className={`px-2 py-0.5 rounded-full text-xs ${
            article.difficulty === "beginner" ? "bg-blue-100 text-blue-700" :
            article.difficulty === "intermediate" ? "bg-orange-100 text-orange-700" :
            "bg-red-100 text-red-700"
          }`}>
            {article.difficulty}
          </span>
          {article.apiKeyName ? (
            <span className="flex items-center gap-1 text-purple-600 dark:text-purple-400">
              <Key size={14} />
              {article.apiKeyName}
            </span>
          ) : article.ownerName ? (
            <span className="flex items-center gap-1">
              <User size={14} />
              {article.ownerName}
            </span>
          ) : null}
        </div>
        {article.tags?.length > 0 && (
          <div className="flex items-center gap-2 mt-3 flex-wrap">
            <Tag size={14} className="text-zinc-400" />
            {article.tags.map((tag) => (
              <span
                key={tag.id}
                className="text-xs px-2.5 py-1 rounded-full bg-indigo-50 text-indigo-600 dark:bg-indigo-950 dark:text-indigo-400"
              >
                {tag.name}
              </span>
            ))}
          </div>
        )}
      </div>

      <div className="prose dark:prose-invert max-w-none border-t border-zinc-200 dark:border-zinc-800 pt-6">
        {article.content ? (
          <TiptapRenderer content={article.content} />
        ) : (
          <p className="text-zinc-400 italic">No content yet.</p>
        )}
      </div>

      <div className="mt-8 pt-6 border-t border-zinc-200 dark:border-zinc-800">
        <p className="text-sm text-zinc-500 mb-3">Was this article helpful?</p>
        <div className="flex items-center gap-2">
          <button
            onClick={() => handleFeedback(true)}
            className="flex items-center gap-1 px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg hover:bg-green-50 dark:hover:bg-green-950 hover:border-green-300"
          >
            <ThumbsUp size={14} />
            Yes
          </button>
          <button
            onClick={() => handleFeedback(false)}
            className="flex items-center gap-1 px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg hover:bg-red-50 dark:hover:bg-red-950 hover:border-red-300"
          >
            <ThumbsDown size={14} />
            No
          </button>
        </div>
      </div>
    </div>
  );
}
