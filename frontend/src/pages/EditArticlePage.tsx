import { useState, useEffect, useCallback } from "react";
import { useParams, useNavigate, Link } from "react-router-dom";
import { Loader2 } from "lucide-react";
import { useApi } from "../hooks/useApi";
import { useAuth } from "../contexts/AuthContext";
import { toast } from "sonner";
import type { Article } from "../types/api";
import AttachmentList from "../components/attachments/attachment-list";
import { ArticleForm } from "../components/editor/article-form";
import { useArticleImages } from "../hooks/useArticleImages";

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
  const [pendingFiles, setPendingFiles] = useState<File[]>([]);
  const [deletedAttachmentIds, setDeletedAttachmentIds] = useState<Set<string>>(new Set());

  const { uploadImage, deleteBlobImage, uploadPendingImages, uploadPendingFiles } = useArticleImages();

  const deleteImage = useCallback(async (src: string) => {
    if (src.startsWith("blob:")) {
      await deleteBlobImage(src);
      return;
    }
    // If it's an already-uploaded image, delete from backend
    if (!article) return;
    const match = src.match(/\/api\/attachments\/([^/]+)\/download/);
    if (!match) return;
    const attachmentId = match[1];
    try {
      await fetchWithAuth(`/api/articles/${article.id}/attachments/${attachmentId}`, {
        method: "DELETE",
      });
    } catch {
      // Silent fail — orphan cleanup is not critical
    }
  }, [article, fetchWithAuth, deleteBlobImage]);

  useEffect(() => {
    if (params.slug) {
      fetchWithAuth(`/api/articles/${params.slug}`)
        .then((res) => res.json())
        .then((data) => {
          if (data.error) {
            setError("Article not found");
          } else {
            // Viewers can only edit their own articles
            if (user?.role === "viewer" && data.ownerId !== user?.id) {
              toast.error("You do not have permission to edit this article");
              navigate(`/articles/${params.slug}`);
              return;
            }
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

    try {
      // Upload pending images first, replace blob URLs with real URLs
      const finalContent = await uploadPendingImages(article.id, content || {});

      // Delete attachments marked for removal
      for (const attachmentId of deletedAttachmentIds) {
        try {
          await fetchWithAuth(`/api/articles/${article.id}/attachments/${attachmentId}`, { method: "DELETE" });
        } catch {
          // Silent fail on delete
        }
      }

      // Upload pending file attachments
      await uploadPendingFiles(article.id, pendingFiles);

      const res = await fetchWithAuth(`/api/articles/${article.id}`, {
        method: "PUT",
        body: JSON.stringify({
          title: title.trim(),
          content: finalContent,
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
        toast.success("Article saved successfully");
        navigate(`/articles/${updated.slug}`);
      } else {
        const data = await res.json();
        setError(data.error || "Failed to save article");
        setSaving(false);
      }
    } catch {
      setError("An unexpected error occurred");
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
    <ArticleForm
      mode="edit"
      title={title}
      onTitleChange={setTitle}
      content={content}
      onContentChange={setContent}
      excerpt={excerpt}
      onExcerptChange={setExcerpt}
      contentType={contentType}
      onContentTypeChange={setContentType}
      difficulty={difficulty}
      onDifficultyChange={setDifficulty}
      status={status}
      onStatusChange={setStatus}
      tags={tags}
      onTagsChange={setTags}
      saving={saving}
      error={error}
      onSave={handleSave}
      onSubmitForReview={() => { setStatus("pending"); handleSave(); }}
      isViewer={isViewer}
      backLink={`/articles/${article.slug}`}
      articleId={article.id}
      uploadImage={uploadImage}
      deleteImage={deleteImage}
      changeSummary={changeSummary}
      onChangeSummaryChange={setChangeSummary}
      attachmentSection={
        <AttachmentList
          articleId={article.id}
          canEdit={true}
          hideUpload={true}
          deletedIds={deletedAttachmentIds}
          onDeferredDelete={(attachment) => setDeletedAttachmentIds(prev => new Set(prev).add(attachment.id))}
          onUndoDelete={(id) => setDeletedAttachmentIds(prev => { const next = new Set(prev); next.delete(id); return next; })}
          pendingFiles={pendingFiles}
          onAddFiles={(newFiles) => setPendingFiles(prev => [...prev, ...newFiles])}
          onRemovePendingFile={(index) => setPendingFiles(prev => prev.filter((_, i) => i !== index))}
        />
      }
    />
  );
}
