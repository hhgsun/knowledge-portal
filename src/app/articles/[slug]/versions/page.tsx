"use client";

import { useEffect, useState } from "react";
import { useParams } from "next/navigation";
import Link from "next/link";
import { ArrowLeft, Clock, User, GitCompare } from "lucide-react";
import { cn } from "@/lib/utils";

interface Version {
  id: string;
  version: number;
  title: string;
  changeSummary: string | null;
  changedBy: string;
  changedByName: string | null;
  createdAt: string;
}

interface ArticleInfo {
  id: string;
  title: string;
  slug: string;
}

export default function VersionsPage() {
  const params = useParams();
  const [versions, setVersions] = useState<Version[]>([]);
  const [article, setArticle] = useState<ArticleInfo | null>(null);
  const [loading, setLoading] = useState(true);
  const [compareA, setCompareA] = useState<string | null>(null);
  const [compareB, setCompareB] = useState<string | null>(null);
  const [diff, setDiff] = useState<{ added: string[]; removed: string[] } | null>(null);

  useEffect(() => {
    async function load() {
      // Load article info
      const artRes = await fetch(`/api/articles/${params.slug}`);
      if (artRes.ok) {
        const artData = await artRes.json();
        setArticle(artData);

        // Load versions
        const verRes = await fetch(`/api/articles/${artData.id}/versions`);
        if (verRes.ok) {
          const verData = await verRes.json();
          setVersions(verData);
        }
      }
      setLoading(false);
    }
    load();
  }, [params.slug]);

  const handleCompare = async () => {
    if (!compareA || !compareB || !versions.length) return;
    const vA = versions.find((v) => v.id === compareA);
    const vB = versions.find((v) => v.id === compareB);
    if (!vA || !vB) return;

    // Fetch full version content
    const [resA, resB] = await Promise.all([
      fetch(`/api/articles/${article?.id}/versions/${compareA}`),
      fetch(`/api/articles/${article?.id}/versions/${compareB}`),
    ]);

    if (resA.ok && resB.ok) {
      const dataA = await resA.json();
      const dataB = await resB.json();
      const textA = extractText(dataA.content);
      const textB = extractText(dataB.content);
      setDiff(computeSimpleDiff(textA, textB));
    }
  };

  if (loading) {
    return <div className="text-center py-12 text-zinc-500">Loading...</div>;
  }

  if (!article) {
    return (
      <div className="text-center py-12">
        <p className="text-zinc-500">Article not found</p>
      </div>
    );
  }

  return (
    <div className="max-w-4xl mx-auto">
      {/* Header */}
      <div className="flex items-center gap-3 mb-6">
        <Link
          href={`/articles/${article.slug}`}
          className="p-2 rounded-lg hover:bg-zinc-100 dark:hover:bg-zinc-800"
        >
          <ArrowLeft size={18} />
        </Link>
        <div>
          <h1 className="text-xl font-bold text-zinc-900 dark:text-zinc-100">
            Version History
          </h1>
          <p className="text-sm text-zinc-500">{article.title}</p>
        </div>
      </div>

      {/* Compare Section */}
      {versions.length >= 2 && (
        <div className="mb-6 p-4 border border-zinc-200 dark:border-zinc-800 rounded-xl bg-zinc-50 dark:bg-zinc-900">
          <h3 className="text-sm font-medium text-zinc-700 dark:text-zinc-300 mb-3 flex items-center gap-2">
            <GitCompare size={16} />
            Compare Versions
          </h3>
          <div className="flex items-center gap-3 flex-wrap">
            <select
              value={compareA || ""}
              onChange={(e) => { setCompareA(e.target.value); setDiff(null); }}
              className="px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800"
            >
              <option value="">Select version...</option>
              {versions.map((v) => (
                <option key={v.id} value={v.id}>v{v.version} — {v.changeSummary || v.title}</option>
              ))}
            </select>
            <span className="text-zinc-400 text-sm">vs</span>
            <select
              value={compareB || ""}
              onChange={(e) => { setCompareB(e.target.value); setDiff(null); }}
              className="px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800"
            >
              <option value="">Select version...</option>
              {versions.map((v) => (
                <option key={v.id} value={v.id}>v{v.version} — {v.changeSummary || v.title}</option>
              ))}
            </select>
            <button
              onClick={handleCompare}
              disabled={!compareA || !compareB || compareA === compareB}
              className="px-4 py-1.5 text-sm bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white rounded-lg"
            >
              Compare
            </button>
          </div>

          {/* Diff Output */}
          {diff && (
            <div className="mt-4 border border-zinc-200 dark:border-zinc-700 rounded-lg overflow-hidden">
              <div className="p-3 space-y-1 text-sm font-mono max-h-96 overflow-y-auto">
                {diff.removed.map((line, i) => (
                  <div key={`r-${i}`} className="bg-red-50 dark:bg-red-950 text-red-700 dark:text-red-300 px-2 py-0.5 rounded">
                    - {line}
                  </div>
                ))}
                {diff.added.map((line, i) => (
                  <div key={`a-${i}`} className="bg-green-50 dark:bg-green-950 text-green-700 dark:text-green-300 px-2 py-0.5 rounded">
                    + {line}
                  </div>
                ))}
                {diff.added.length === 0 && diff.removed.length === 0 && (
                  <p className="text-zinc-500 text-center py-2">No text differences found</p>
                )}
              </div>
            </div>
          )}
        </div>
      )}

      {/* Version List */}
      {versions.length === 0 ? (
        <div className="text-center py-8 border border-dashed border-zinc-300 dark:border-zinc-700 rounded-xl">
          <p className="text-zinc-500">No version history yet</p>
          <p className="text-sm text-zinc-400 mt-1">Versions are created when an article is edited</p>
        </div>
      ) : (
        <div className="space-y-3">
          {versions.map((v) => (
            <div
              key={v.id}
              className="p-4 border border-zinc-200 dark:border-zinc-800 rounded-xl"
            >
              <div className="flex items-start justify-between">
                <div>
                  <span className="text-sm font-medium text-zinc-900 dark:text-zinc-100">
                    Version {v.version}
                  </span>
                  {v.changeSummary && (
                    <p className="text-sm text-zinc-500 mt-0.5">{v.changeSummary}</p>
                  )}
                </div>
                <span className="text-xs text-zinc-400 shrink-0">
                  {new Date(v.createdAt).toLocaleString()}
                </span>
              </div>
              <div className="flex items-center gap-3 mt-2 text-xs text-zinc-400">
                <span className="flex items-center gap-1">
                  <User size={12} />
                  {v.changedByName || "Unknown"}
                </span>
                <span className="flex items-center gap-1">
                  <Clock size={12} />
                  {v.title}
                </span>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

/** Extract plain text from TipTap JSON */
function extractText(content: Record<string, unknown> | null): string {
  if (!content) return "";
  const nodes = (content.content || []) as { type?: string; text?: string; content?: unknown[] }[];
  return extractNodesText(nodes);
}

function extractNodesText(nodes: { type?: string; text?: string; content?: unknown[] }[]): string {
  return nodes
    .map((n) => {
      if (n.text) return n.text;
      if (n.content) return extractNodesText(n.content as { type?: string; text?: string; content?: unknown[] }[]);
      return "";
    })
    .join("\n");
}

/** Simple line-based diff */
function computeSimpleDiff(textA: string, textB: string): { added: string[]; removed: string[] } {
  const linesA = new Set(textA.split("\n").map((l) => l.trim()).filter(Boolean));
  const linesB = new Set(textB.split("\n").map((l) => l.trim()).filter(Boolean));

  const removed = [...linesA].filter((l) => !linesB.has(l));
  const added = [...linesB].filter((l) => !linesA.has(l));

  return { added, removed };
}
