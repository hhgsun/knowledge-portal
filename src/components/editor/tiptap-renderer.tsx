"use client";

interface TipTapNode {
  type?: string;
  text?: string;
  content?: TipTapNode[];
  attrs?: Record<string, unknown>;
  marks?: { type: string; attrs?: Record<string, unknown> }[];
}

export function TiptapRenderer({ content }: { content: Record<string, unknown> }) {
  const doc = content as unknown as TipTapNode;
  if (!doc.content) return null;
  return <div className="tiptap-content">{renderNodes(doc.content)}</div>;
}

function renderNodes(nodes: TipTapNode[]): React.ReactNode[] {
  return nodes.map((node, i) => renderNode(node, i));
}

function renderNode(node: TipTapNode, key: number): React.ReactNode {
  if (node.type === "text") return renderText(node, key);

  switch (node.type) {
    case "paragraph":
      return <p key={key}>{node.content ? renderNodes(node.content) : null}</p>;
    case "heading": {
      const level = (node.attrs?.level as number) || 1;
      const Tag = (`h${level}`) as keyof React.JSX.IntrinsicElements;
      return <Tag key={key}>{node.content ? renderNodes(node.content) : null}</Tag>;
    }
    case "bulletList":
      return <ul key={key}>{node.content ? renderNodes(node.content) : null}</ul>;
    case "orderedList":
      return <ol key={key}>{node.content ? renderNodes(node.content) : null}</ol>;
    case "listItem":
      return <li key={key}>{node.content ? renderNodes(node.content) : null}</li>;
    case "taskList":
      return <ul key={key} className="task-list list-none pl-0">{node.content ? renderNodes(node.content) : null}</ul>;
    case "taskItem": {
      const checked = node.attrs?.checked as boolean;
      return (
        <li key={key} className="flex items-start gap-2 list-none">
          <input type="checkbox" checked={checked} readOnly className="mt-1.5" />
          <div>{node.content ? renderNodes(node.content) : null}</div>
        </li>
      );
    }
    case "blockquote":
      return <blockquote key={key}>{node.content ? renderNodes(node.content) : null}</blockquote>;
    case "codeBlock": {
      const language = (node.attrs?.language as string) || "";
      return (
        <pre key={key} className="bg-zinc-900 text-zinc-100 rounded-lg p-4 overflow-x-auto">
          <code data-language={language}>{node.content ? extractPlainText(node.content) : ""}</code>
        </pre>
      );
    }
    case "horizontalRule":
      return <hr key={key} />;
    case "hardBreak":
      return <br key={key} />;
    case "image": {
      const src = node.attrs?.src as string;
      const alt = (node.attrs?.alt as string) || "";
      if (!src) return null;
      // eslint-disable-next-line @next/next/no-img-element
      return <img key={key} src={src} alt={alt} className="rounded-lg max-w-full" />;
    }
    case "table":
      return (
        <div key={key} className="overflow-x-auto">
          <table><tbody>{node.content ? renderNodes(node.content) : null}</tbody></table>
        </div>
      );
    case "tableRow":
      return <tr key={key}>{node.content ? renderNodes(node.content) : null}</tr>;
    case "tableCell":
      return <td key={key}>{node.content ? renderNodes(node.content) : null}</td>;
    case "tableHeader":
      return <th key={key}>{node.content ? renderNodes(node.content) : null}</th>;
    default:
      if (node.content) return <div key={key}>{renderNodes(node.content)}</div>;
      return null;
  }
}

function renderText(node: TipTapNode, key: number): React.ReactNode {
  if (!node.text) return null;
  let element: React.ReactNode = node.text;

  if (node.marks) {
    for (const mark of node.marks) {
      switch (mark.type) {
        case "bold":
        case "strong":
          element = <strong>{element}</strong>;
          break;
        case "italic":
        case "em":
          element = <em>{element}</em>;
          break;
        case "strike":
          element = <s>{element}</s>;
          break;
        case "code":
          element = <code className="bg-zinc-100 dark:bg-zinc-800 px-1.5 py-0.5 rounded text-sm">{element}</code>;
          break;
        case "link": {
          const href = mark.attrs?.href as string;
          element = <a href={href} target="_blank" rel="noopener noreferrer" className="text-blue-600 hover:underline">{element}</a>;
          break;
        }
        case "highlight":
          element = <mark>{element}</mark>;
          break;
      }
    }
  }

  return <span key={key}>{element}</span>;
}

function extractPlainText(nodes: TipTapNode[]): string {
  return nodes.map((n) => n.text || (n.content ? extractPlainText(n.content) : "")).join("");
}
