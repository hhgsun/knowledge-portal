import { useEffect, useState } from "react";
import { useParams, Link } from "react-router-dom";
import { ArrowLeft, Clock, User, GitCompare, Eye, RotateCcw, X } from "lucide-react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { AuthenticatedImage } from "../components/attachments/authenticated-image";
import { useApi } from "../hooks/useApi";
import { toast } from "sonner";
import { VersionsListSkeleton } from "../components/ui/skeleton";
import { DropdownSelector } from "../components/ui/dropdown-selector";
import type { ArticleVersionListItem } from "../types/api";

interface ArticleInfo {
  id: string;
  title: string;
  slug: string;
}

interface VersionDetail {
  id: string;
  version: number;
  title: string;
  changeSummary: string | null;
  changedBy: string;
  contentMarkdown: string | null;
  createdAt: string;
}

export default function VersionsPage() {
  const params = useParams();
  const { fetchWithAuth } = useApi();
  const [versions, setVersions] = useState<ArticleVersionListItem[]>([]);
  const [article, setArticle] = useState<ArticleInfo | null>(null);
  const [loading, setLoading] = useState(true);
  const [compareA, setCompareA] = useState<string | null>(null);
  const [compareB, setCompareB] = useState<string | null>(null);
  const [diff, setDiff] = useState<{ added: string[]; removed: string[] } | null>(null);
  const [viewingVersion, setViewingVersion] = useState<VersionDetail | null>(null);
  const [restoring, setRestoring] = useState(false);

  useEffect(() => {
    async function load() {
      const artRes = await fetchWithAuth(`/api/articles/${params.slug}`);
      if (artRes.ok) {
        const artData = await artRes.json();
        setArticle(artData);

        const verRes = await fetchWithAuth(`/api/articles/${artData.id}/versions`);
        if (verRes.ok) {
          const verData = await verRes.json();
          setVersions(verData);
        } else {
          toast.error("Failed to load versions");
        }
      } else {
        toast.error("Makale bulunamadı");
      }
      setLoading(false);
    }
    load();
  }, [params.slug, fetchWithAuth]);

  const handleCompare = async () => {
    if (!compareA || !compareB || !versions.length) return;
    const vA = versions.find((v) => v.id === compareA);
    const vB = versions.find((v) => v.id === compareB);
    if (!vA || !vB) return;

    const [resA, resB] = await Promise.all([
      fetchWithAuth(`/api/articles/${article?.id}/versions/${compareA}`),
      fetchWithAuth(`/api/articles/${article?.id}/versions/${compareB}`),
    ]);

    if (resA.ok && resB.ok) {
      const dataA = await resA.json();
      const dataB = await resB.json();
      const textA = dataA.contentMarkdown || "";
      const textB = dataB.contentMarkdown || "";
      setDiff(computeSimpleDiff(textA, textB));
    }
  };

  const handleViewVersion = async (versionId: string) => {
    if (!article) return;
    const res = await fetchWithAuth(`/api/articles/${article.id}/versions/${versionId}`);
    if (res.ok) {
      const data = await res.json();
      setViewingVersion(data);
    } else {
      toast.error("Failed to load version content");
    }
  };

  const handleRestore = async (versionId: string) => {
    if (!article) return;
    setRestoring(true);
    const res = await fetchWithAuth(`/api/articles/${article.id}/versions/${versionId}/restore`, {
      method: "POST",
    });
    if (res.ok) {
      toast.success("Article restored to selected version");
      setViewingVersion(null);
      // Reload versions
      const verRes = await fetchWithAuth(`/api/articles/${article.id}/versions`);
      if (verRes.ok) setVersions(await verRes.json());
    } else {
      const err = await res.json();
      toast.error(err.error || "Failed to restore version");
    }
    setRestoring(false);
  };

  if (loading) {
    return <VersionsListSkeleton />;
  }

  if (!article) {
    return (
      <div className="text-center py-12">
        <p className="text-zinc-500">Makale bulunamadı</p>
      </div>
    );
  }

  return (
    <div className="max-w-4xl mx-auto">
      <div className="flex items-center gap-3 mb-6">
        <Link to={`/articles/${article.slug}`} className="p-2 rounded-lg hover:bg-zinc-100 dark:hover:bg-zinc-800">
          <ArrowLeft size={18} />
        </Link>
        <div>
          <h1 className="text-xl font-bold text-zinc-900 dark:text-zinc-100">Sürüm Geçmişi</h1>
          <p className="text-sm text-zinc-500">{article.title}</p>
        </div>
      </div>

      {versions.length >= 2 && (
        <div className="mb-6 p-4 border border-zinc-200 dark:border-zinc-800 rounded-xl bg-zinc-50 dark:bg-zinc-900">
          <h3 className="text-sm font-medium text-zinc-700 dark:text-zinc-300 mb-3 flex items-center gap-2">
            <GitCompare size={16} />
            Compare Versions
          </h3>
          <div className="flex items-center gap-3 flex-wrap">
            <DropdownSelector label="Sürüm seçin..." options={versions.map(version => ({ value: version.id, label: `v${version.version} — ${version.changeSummary || version.title}` }))} selected={compareA ? [compareA] : []} onChange={values => { setCompareA(values[0] ?? null); setDiff(null); }} searchable={versions.length > 10} clearable />
            <span className="text-zinc-400 text-sm">vs</span>
            <DropdownSelector label="Sürüm seçin..." options={versions.map(version => ({ value: version.id, label: `v${version.version} — ${version.changeSummary || version.title}` }))} selected={compareB ? [compareB] : []} onChange={values => { setCompareB(values[0] ?? null); setDiff(null); }} searchable={versions.length > 10} clearable />
            <button
              onClick={handleCompare}
              disabled={!compareA || !compareB || compareA === compareB}
              className="px-4 py-1.5 text-sm bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white rounded-lg"
            >
              Compare
            </button>
          </div>

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
                  <p className="text-zinc-500 text-center py-2">Metin farkı bulunamadı</p>
                )}
              </div>
            </div>
          )}
        </div>
      )}

      {versions.length === 0 ? (
        <div className="text-center py-8 border border-dashed border-zinc-300 dark:border-zinc-700 rounded-xl">
          <p className="text-zinc-500">Henüz sürüm geçmişi yok</p>
          <p className="text-sm text-zinc-400 mt-1">Makale düzenlendiğinde yeni sürümler oluşturulur</p>
        </div>
      ) : (
        <div className="space-y-3">
          {versions.map((v) => (
            <div key={v.id} className="p-4 border border-zinc-200 dark:border-zinc-800 rounded-xl">
              <div className="flex items-start justify-between">
                <div>
                  <span className="text-sm font-medium text-zinc-900 dark:text-zinc-100">Version {v.version}</span>
                  {v.changeSummary && <p className="text-sm text-zinc-500 mt-0.5">{v.changeSummary}</p>}
                </div>
                <div className="flex items-center gap-2">
                  <button
                    onClick={() => handleViewVersion(v.id)}
                    className="flex items-center gap-1 px-2 py-1 text-xs border border-zinc-300 dark:border-zinc-700 rounded-lg hover:bg-zinc-50 dark:hover:bg-zinc-800"
                  >
                    <Eye size={12} />
                    View
                  </button>
                  {v.version !== versions[0]?.version && (
                    <button
                      onClick={() => handleRestore(v.id)}
                      disabled={restoring}
                      className="flex items-center gap-1 px-2 py-1 text-xs border border-amber-300 dark:border-amber-700 text-amber-700 dark:text-amber-400 rounded-lg hover:bg-amber-50 dark:hover:bg-amber-950 disabled:opacity-50"
                    >
                      <RotateCcw size={12} />
                      Restore
                    </button>
                  )}
                </div>
              </div>
              <div className="flex items-center gap-3 mt-2 text-xs text-zinc-400">
                <span className="flex items-center gap-1">
                  <User size={12} />
                  {v.changedByName || "Unknown"}
                </span>
                <span className="flex items-center gap-1">
                  <Clock size={12} />
                  {new Date(v.createdAt).toLocaleString()}
                </span>
              </div>
            </div>
          ))}
        </div>
      )}

      {viewingVersion && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
          <div className="bg-white dark:bg-zinc-900 rounded-xl border border-zinc-200 dark:border-zinc-800 w-full max-w-3xl max-h-[80vh] flex flex-col mx-4">
            <div className="flex items-center justify-between p-4 border-b border-zinc-200 dark:border-zinc-800">
              <div>
                <h3 className="text-sm font-medium text-zinc-900 dark:text-zinc-100">
                  Version {viewingVersion.version} — {viewingVersion.title}
                </h3>
                <p className="text-xs text-zinc-500 mt-0.5">
                  {new Date(viewingVersion.createdAt).toLocaleString()}
                  {viewingVersion.changeSummary && ` • ${viewingVersion.changeSummary}`}
                </p>
              </div>
              <div className="flex items-center gap-2">
                {viewingVersion.version !== versions[0]?.version && (
                  <button
                    onClick={() => handleRestore(viewingVersion.id)}
                    disabled={restoring}
                    className="flex items-center gap-1 px-3 py-1.5 text-xs bg-amber-600 hover:bg-amber-700 disabled:opacity-50 text-white rounded-lg"
                  >
                    <RotateCcw size={12} />
                    Restore this version
                  </button>
                )}
                <button
                  onClick={() => setViewingVersion(null)}
                  className="p-1.5 rounded-lg hover:bg-zinc-100 dark:hover:bg-zinc-800"
                >
                  <X size={16} />
                </button>
              </div>
            </div>
            <div className="p-4 overflow-y-auto prose dark:prose-invert max-w-none">
              {viewingVersion.contentMarkdown ? (
                <ReactMarkdown remarkPlugins={[remarkGfm]} components={{ img: AuthenticatedImage }}>{viewingVersion.contentMarkdown}</ReactMarkdown>
              ) : (
                <p className="text-zinc-400 italic">Bu sürümde içerik yok.</p>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function computeSimpleDiff(textA: string, textB: string): { added: string[]; removed: string[] } {
  const linesA = new Set(textA.split("\n").map((l) => l.trim()).filter(Boolean));
  const linesB = new Set(textB.split("\n").map((l) => l.trim()).filter(Boolean));
  const removed = [...linesA].filter((l) => !linesB.has(l));
  const added = [...linesB].filter((l) => !linesA.has(l));
  return { added, removed };
}
