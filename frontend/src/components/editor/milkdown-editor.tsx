import { Crepe } from "@milkdown/crepe";
import "@milkdown/crepe/theme/common/style.css";
import "@milkdown/crepe/theme/frame.css";
import { useEffect, useRef } from "react";
import { toast } from "sonner";
import { useAuth } from "../../contexts/AuthContext";

interface MilkdownEditorProps {
  contentMarkdown: string;
  onChange: (markdown: string) => void;
  articleId?: string;
  uploadImage?: (file: File) => Promise<string | null>;
  deleteImage?: (src: string) => Promise<void>;
}

export default function MilkdownEditor({ contentMarkdown, onChange, uploadImage, deleteImage }: MilkdownEditorProps) {
  const rootRef = useRef<HTMLDivElement>(null);
  const initialMarkdown = useRef(contentMarkdown);
  const onChangeRef = useRef(onChange);
  const uploadRef = useRef(uploadImage);
  const deleteRef = useRef(deleteImage);
  const { token } = useAuth();
  onChangeRef.current = onChange;
  uploadRef.current = uploadImage;
  deleteRef.current = deleteImage;

  useEffect(() => {
    if (!rootRef.current) return;
    let active = true;
    let previousImages = extractImageUrls(initialMarkdown.current);

    const handleUpload = async (file: File) => {
      const url = await uploadRef.current?.(file);
      if (!url) {
        toast.error("Görsel yüklenemedi");
        throw new Error("Image upload failed");
      }
      return url;
    };

    const crepe = new Crepe({
      root: rootRef.current,
      defaultValue: initialMarkdown.current,
      features: {
        [Crepe.Feature.Latex]: false,
        [Crepe.Feature.AI]: false,
        [Crepe.Feature.TopBar]: true,
      },
      featureConfigs: {
        [Crepe.Feature.Placeholder]: { text: "Makalenizi yazmaya başlayın..." },
        [Crepe.Feature.ImageBlock]: {
          onUpload: handleUpload,
          inlineOnUpload: handleUpload,
          blockOnUpload: handleUpload,
          proxyDomURL: (url) => url.startsWith("/api/") && token
            ? `${url}${url.includes("?") ? "&" : "?"}token=${token}`
            : url,
        },
      },
    });

    crepe.on((listener) => listener.markdownUpdated((_ctx, markdown) => {
      if (!active) return;
      const currentImages = extractImageUrls(markdown);
      for (const url of previousImages) {
        if (!currentImages.has(url) && (url.startsWith("/api/") || url.startsWith("blob:")))
          void deleteRef.current?.(url);
      }
      previousImages = currentImages;
      onChangeRef.current(markdown);
    }));
    void crepe.create();

    return () => {
      active = false;
      void crepe.destroy();
    };
  }, [token]);

  return <div className="milkdown-shell article-editor-typography min-h-[28rem] overflow-hidden rounded-xl border border-zinc-200 bg-white text-zinc-900 dark:border-zinc-800 dark:bg-zinc-900 dark:text-zinc-100"><div ref={rootRef} /></div>;
}

function extractImageUrls(markdown: string): Set<string> {
  return new Set([...markdown.matchAll(/!\[[^\]]*\]\((?:<([^>]+)>|([^\s)]+))(?:\s+["'][^"']*["'])?\)/g)]
    .map((match) => match[1] || match[2]).filter(Boolean));
}
