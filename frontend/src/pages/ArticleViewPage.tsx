import { useEffect, useState } from "react";
import { useParams, Link, useNavigate } from "react-router-dom";
import { Edit, Clock, User, Tag, Key, ThumbsUp, ThumbsDown, CheckCircle, XCircle, MessageSquare, FileText, Eye, Trash2 } from "lucide-react";
import { TiptapRenderer } from "../components/editor/tiptap-renderer";
import { useApi } from "../hooks/useApi";
import { useAuth } from "../contexts/AuthContext";
import { toast } from "sonner";
import { ContentTypeBadge } from "../components/ContentTypeBadge";
import { ArticleViewSkeleton } from "../components/ui/skeleton";
import type { Article, VoteSummary, ArticleCommentItem, RelatedArticle } from "../types/api";
import AttachmentList from "../components/attachments/attachment-list";

export default function ArticleViewPage() {
  const params = useParams();
  const navigate = useNavigate();
  const { fetchWithAuth } = useApi();
  const { user } = useAuth();

  /* const handleBack = () => {
    const historyIdx = (window.history.state as { idx?: number } | null)?.idx;
    if (historyIdx && historyIdx > 0) {
      navigate(-1);
    } else {
      navigate('/articles');
    }
  }; */
  const [article, setArticle] = useState<Article | null>(null);
  const [loading, setLoading] = useState(true);
  const [actionLoading, setActionLoading] = useState(false);
  const [votes, setVotes] = useState<VoteSummary | null>(null);
  const [comments, setComments] = useState<ArticleCommentItem[]>([]);
  const [commentText, setCommentText] = useState("");
  const [reasonText, setReasonText] = useState("");
  const [showReasonInput, setShowReasonInput] = useState(false);
  const [relatedArticles, setRelatedArticles] = useState<RelatedArticle[]>([]);

  const isApprover = user?.role === "admin" || user?.role === "editor";
  const canEdit = user?.role === "admin" || (article && article.ownerId === user?.id);
  const canDelete = user?.role === "admin";// || (article && article.ownerId === user?.id);

  useEffect(() => {
    if (params.slug) {
      fetchWithAuth(`/api/articles/${params.slug}`)
        .then((res) => res.json())
        .then((data) => {
          if (data.error) {
            setArticle(null);
          } else {
            setArticle(data);
            loadVotes(data.id);
            loadComments(data.id);
            loadRelated(data.id);
          }
          setLoading(false);
        })
        .catch(() => setLoading(false));
    }
  }, [params.slug, fetchWithAuth]);

  const handleVote = async (isHelpful: boolean) => {
    if (!article) return;

    // If voting Not Helpful and no existing vote or existing vote is helpful, show reason input
    if (!isHelpful && votes?.userVote !== false) {
      setShowReasonInput(true);
      return;
    }

    const res = await fetchWithAuth(`/api/articles/${article.id}/vote`, {
      method: "POST",
      body: JSON.stringify({ isHelpful, reason: null }),
    });
    if (res.ok) {
      const data = await res.json();
      if (data.action === "removed") toast.success("Oyunuz kaldırıldı");
      else toast.success("Oyunuz kaydedildi");
      loadVotes(article.id);
    } else {
      toast.error("Oy gönderilemedi");
    }
  };

  const submitNotHelpful = async () => {
    if (!article) return;
    const res = await fetchWithAuth(`/api/articles/${article.id}/vote`, {
      method: "POST",
      body: JSON.stringify({ isHelpful: false, reason: reasonText.trim() || null }),
    });
    if (res.ok) {
      const data = await res.json();
      if (data.action === "removed") toast.success("Oyunuz kaldırıldı");
      else toast.success("Oyunuz kaydedildi");
      setShowReasonInput(false);
      setReasonText("");
      loadVotes(article.id);
    } else {
      toast.error("Oy gönderilemedi");
    }
  };

  const handleComment = async () => {
    if (!article || !commentText.trim()) return;
    const res = await fetchWithAuth(`/api/articles/${article.id}/comments`, {
      method: "POST",
      body: JSON.stringify({ comment: commentText.trim() }),
    });
    if (res.ok) {
      toast.success("Yorum eklendi");
      setCommentText("");
      loadComments(article.id);
    } else {
      toast.error("Yorum gönderilemedi");
    }
  };

  const handleDeleteComment = async (commentId: string) => {
    if (!article) return;
    const res = await fetchWithAuth(`/api/articles/${article.id}/comments/${commentId}`, {
      method: "DELETE",
    });
    if (res.ok) {
      toast.success("Yorum silindi");
      loadComments(article.id);
    } else {
      toast.error("Yorum silinemedi");
    }
  };

  const loadVotes = (articleId: string) => {
    fetchWithAuth(`/api/articles/${articleId}/votes`)
      .then((res) => res.json())
      .then((data) => { if (!data.error) setVotes(data); })
      .catch(() => { });
  };

  const loadComments = (articleId: string) => {
    fetchWithAuth(`/api/articles/${articleId}/comments`)
      .then((res) => res.json())
      .then((data) => { if (data.comments) setComments(data.comments); })
      .catch(() => { });
  };

  const loadRelated = (articleId: string) => {
    fetchWithAuth(`/api/articles/${articleId}/related?limit=2`)
      .then((res) => res.json())
      .then((data) => { if (data.articles) setRelatedArticles(data.articles); })
      .catch(() => { });
  };

  const handleApprove = async () => {
    if (!article) return;
    setActionLoading(true);
    const res = await fetchWithAuth(`/api/articles/${article.id}/approve`, { method: "POST" });
    if (res.ok) {
      setArticle({ ...article, status: "published" });
      toast.success("Makale onaylandı ve yayımlandı");
    } else {
      const data = await res.json().catch(() => ({}));
      toast.error(data.error || "Makale onaylanamadı");
    }
    setActionLoading(false);
  };

  const handleReject = async () => {
    if (!article) return;
    setActionLoading(true);
    const res = await fetchWithAuth(`/api/articles/${article.id}/reject`, { method: "POST" });
    if (res.ok) {
      setArticle({ ...article, status: "draft" });
      toast.success("Makale reddedildi ve taslağa döndürüldü");
    } else {
      const data = await res.json().catch(() => ({}));
      toast.error(data.error || "Makale reddedilemedi");
    }
    setActionLoading(false);
  };

  const handleDelete = async () => {
    if (!article) return;
    if (!confirm(`"${article.title}" makalesi silinecek. Emin misiniz? Bu işlem geri alınamaz.`)) return;
    setActionLoading(true);
    const res = await fetchWithAuth(`/api/articles/${article.id}`, { method: "DELETE" });
    if (res.ok) {
      toast.success("Makale silindi");
      navigate("/articles");
    } else {
      const data = await res.json().catch(() => ({}));
      toast.error(data.error || "Makale silinemedi");
      setActionLoading(false);
    }
  };

  if (loading) {
    return <ArticleViewSkeleton />;
  }

  if (!article) {
    return (
      <div className="text-center py-12">
        <p className="text-zinc-500">Makale bulunamadı</p>
        <Link to="/articles" className="text-blue-600 hover:underline text-sm mt-2 inline-block">
          Back to articles
        </Link>
      </div>
    );
  }

  return (
    <div className="max-w-5xl mx-auto">
      {/* <div className="flex items-center gap-2 mb-6">
        <button onClick={handleBack} className="flex items-center gap-1 text-sm text-zinc-500 hover:text-zinc-700 dark:hover:text-zinc-300">
          <ArrowLeft size={14} />
          Geri
        </button>
        <span className="text-zinc-300">/</span>
        <span className="text-sm text-zinc-700 dark:text-zinc-300">{article.title}</span>
      </div> */}

      {article.status === "pending" && (
        <div className="mb-6 p-4 bg-amber-50 dark:bg-amber-950 border border-amber-200 dark:border-amber-800 rounded-xl">
          <div className="flex items-center justify-between">
            <div>
              <p className="text-sm font-medium text-amber-800 dark:text-amber-200">Onay Bekliyor</p>
              <p className="text-xs text-amber-600 dark:text-amber-400 mt-0.5">This article is waiting for editor or admin approval before publishing.</p>
            </div>
            {isApprover && (
              <div className="flex items-center gap-2">
                <button
                  onClick={handleApprove}
                  disabled={actionLoading}
                  className="flex items-center gap-1 px-2 py-1 text-sm bg-green-600 hover:bg-green-700 disabled:opacity-50 text-white rounded-lg transition-colors"
                >
                  <CheckCircle size={14} />
                  Approve
                </button>
                <button
                  onClick={handleReject}
                  disabled={actionLoading}
                  className="flex items-center gap-1 px-2 py-1 text-sm bg-red-600 hover:bg-red-700 disabled:opacity-50 text-white rounded-lg transition-colors"
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
          <h1 className="text-3xl font-bold text-zinc-900 dark:text-zinc-100 mr-1">{article.title}</h1>
          <div className="flex items-center gap-2">
            <Link
              to={`/articles/${article.slug}/versions`}
              className="flex items-center gap-1 px-2 py-1 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg hover:bg-zinc-50 dark:hover:bg-zinc-800"
            >
              <Clock size={14} />
              History
            </Link>
            {canEdit && (
              <Link
                to={`/articles/${article.slug}/edit`}
                className="flex items-center gap-1 px-2 py-1 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg hover:bg-zinc-50 dark:hover:bg-zinc-800"
              >
                <Edit size={14} />
                Edit
              </Link>
            )}
            {canDelete && (
              <button
                onClick={handleDelete}
                disabled={actionLoading}
                className="flex items-center gap-1 px-2 py-1 text-sm border border-red-300 dark:border-red-800 text-red-600 dark:text-red-400 rounded-lg hover:bg-red-50 dark:hover:bg-red-950 disabled:opacity-50 transition-colors"
              >
                <Trash2 size={14} />
                Delete
              </button>
            )}
          </div>
        </div>
        {article.excerpt && <p className="text-zinc-500 mt-2">{article.excerpt}</p>}
        <div className="flex items-center gap-4 mt-4 text-sm text-zinc-500">
          <ContentTypeBadge contentType={article.contentType} size="md" clickable />
          {article.status !== "published" && (
            <span className={`text-xs px-2 py-0.5 rounded-full ${article.status === "draft" ? "bg-zinc-100 text-zinc-600 dark:bg-zinc-800 dark:text-zinc-400" :
              article.status === "pending" ? "bg-amber-100 text-amber-700 dark:bg-amber-900 dark:text-amber-300" :
                article.status === "archived" ? "bg-red-100 text-red-700 dark:bg-red-900 dark:text-red-300" : ""
              }`}>
              {article.status}
            </span>
          )}
          <span className="flex items-center gap-1">
            <Clock size={14} />
            {new Date(article.updatedAt).toLocaleDateString()}
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
              <Link
                key={tag.id}
                to={`/articles?tag=${encodeURIComponent(tag.slug)}`}
                className="text-xs px-2.5 py-1 rounded-full bg-indigo-50 text-indigo-600 dark:bg-indigo-950 dark:text-indigo-400 hover:bg-indigo-100 dark:hover:bg-indigo-900 transition-colors"
              >
                {tag.name}
              </Link>
            ))}
          </div>
        )}
      </div>

      <div className="prose dark:prose-invert max-w-none border-t border-zinc-200 dark:border-zinc-800 pt-6">
        {article.content ? (
          <TiptapRenderer content={article.content as Record<string, unknown>} />
        ) : (
          <p className="text-zinc-400 italic">Henüz içerik yok.</p>
        )}
      </div>

      <AttachmentList articleId={article.id} canEdit={false} initialAttachments={article.attachments} />

      {relatedArticles.length > 0 && (
        <div className="mt-8 pt-6 border-t border-zinc-200 dark:border-zinc-800">
          <h3 className="text-sm font-medium text-zinc-700 dark:text-zinc-300 flex items-center gap-1.5 mb-4">
            <FileText size={14} />
            İlgili Makaleler
          </h3>
          <div className="grid gap-3 sm:grid-cols-2">
            {relatedArticles.map((ra) => (
              <Link
                key={ra.id}
                to={`/articles/${ra.slug}`}
                className="p-3 rounded-lg border border-zinc-200 dark:border-zinc-700 hover:border-blue-300 dark:hover:border-blue-700 hover:bg-blue-50/50 dark:hover:bg-blue-950/30 transition-colors"
              >
                <p className="text-sm font-medium text-zinc-900 dark:text-zinc-100 line-clamp-1">{ra.title}</p>
                {ra.excerpt && (
                  <p className="text-xs text-zinc-500 mt-1 line-clamp-2">{ra.excerpt}</p>
                )}
                {ra.tags.length > 0 && (
                  <div className="flex items-center gap-1.5 mt-2 flex-wrap">
                    {ra.tags.slice(0, 3).map((tag) => (
                      <span
                        key={tag.id}
                        onClick={(e) => {
                          e.preventDefault();
                          e.stopPropagation();
                          navigate(`/articles?tag=${encodeURIComponent(tag.slug)}`);
                        }}
                        className="text-[10px] px-1.5 py-0.5 rounded-full bg-indigo-50 text-indigo-600 dark:bg-indigo-950 dark:text-indigo-400 cursor-pointer hover:bg-indigo-100 dark:hover:bg-indigo-900 transition-colors"
                      >
                        {tag.name}
                      </span>
                    ))}
                  </div>
                )}
              </Link>
            ))}
          </div>
        </div>
      )}

      <div className="mt-8 pt-6 border-t border-zinc-200 dark:border-zinc-800">
        {/* View count + Wilson Score */}
        <div className="flex items-center gap-4 mb-4 text-sm text-zinc-600 dark:text-zinc-400">
          <span className="flex items-center gap-1">
            <Eye size={14} />
            {article.viewCount} görüntülenme
          </span>
          {votes && (votes.helpful > 0 || votes.notHelpful > 0) && (
            <>
              <span className="flex items-center gap-1">
                <ThumbsUp size={14} className="text-green-600" />
                {votes.helpful}
              </span>
              <span className="flex items-center gap-1">
                <ThumbsDown size={14} className="text-red-500" />
                {votes.notHelpful}
              </span>
              <span className="px-2 py-0.5 rounded-full text-xs bg-blue-100 text-blue-700 dark:bg-blue-950 dark:text-blue-300 font-medium">
                Score: {(votes.wilsonScore * 100).toFixed(0)}%
              </span>
            </>
          )}
        </div>

        {/* Vote buttons */}
        <div className="mb-6">
          <p className="text-sm text-zinc-500 mb-3">Bu makale faydalı mıydı?</p>
          <div className="flex items-center gap-2">
            <button
              onClick={() => handleVote(true)}
              className={`flex items-center gap-1 px-3 py-1.5 text-sm border rounded-lg transition-colors ${votes?.userVote === true
                ? "bg-green-100 border-green-400 text-green-700 dark:bg-green-950 dark:border-green-600 dark:text-green-300"
                : "border-zinc-300 dark:border-zinc-700 hover:bg-green-50 dark:hover:bg-green-950 hover:border-green-300"
                }`}
            >
              <ThumbsUp size={14} />
              Faydalı
            </button>
            <button
              onClick={() => {
                if (votes?.userVote === false) {
                  // Already voted not helpful → toggle off
                  handleVote(false);
                } else {
                  setShowReasonInput(true);
                }
              }}
              className={`flex items-center gap-1 px-3 py-1.5 text-sm border rounded-lg transition-colors ${votes?.userVote === false
                ? "bg-red-100 border-red-400 text-red-700 dark:bg-red-950 dark:border-red-600 dark:text-red-300"
                : "border-zinc-300 dark:border-zinc-700 hover:bg-red-50 dark:hover:bg-red-950 hover:border-red-300"
                }`}
            >
              <ThumbsDown size={14} />
              Faydalı Değil
            </button>
          </div>

          {/* Not Helpful reason input */}
          {showReasonInput && (
            <div className="mt-3 p-3 rounded-lg border border-red-200 dark:border-red-800 bg-red-50/50 dark:bg-red-950/30">
              <p className="text-xs text-zinc-500 mb-2">Neden faydalı değil? (opsiyonel)</p>
              <textarea
                value={reasonText}
                onChange={(e) => setReasonText(e.target.value)}
                placeholder="Nedenini yazabilirsiniz..."
                rows={2}
                className="w-full mb-2 px-3 py-2 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-900 text-zinc-900 dark:text-zinc-100 placeholder-zinc-400 resize-none focus:outline-none focus:ring-2 focus:ring-red-500"
              />
              <div className="flex items-center gap-2">
                <button
                  onClick={submitNotHelpful}
                  className="px-3 py-1.5 text-sm bg-red-600 hover:bg-red-700 text-white rounded-lg transition-colors"
                >
                  Gönder
                </button>
                <button
                  onClick={() => { setShowReasonInput(false); setReasonText(""); }}
                  className="px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg hover:bg-zinc-100 dark:hover:bg-zinc-800 transition-colors"
                >
                  İptal
                </button>
              </div>
            </div>
          )}
        </div>

        {/* Not Helpful reasons */}
        {votes && votes.reasons.length > 0 && (
          <div className="mb-6">
            <h3 className="text-sm font-medium text-zinc-700 dark:text-zinc-300 mb-2">Faydasız bulunma nedenleri</h3>
            <div className="space-y-1">
              {votes.reasons.map((reason, i) => (
                <div key={i} className="text-sm text-zinc-500 dark:text-zinc-400 flex items-start gap-2">
                  <ThumbsDown size={12} className="text-red-400 mt-0.5 shrink-0" />
                  <span>{reason}</span>
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Comments section (independent from votes) */}
        <div className="border-t border-zinc-200 dark:border-zinc-800 pt-6">
          <h3 className="text-sm font-medium text-zinc-700 dark:text-zinc-300 flex items-center gap-1.5 mb-3">
            <MessageSquare size={14} />
            Yorumlar ({comments.length})
          </h3>
          <div className="mb-4">
            <textarea
              value={commentText}
              onChange={(e) => setCommentText(e.target.value)}
              placeholder="Yorum ekle..."
              rows={2}
              className="w-full mb-2 px-3 py-2 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-900 text-zinc-900 dark:text-zinc-100 placeholder-zinc-400 resize-none focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
            <button
              onClick={handleComment}
              disabled={!commentText.trim()}
              className="px-3 py-1.5 text-sm bg-blue-600 hover:bg-blue-700 disabled:opacity-40 disabled:cursor-not-allowed text-white rounded-lg transition-colors"
            >
              Yorum Gönder
            </button>
          </div>
          {comments.length > 0 && (
            <div className="space-y-3">
              {comments.map((c) => (
                <div key={c.id} className="p-3 rounded-lg bg-zinc-50 dark:bg-zinc-800/50 border border-zinc-200 dark:border-zinc-700">
                  <div className="flex items-center justify-between mb-1">
                    <div className="flex items-center gap-2">
                      <span className="text-xs font-medium text-zinc-700 dark:text-zinc-300">{c.userName}</span>
                      <span className="text-xs text-zinc-400">{new Date(c.createdAt).toLocaleDateString()}</span>
                    </div>
                    {c.isOwn && (
                      <button
                        onClick={() => handleDeleteComment(c.id)}
                        className="text-zinc-400 hover:text-red-500 transition-colors"
                        title="Yorumu sil"
                      >
                        <Trash2 size={12} />
                      </button>
                    )}
                  </div>
                  <p className="text-sm text-zinc-600 dark:text-zinc-400">{c.comment}</p>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
