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
    async (articleId: string, contentJson: Record<string, unknown>): Promise<Record<string, unknown>> => {
      const pending = pendingUploadsRef.current;
      if (pending.size === 0) return contentJson;

      const urlMap = new Map<string, string>();

      for (const [blobUrl, file] of pending) {
        const formData = new FormData();
        formData.append("file", file);
        try {
          const res = await fetchWithAuth(`/api/articles/${articleId}/attachments`, {
            method: "POST",
            body: formData,
          });
          if (res.ok) {
            const data = await res.json();
            urlMap.set(blobUrl, data.downloadUrl);
          } else {
            const err = await res.json();
            toast.error(err.error || `Failed to upload ${file.name}`);
          }
        } catch {
          toast.error(`Failed to upload ${file.name}`);
        }
        URL.revokeObjectURL(blobUrl);
      }
      pending.clear();

      if (urlMap.size > 0) {
        let jsonStr = JSON.stringify(contentJson);
        for (const [blobUrl, realUrl] of urlMap) {
          jsonStr = jsonStr.split(blobUrl).join(realUrl);
        }
        return JSON.parse(jsonStr);
      }
      return contentJson;
    },
    [fetchWithAuth]
  );

  const uploadPendingFiles = useCallback(
    async (articleId: string, files: File[]) => {
      for (const file of files) {
        const formData = new FormData();
        formData.append("file", file);
        try {
          const res = await fetchWithAuth(`/api/articles/${articleId}/attachments`, {
            method: "POST",
            body: formData,
          });
          if (!res.ok) {
            const err = await res.json();
            toast.error(err.error || `Failed to upload ${file.name}`);
          }
        } catch {
          toast.error(`Failed to upload ${file.name}`);
        }
      }
    },
    [fetchWithAuth]
  );

  return {
    pendingUploadsRef,
    uploadImage,
    deleteBlobImage,
    uploadPendingImages,
    uploadPendingFiles,
  };
}
