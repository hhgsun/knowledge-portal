import { useState, useEffect, useCallback } from "react";
import { useParams, useNavigate, Link } from "react-router-dom";
import { useApi } from "../hooks/useApi";
import { useAuth } from "../contexts/AuthContext";
import { toast } from "sonner";
import { EditArticleSkeleton } from "../components/ui/skeleton";
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
  const [contentMarkdown, setContentMarkdown] = useState("");
  const [excerpt, setExcerpt] = useState("");
  const [contentType, setContentType] = useState("reference");
  const [status, setStatus] = useState("draft");
  const [changeSummary, setChangeSummary] = useState("");
  const [tags, setTags] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [pendingFiles, setPendingFiles] = useState<File[]>([]);
  const [deletedAttachmentIds, setDeletedAttachmentIds] = useState<Set<string>>(new Set());

  const { uploadImage, deleteBlobImage, uploadPendingImages, uploadPendingFiles, deleteUploadedAttachments } = useArticleImages();

  const deleteImage = useCallback(async (src: string) => {
    if (src.startsWith("blob:")) {
      await deleteBlobImage(src);
      return;
    }
    // Existing images are deleted only after the article update succeeds.
    if (!article) return;
    const match = src.match(/\/api\/attachments\/([^/]+)\/download/);
    if (!match) return;
    const attachmentId = match[1];
    setDeletedAttachmentIds(prev => new Set(prev).add(attachmentId));
  }, [article, deleteBlobImage]);

  useEffect(() => {
    if (params.slug) {
      fetchWithAuth(`/api/articles/${params.slug}`)
        .then((res) => res.json())
        .then((data) => {
          if (data.error) {
            setError("Makale bulunamadı");
          } else {
            // Viewers can only edit their own articles
            if (user?.role === "viewer" && data.ownerId !== user?.id) {
              toast.error("Bu makaleyi düzenleme yetkiniz yok");
              navigate(`/articles/${params.slug}`);
              return;
            }
            setArticle(data);
            setTitle(data.title);
            setContentMarkdown(data.contentMarkdown || "");
            setExcerpt(data.excerpt || "");
            setContentType(data.contentType);
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
  }, [params.slug, fetchWithAuth, navigate, user?.id, user?.role]);

  const handleSave = async () => {
    if (!title.trim()) {
      setError("Title is required");
      return;
    }
    if (!article) return;

    setSaving(true);
    setError("");

    let newlyUploadedIds: string[] = [];
    try {
      // Upload pending images first, replace blob URLs with real URLs
      const images = await uploadPendingImages(article.id, contentMarkdown);
      newlyUploadedIds.push(...images.uploadedIds);

      newlyUploadedIds.push(...await uploadPendingFiles(article.id, pendingFiles));

      const res = await fetchWithAuth(`/api/articles/${article.id}`, {
        method: "PUT",
        body: JSON.stringify({
          title: title.trim(),
          contentMarkdown: images.markdown,
          excerpt: excerpt.trim() || undefined,
          contentType,
          status,
          changeSummary: changeSummary.trim() || undefined,
          tags,
        }),
      });

      if (res.ok) {
        const updated = await res.json();
        let deleteFailed = false;
        for (const attachmentId of deletedAttachmentIds) {
          const deleted = await fetchWithAuth(`/api/articles/${article.id}/attachments/${attachmentId}`, { method: "DELETE" });
          if (!deleted.ok) deleteFailed = true;
        }
        if (deleteFailed) toast.error("Makale kaydedildi; bazı eski ekler silinemedi");
        toast.success("Makale başarıyla kaydedildi");
        navigate(`/articles/${updated.slug}`);
      } else {
        const data = await res.json();
        await deleteUploadedAttachments(article.id, newlyUploadedIds);
        newlyUploadedIds = [];
        setError(data.error || "Failed to save article");
        setSaving(false);
      }
    } catch (error) {
      await deleteUploadedAttachments(article.id, newlyUploadedIds);
      setError(error instanceof Error ? error.message : "An unexpected error occurred");
      setSaving(false);
    }
  };

  if (loading) {
    return <EditArticleSkeleton />;
  }

  if (!article) {
    return (
      <div className="text-center py-12">
        <p className="text-zinc-500">{error || "Makale bulunamadı"}</p>
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
      contentMarkdown={contentMarkdown}
      onContentMarkdownChange={setContentMarkdown}
      excerpt={excerpt}
      onExcerptChange={setExcerpt}
      contentType={contentType}
      onContentTypeChange={setContentType}
      status={status}
      onStatusChange={setStatus}
      tags={tags}
      onTagsChange={setTags}
      saving={saving}
      error={error}
      onSave={handleSave}
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
