import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { useApi } from "../hooks/useApi";
import { useAuth } from "../contexts/AuthContext";
import { toast } from "sonner";
import { PendingFileList } from "../components/attachments/file-upload-zone";
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
  const [tags, setTags] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [pendingFiles, setPendingFiles] = useState<File[]>([]);

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
      if (createdArticleId)
        await fetchWithAuth(`/api/articles/${createdArticleId}`, { method: "DELETE" }).catch(() => undefined);
      setError(error instanceof Error ? error.message : "An unexpected error occurred");
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
          onAdd={(newFiles) => setPendingFiles(prev => [...prev, ...newFiles])}
          onRemove={(index) => setPendingFiles(prev => prev.filter((_, i) => i !== index))}
        />
      }
    />
  );
}

function removePendingImageNodes(markdown: string) {
  return markdown.replace(/!\[[^\]]*\]\((?:<blob:[^>]+>|blob:[^\s)]+)(?:\s+["'][^"']*["'])?\)/g, "");
}
