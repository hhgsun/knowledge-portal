import { useCallback, useRef } from "react";
import { useApi } from "./useApi";
import { toast } from "sonner";

/**
 * Shared hook for deferred image upload logic used by both
 * NewArticlePage and EditArticlePage.
 */
export function useArticleImages() {
  const { fetchWithAuth } = useApi();
  const pendingUploadsRef = useRef<Map<string, File>>(new Map());

  const uploadImage = useCallback(async (file: File): Promise<string | null> => {
    const blobUrl = URL.createObjectURL(file);
    pendingUploadsRef.current.set(blobUrl, file);
    return blobUrl;
  }, []);

  const deleteBlobImage = useCallback(async (src: string) => {
    if (src.startsWith("blob:")) {
      pendingUploadsRef.current.delete(src);
      URL.revokeObjectURL(src);
    }
  }, []);

  const uploadPendingImages = useCallback(
    async (articleId: string, markdown: string): Promise<{ markdown: string; uploadedIds: string[] }> => {
      const pending = pendingUploadsRef.current;
      if (pending.size === 0) return { markdown, uploadedIds: [] };

      const urlMap = new Map<string, string>();
      const uploadedIds: string[] = [];

      try {
        for (const [blobUrl, file] of pending) {
          const formData = new FormData();
          formData.append("file", file);
          const res = await fetchWithAuth(`/api/articles/${articleId}/attachments`, {
            method: "POST",
            body: formData,
          });
          if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            throw new Error(err.error || `Failed to upload ${file.name}`);
          }
          const data = await res.json();
          urlMap.set(blobUrl, data.downloadUrl);
          uploadedIds.push(data.id);
        }
      } catch (error) {
        await Promise.all(uploadedIds.map((id) => fetchWithAuth(
          `/api/articles/${articleId}/attachments/${id}`, { method: "DELETE" }).catch(() => undefined)));
        const message = error instanceof Error ? error.message : "Image upload failed";
        toast.error(message);
        throw error;
      }

      let result = markdown;
      for (const [blobUrl, realUrl] of urlMap) {
        result = result.split(blobUrl).join(realUrl);
        URL.revokeObjectURL(blobUrl);
      }
      pending.clear();
      return { markdown: result, uploadedIds };
    },
    [fetchWithAuth]
  );

  const uploadPendingFiles = useCallback(
    async (articleId: string, files: File[]): Promise<string[]> => {
      const uploadedIds: string[] = [];
      try {
        for (const file of files) {
          const formData = new FormData();
          formData.append("file", file);
          const res = await fetchWithAuth(`/api/articles/${articleId}/attachments`, {
            method: "POST",
            body: formData,
          });
          if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            throw new Error(err.error || `Failed to upload ${file.name}`);
          }
          const data = await res.json();
          uploadedIds.push(data.id);
        }
        return uploadedIds;
      } catch (error) {
        await Promise.all(uploadedIds.map((id) => fetchWithAuth(
          `/api/articles/${articleId}/attachments/${id}`, { method: "DELETE" }).catch(() => undefined)));
        const message = error instanceof Error ? error.message : "File upload failed";
        toast.error(message);
        throw error;
      }
    },
    [fetchWithAuth]
  );

  const deleteUploadedAttachments = useCallback(async (articleId: string, ids: string[]) => {
    await Promise.all(ids.map((id) => fetchWithAuth(
      `/api/articles/${articleId}/attachments/${id}`, { method: "DELETE" }).catch(() => undefined)));
  }, [fetchWithAuth]);

  return {
    pendingUploadsRef,
    uploadImage,
    deleteBlobImage,
    uploadPendingImages,
    uploadPendingFiles,
    deleteUploadedAttachments,
  };
}
