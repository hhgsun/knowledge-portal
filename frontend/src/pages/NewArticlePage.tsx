import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { useApi } from "../hooks/useApi";
import { useAuth } from "../contexts/AuthContext";
import { toast } from "sonner";
import { PendingFileList } from "../components/attachments/file-upload-zone";
import type { PendingAttachment } from "../components/attachments/file-upload-zone";
import { ArticleForm } from "../components/editor/article-form";
import { useArticleImages } from "../hooks/useArticleImages";

export default function NewArticlePage() {
  const navigate = useNavigate();
  const { fetchWithAuth } = useApi();
  const { user } = useAuth();
  const isViewer = user?.role === "viewer";
  const [title, setTitle] = useState("");
  const [contentMarkdown, setContentMarkdown] = useState("");
  const [excerpt, setExcerpt] = useState("");
  const [contentType, setContentType] = useState("reference");
  const [status, setStatus] = useState("draft");
  const [reviewIntervalDays, setReviewIntervalDays] = useState(90);
  const [tags, setTags] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [pendingFiles, setPendingFiles] = useState<PendingAttachment[]>([]);

  const { uploadImage, deleteBlobImage, uploadPendingImages, commitUploadedImages, uploadPendingFiles } = useArticleImages();

  const handleSave = async () => {
    if (!title.trim()) {
      setError("Title is required");
      return;
    }

    setSaving(true);
    setError("");

    let createdArticleId: string | undefined;
    try {
      const shellContent = removePendingImageNodes(contentMarkdown);
      const res = await fetchWithAuth("/api/articles", {
        method: "POST",
        body: JSON.stringify({
          title: title.trim(),
          contentMarkdown: shellContent,
          excerpt: excerpt.trim() || undefined,
          contentType,
          status: "draft",
          reviewIntervalDays,
          tags,
        }),
      });

      if (res.ok) {
        const article = await res.json();
        createdArticleId = article.id;

        const images = await uploadPendingImages(article.id, contentMarkdown);
        await uploadPendingFiles(article.id, pendingFiles);
        const finalize = await fetchWithAuth(`/api/articles/${article.id}`, {
          method: "PUT",
          body: JSON.stringify({
            title: title.trim(),
            contentMarkdown: images.markdown,
            excerpt: excerpt.trim() || undefined,
            contentType,
            status,
            reviewIntervalDays,
            tags,
            changeSummary: "Finalized initial attachment uploads",
          }),
        });
        if (!finalize.ok) {
          const data = await finalize.json().catch(() => ({}));
          throw new Error(data.error || "Failed to finalize article");
        }
        const finalized = await finalize.json();
        commitUploadedImages(images.uploadedBlobUrls);

        toast.success("Article created successfully");
        navigate(`/articles/${finalized.slug}`);
      } else {
        const data = await res.json();
        setError(data.error || "Failed to save article");
        setSaving(false);
      }
    } catch (error) {
      const message = error instanceof Error ? error.message : "An unexpected error occurred";
      if (createdArticleId && user?.role === "admin")
        await fetchWithAuth(`/api/articles/${createdArticleId}`, { method: "DELETE" }).catch(() => undefined);
      else if (createdArticleId) {
        toast.error(`${message}. The recoverable draft was kept.`);
        navigate(`/articles/${createdArticleId}/edit`);
        return;
      }
      setError(message);
      setSaving(false);
    }
  };

  return (
    <ArticleForm
      mode="create"
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
      reviewIntervalDays={reviewIntervalDays}
      onReviewIntervalDaysChange={setReviewIntervalDays}
      tags={tags}
      onTagsChange={setTags}
      saving={saving}
      error={error}
      onSave={handleSave}
      isViewer={isViewer}
      backLink="/articles"
      uploadImage={uploadImage}
      deleteImage={deleteBlobImage}
      attachmentSection={
        <PendingFileList
          files={pendingFiles}
          onAdd={(newFiles) => setPendingFiles(prev => [
            ...prev, ...newFiles.map(file => ({ file, includeInIndex: true }))
          ])}
          onRemove={(index) => setPendingFiles(prev => prev.filter((_, i) => i !== index))}
          onToggleIndexing={(index, includeInIndex) => setPendingFiles(prev =>
            prev.map((pending, i) => i === index ? { ...pending, includeInIndex } : pending))}
        />
      }
    />
  );
}

function removePendingImageNodes(markdown: string) {
  return markdown.replace(/!\[[^\]]*\]\((?:<blob:[^>]+>|blob:[^\s)]+)(?:\s+["'][^"']*["'])?\)/g, "");
}
