import { useEditor, EditorContent } from "@tiptap/react";
import StarterKit from "@tiptap/starter-kit";
import Placeholder from "@tiptap/extension-placeholder";
import Link from "@tiptap/extension-link";
import TaskList from "@tiptap/extension-task-list";
import TaskItem from "@tiptap/extension-task-item";
import Highlight from "@tiptap/extension-highlight";
import Image from "@tiptap/extension-image";
import { useRef, useCallback, useMemo } from "react";
import { toast } from "sonner";
import { useAuth } from "../../contexts/AuthContext";
import {
  Bold,
  Italic,
  Strikethrough,
  Code,
  Heading1,
  Heading2,
  Heading3,
  List,
  ListOrdered,
  Quote,
  Minus,
  Undo,
  Redo,
  CheckSquare,
  Highlighter,
  ImageIcon,
} from "lucide-react";
import { cn } from "../../lib/utils";

interface TiptapEditorProps {
  content: Record<string, unknown> | null;
  onChange: (json: Record<string, unknown>) => void;
  articleId?: string;
  uploadImage?: (file: File) => Promise<string | null>;
  deleteImage?: (src: string) => Promise<void>;
  /** When true, image toolbar button works without articleId (deferred upload mode) */
  deferredUpload?: boolean;
}

export default function TiptapEditor({ content, onChange, articleId, uploadImage, deleteImage, deferredUpload }: TiptapEditorProps) {
  const { token } = useAuth();
  const imageInputRef = useRef<HTMLInputElement>(null);

  // Track current image srcs to detect removals
  const prevImageSrcsRef = useRef<Set<string>>(new Set());
  const deleteImageRef = useRef(deleteImage);
  deleteImageRef.current = deleteImage;

  // Custom Image extension that appends auth token to /api/ image URLs
  const AuthImage = useMemo(() => Image.extend({
    renderHTML({ HTMLAttributes }) {
      let src = HTMLAttributes.src as string;
      if (src && src.startsWith("/api/") && token) {
        src = `${src}${src.includes("?") ? "&" : "?"}token=${token}`;
      }
      return ["img", { ...HTMLAttributes, src }];
    },
  }).configure({
    inline: false,
    allowBase64: false,
  }), [token]);

  const uploadRef = useRef(uploadImage);
  uploadRef.current = uploadImage;
  const editorRef = useRef<ReturnType<typeof useEditor>>(null);

  const handleImageUpload = useCallback(async (file: File) => {
    if (!uploadRef.current) {
      toast.error("Image upload not available");
      return;
    }
    const url = await uploadRef.current(file);
    if (url && editorRef.current) {
      editorRef.current.chain().focus().setImage({ src: url }).run();
    }
  }, []);

  const editor = useEditor({
    extensions: [
      StarterKit.configure({
        heading: { levels: [1, 2, 3] },
      }),
      Placeholder.configure({
        placeholder: "Start writing your article...",
      }),
      Link.configure({
        openOnClick: false,
      }),
      TaskList,
      TaskItem.configure({
        nested: true,
      }),
      Highlight,
      AuthImage,
    ],
    content: content || undefined,
    onUpdate: ({ editor }) => {
      const json = editor.getJSON() as Record<string, unknown>;
      onChange(json);

      // Detect removed images and delete from backend (only for already-uploaded images)
      const currentSrcs = extractImageSrcs(json);
      const prevSrcs = prevImageSrcsRef.current;
      for (const src of prevSrcs) {
        if (!currentSrcs.has(src) && src.startsWith("/api/") && deleteImageRef.current) {
          deleteImageRef.current(src);
        }
      }
      prevImageSrcsRef.current = currentSrcs;
    },
    editorProps: {
      attributes: {
        class:
          "prose dark:prose-invert max-w-none focus:outline-none min-h-[300px] px-4 py-3",
      },
      handleDrop: (_view, event, _slice, moved) => {
        if (!moved && event.dataTransfer?.files?.length) {
          const file = event.dataTransfer.files[0];
          if (file.type.startsWith("image/")) {
            event.preventDefault();
            handleImageUpload(file);
            return true;
          }
        }
        return false;
      },
      handlePaste: (_view, event) => {
        const items = event.clipboardData?.items;
        if (items) {
          for (const item of items) {
            if (item.type.startsWith("image/")) {
              event.preventDefault();
              const file = item.getAsFile();
              if (file) handleImageUpload(file);
              return true;
            }
          }
        }
        return false;
      },
    },
  });

  editorRef.current = editor;

  // Initialize image tracking on first render / content load
  if (editor && prevImageSrcsRef.current.size === 0 && content) {
    prevImageSrcsRef.current = extractImageSrcs(content);
  }

  if (!editor) return null;

  return (
    <div className="border border-zinc-200 dark:border-zinc-800 rounded-xl overflow-hidden">
      {/* Toolbar */}
      <div className="flex flex-wrap items-center gap-0.5 p-2 border-b border-zinc-200 dark:border-zinc-800 bg-zinc-50 dark:bg-zinc-900">
        <ToolbarButton
          onClick={() => editor.chain().focus().toggleBold().run()}
          active={editor.isActive("bold")}
          title="Bold"
        >
          <Bold size={16} />
        </ToolbarButton>
        <ToolbarButton
          onClick={() => editor.chain().focus().toggleItalic().run()}
          active={editor.isActive("italic")}
          title="Italic"
        >
          <Italic size={16} />
        </ToolbarButton>
        <ToolbarButton
          onClick={() => editor.chain().focus().toggleStrike().run()}
          active={editor.isActive("strike")}
          title="Strikethrough"
        >
          <Strikethrough size={16} />
        </ToolbarButton>
        <ToolbarButton
          onClick={() => editor.chain().focus().toggleCode().run()}
          active={editor.isActive("code")}
          title="Inline code"
        >
          <Code size={16} />
        </ToolbarButton>
        <ToolbarButton
          onClick={() => editor.chain().focus().toggleHighlight().run()}
          active={editor.isActive("highlight")}
          title="Highlight"
        >
          <Highlighter size={16} />
        </ToolbarButton>

        <div className="w-px h-5 bg-zinc-200 dark:bg-zinc-700 mx-1" />

        <ToolbarButton
          onClick={() => editor.chain().focus().toggleHeading({ level: 1 }).run()}
          active={editor.isActive("heading", { level: 1 })}
          title="Heading 1"
        >
          <Heading1 size={16} />
        </ToolbarButton>
        <ToolbarButton
          onClick={() => editor.chain().focus().toggleHeading({ level: 2 }).run()}
          active={editor.isActive("heading", { level: 2 })}
          title="Heading 2"
        >
          <Heading2 size={16} />
        </ToolbarButton>
        <ToolbarButton
          onClick={() => editor.chain().focus().toggleHeading({ level: 3 }).run()}
          active={editor.isActive("heading", { level: 3 })}
          title="Heading 3"
        >
          <Heading3 size={16} />
        </ToolbarButton>

        <div className="w-px h-5 bg-zinc-200 dark:bg-zinc-700 mx-1" />

        <ToolbarButton
          onClick={() => editor.chain().focus().toggleBulletList().run()}
          active={editor.isActive("bulletList")}
          title="Bullet list"
        >
          <List size={16} />
        </ToolbarButton>
        <ToolbarButton
          onClick={() => editor.chain().focus().toggleOrderedList().run()}
          active={editor.isActive("orderedList")}
          title="Ordered list"
        >
          <ListOrdered size={16} />
        </ToolbarButton>
        <ToolbarButton
          onClick={() => editor.chain().focus().toggleTaskList().run()}
          active={editor.isActive("taskList")}
          title="Task list"
        >
          <CheckSquare size={16} />
        </ToolbarButton>

        <div className="w-px h-5 bg-zinc-200 dark:bg-zinc-700 mx-1" />

        <ToolbarButton
          onClick={() => editor.chain().focus().toggleBlockquote().run()}
          active={editor.isActive("blockquote")}
          title="Quote"
        >
          <Quote size={16} />
        </ToolbarButton>
        <ToolbarButton
          onClick={() => editor.chain().focus().setHorizontalRule().run()}
          title="Divider"
        >
          <Minus size={16} />
        </ToolbarButton>
        <ToolbarButton
          onClick={() => editor.chain().focus().toggleCodeBlock().run()}
          active={editor.isActive("codeBlock")}
          title="Code block"
        >
          <Code size={16} />
        </ToolbarButton>

        <div className="w-px h-5 bg-zinc-200 dark:bg-zinc-700 mx-1" />

        <ToolbarButton
          onClick={() => {
            if (!deferredUpload && (!articleId || !uploadImage)) {
              toast.error("Save the article first to upload images");
              return;
            }
            imageInputRef.current?.click();
          }}
          title="Insert image"
          disabled={!deferredUpload && !articleId}
        >
          <ImageIcon size={16} />
        </ToolbarButton>
        <input
          ref={imageInputRef}
          type="file"
          accept="image/png,image/jpeg,image/gif,image/webp,image/svg+xml"
          className="hidden"
          onChange={(e) => {
            const file = e.target.files?.[0];
            if (file) handleImageUpload(file);
            e.target.value = "";
          }}
        />

        <div className="w-px h-5 bg-zinc-200 dark:bg-zinc-700 mx-1" />

        <ToolbarButton
          onClick={() => editor.chain().focus().undo().run()}
          disabled={!editor.can().undo()}
          title="Undo"
        >
          <Undo size={16} />
        </ToolbarButton>
        <ToolbarButton
          onClick={() => editor.chain().focus().redo().run()}
          disabled={!editor.can().redo()}
          title="Redo"
        >
          <Redo size={16} />
        </ToolbarButton>
      </div>

      {/* Editor Content */}
      <EditorContent editor={editor} />
    </div>
  );
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
function extractImageSrcs(json: Record<string, any>): Set<string> {
  const srcs = new Set<string>();
  function walk(node: Record<string, any>) {
    if (node.type === "image" && node.attrs?.src) {
      srcs.add(node.attrs.src as string);
    }
    if (node.content && Array.isArray(node.content)) {
      for (const child of node.content) walk(child);
    }
  }
  walk(json);
  return srcs;
}

function ToolbarButton({
  children,
  onClick,
  active,
  disabled,
  title,
}: {
  children: React.ReactNode;
  onClick?: () => void;
  active?: boolean;
  disabled?: boolean;
  title?: string;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      title={title}
      className={cn(
        "p-1.5 rounded hover:bg-zinc-200 dark:hover:bg-zinc-700 transition-colors disabled:opacity-30",
        active && "bg-zinc-200 dark:bg-zinc-700 text-blue-600"
      )}
    >
      {children}
    </button>
  );
}
