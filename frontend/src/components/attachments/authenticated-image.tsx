import { useEffect, useState, type ImgHTMLAttributes } from "react";
import { useAuth } from "../../contexts/AuthContext";

type Props = ImgHTMLAttributes<HTMLImageElement>;

/** Renders protected attachment images without putting the JWT in the URL. */
export function AuthenticatedImage({ src, alt, ...props }: Props) {
  const { token } = useAuth();
  const [loaded, setLoaded] = useState<{ source: string; url: string }>();
  const protectedSource = src?.startsWith("/api/attachments/") ?? false;

  useEffect(() => {
    if (!protectedSource || !src || !token) return;

    const controller = new AbortController();
    let objectUrl: string | undefined;
    void fetch(src, {
      headers: { Authorization: `Bearer ${token}` },
      signal: controller.signal,
    })
      .then((response) => {
        if (!response.ok) throw new Error("Protected image could not be loaded");
        return response.blob();
      })
      .then((blob) => {
        objectUrl = URL.createObjectURL(blob);
        setLoaded({ source: src, url: objectUrl });
      })
      .catch(() => { /* Keep the protected image hidden when loading fails. */ });

    return () => {
      controller.abort();
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [src, token, protectedSource]);

  const resolvedSrc = protectedSource
    ? (token && loaded && loaded.source === src ? loaded.url : undefined)
    : src;
  return <img {...props} src={resolvedSrc} alt={alt ?? ""} />;
}
