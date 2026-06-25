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
  const [content, setContent] = useState<Record<string, unknown> | null>(null);
  const [excerpt, setExcerpt] = useState("");
  const [contentType, setContentType] = useState("reference");
  const [status, setStatus] = useState("draft");
  const [tags, setTags] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const [pendingFiles, setPendingFiles] = useState<File[]>([]);

  const { uploadImage, deleteBlobImage, uploadPendingImages, uploadPendingFiles } = useArticleImages();

  const handleSave = async () => {
    if (!title.trim()) {
      setError("Title is required");
      return;
    }

    setSaving(true);
    setError("");

    try {
      const res = await fetchWithAuth("/api/articles", {
        method: "POST",
        body: JSON.stringify({
          title: title.trim(),
          content,
          excerpt: excerpt.trim() || undefined,
          contentType,
          status,
          tags,
        }),
      });

      if (res.ok) {
        const article = await res.json();

        // Upload pending images and update content if needed
        const finalContent = await uploadPendingImages(article.id, content || {});
        if (finalContent !== content) {
          await fetchWithAuth(`/api/articles/${article.id}`, {
            method: "PUT",
            body: JSON.stringify({
              title: title.trim(),
              content: finalContent,
              excerpt: excerpt.trim() || undefined,
              contentType,
              status,
              tags,
            }),
          });
        }

        // Upload pending file attachments
        await uploadPendingFiles(article.id, pendingFiles);

        toast.success("Article created successfully");
        navigate(`/articles/${article.slug}`);
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

  return (
    <ArticleForm
      mode="create"
      title={title}
      onTitleChange={setTitle}
      content={content}
      onContentChange={setContent}
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
      onSubmitForReview={() => { setStatus("pending"); handleSave(); }}
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
