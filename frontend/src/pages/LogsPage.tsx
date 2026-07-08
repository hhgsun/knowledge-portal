import { useEffect, useState, useCallback } from "react";
import { useApi } from "../hooks/useApi";
import { toast } from "sonner";
import { FileText, Trash2, RefreshCw, Download, Clock, HardDrive, Search, ChevronUp, ChevronDown, X, WrapText } from "lucide-react";
import { cn } from "../lib/utils";

interface LogFile {
  fileName: string;
  sizeBytes: number;
  createdAt: string;
  lastModifiedAt: string;
  isToday: boolean;
  canDelete: boolean;
}

interface LogContent {
  fileName: string;
  totalLines: number;
  returnedLines: number;
  content: string;
}

export default function LogsPage() {
  const { fetchWithAuth } = useApi();
  const [files, setFiles] = useState<LogFile[]>([]);
  const [selectedFile, setSelectedFile] = useState<string | null>(null);
  const [logContent, setLogContent] = useState<LogContent | null>(null);
  const [loading, setLoading] = useState(true);
  const [contentLoading, setContentLoading] = useState(false);
  const [tail, setTail] = useState<number>(200);
  const [searchTerm, setSearchTerm] = useState("");
  const [matchIndices, setMatchIndices] = useState<number[]>([]);
  const [currentMatchIdx, setCurrentMatchIdx] = useState(0);
  const [wordWrap, setWordWrap] = useState(false);

  const loadFiles = useCallback(async () => {
    setLoading(true);
    try {
      const res = await fetchWithAuth("/api/logs");
      if (res.ok) {
        const data = await res.json();
        setFiles(data.files);
        if (data.files.length > 0 && !selectedFile) {
          setSelectedFile(data.files[0].fileName);
        }
      }
    } catch {
      // handled by useApi
    } finally {
      setLoading(false);
    }
  }, [fetchWithAuth, selectedFile]);

  const loadContent = useCallback(async (fileName: string) => {
    setContentLoading(true);
    try {
      const res = await fetchWithAuth(`/api/logs/${fileName}?tail=${tail}`);
      if (res.ok) {
        const data = await res.json();
        setLogContent(data);
      }
    } catch {
      // handled by useApi
    } finally {
      setContentLoading(false);
    }
  }, [fetchWithAuth, tail]);

  useEffect(() => {
    loadFiles();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (selectedFile) {
      loadContent(selectedFile);
    }
  }, [selectedFile, loadContent]);

  const handleDelete = async (fileName: string) => {
    if (!confirm(`"${fileName}" dosyası silinecek. Emin misiniz?`)) return;

    try {
      const res = await fetchWithAuth(`/api/logs/${fileName}`, { method: "DELETE" });
      if (res.ok) {
        toast.success("Log dosyası silindi");
        setFiles((prev) => prev.filter((f) => f.fileName !== fileName));
        if (selectedFile === fileName) {
          setSelectedFile(null);
          setLogContent(null);
        }
      } else {
        const data = await res.json();
        toast.error(data.error || "Silinemedi");
      }
    } catch {
      // handled by useApi
    }
  };

  const formatSize = (bytes: number) => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  // Search logic
  const lines = logContent?.content?.split("\n") || [];

  useEffect(() => {
    if (!searchTerm.trim() || lines.length === 0) {
      setMatchIndices([]);
      setCurrentMatchIdx(0);
      return;
    }
    const term = searchTerm.toLowerCase();
    const indices = lines.reduce<number[]>((acc, line, idx) => {
      if (line.toLowerCase().includes(term)) acc.push(idx);
      return acc;
    }, []);
    setMatchIndices(indices);
    setCurrentMatchIdx(0);
  }, [searchTerm, logContent]);

  useEffect(() => {
    if (matchIndices.length > 0) {
      const row = document.getElementById(`log-line-${matchIndices[currentMatchIdx]}`);
      row?.scrollIntoView({ block: "center", behavior: "smooth" });
    }
  }, [currentMatchIdx, matchIndices]);

  const goToNextMatch = () => {
    if (matchIndices.length === 0) return;
    setCurrentMatchIdx((prev) => (prev + 1) % matchIndices.length);
  };

  const goToPrevMatch = () => {
    if (matchIndices.length === 0) return;
    setCurrentMatchIdx((prev) => (prev - 1 + matchIndices.length) % matchIndices.length);
  };

  const highlightLine = (line: string) => {
    if (!searchTerm.trim()) return line || "\u00A0";
    const term = searchTerm.toLowerCase();
    const idx = line.toLowerCase().indexOf(term);
    if (idx === -1) return line || "\u00A0";
    const parts: React.ReactNode[] = [];
    let lastIdx = 0;
    let pos = line.toLowerCase().indexOf(term, 0);
    while (pos !== -1) {
      if (pos > lastIdx) parts.push(line.slice(lastIdx, pos));
      parts.push(
        <mark key={pos} className="bg-yellow-400/80 text-zinc-900 rounded-sm px-0.5">
          {line.slice(pos, pos + searchTerm.length)}
        </mark>
      );
      lastIdx = pos + searchTerm.length;
      pos = line.toLowerCase().indexOf(term, lastIdx);
    }
    if (lastIdx < line.length) parts.push(line.slice(lastIdx));
    return <>{parts}</>;
  };

  const handleDownload = (fileName: string) => {
    if (!logContent) return;
    const blob = new Blob([logContent.content], { type: "text/plain" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = fileName;
    a.click();
    URL.revokeObjectURL(url);
  };

  return (
    <div className="max-w-7xl mx-auto space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-zinc-900 dark:text-zinc-100">
            Sistem Logları
          </h1>
          <p className="text-sm text-zinc-500 dark:text-zinc-400 mt-1">
            Tarih bazlı log dosyalarını görüntüle ve yönet
          </p>
        </div>
        <button
          onClick={loadFiles}
          disabled={loading}
          className="flex items-center gap-2 px-3 py-2 text-sm rounded-lg border border-zinc-200 dark:border-zinc-700 hover:bg-zinc-50 dark:hover:bg-zinc-800 transition-colors"
        >
          <RefreshCw size={16} className={cn(loading && "animate-spin")} />
          Yenile
        </button>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-4 gap-6">
        {/* File List */}
        <div className="lg:col-span-1 space-y-2">
          <h2 className="text-sm font-semibold text-zinc-700 dark:text-zinc-300 mb-3">
            Log Dosyaları ({files.length})
          </h2>
          {loading ? (
            <div className="space-y-2">
              {[1, 2, 3].map((i) => (
                <div key={i} className="h-16 bg-zinc-100 dark:bg-zinc-800 rounded-lg animate-pulse" />
              ))}
            </div>
          ) : files.length === 0 ? (
            <p className="text-sm text-zinc-500 dark:text-zinc-400">
              Henüz log dosyası yok
            </p>
          ) : (
            <div className="space-y-2 max-h-[70vh] overflow-y-auto pr-1">
              {files.map((file) => (
                <div
                  key={file.fileName}
                  className={cn(
                    "p-3 rounded-lg border cursor-pointer transition-colors",
                    selectedFile === file.fileName
                      ? "border-blue-500 bg-blue-50 dark:bg-blue-900/20 dark:border-blue-500"
                      : "border-zinc-200 dark:border-zinc-700 hover:bg-zinc-50 dark:hover:bg-zinc-800/50"
                  )}
                  onClick={() => setSelectedFile(file.fileName)}
                >
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-2 min-w-0">
                      <FileText size={14} className="text-zinc-400 shrink-0" />
                      <span className="text-sm font-medium text-zinc-900 dark:text-zinc-100 truncate">
                        {file.fileName}
                      </span>
                    </div>
                    {file.isToday && (
                      <span className="text-[10px] font-medium px-1.5 py-0.5 rounded bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400 shrink-0">
                        Bugün
                      </span>
                    )}
                  </div>
                  <div className="flex items-center justify-between mt-1.5">
                    <span className="text-xs text-zinc-500 dark:text-zinc-400 flex items-center gap-1">
                      <HardDrive size={10} />
                      {formatSize(file.sizeBytes)}
                    </span>
                    {file.canDelete && (
                      <button
                        onClick={(e) => {
                          e.stopPropagation();
                          handleDelete(file.fileName);
                        }}
                        title="Sil"
                        className="p-1 text-red-500 hover:bg-red-50 dark:hover:bg-red-900/20 rounded transition-colors"
                      >
                        <Trash2 size={13} />
                      </button>
                    )}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Log Content */}
        <div className="lg:col-span-3">
          {selectedFile && logContent ? (
            <div className="space-y-3">
              <div className="flex items-center justify-between flex-wrap gap-2">
                <div className="flex items-center gap-3">
                  <h2 className="text-sm font-semibold text-zinc-700 dark:text-zinc-300">
                    {selectedFile}
                  </h2>
                  <span className="text-xs text-zinc-500 dark:text-zinc-400 flex items-center gap-1">
                    <Clock size={11} />
                    {logContent.totalLines} satır
                  </span>
                </div>
                <div className="flex items-center gap-2">
                  <button
                    onClick={() => setWordWrap(!wordWrap)}
                    title={wordWrap ? "Satır kaydırmayı kapat" : "Satır kaydırmayı aç"}
                    className={cn(
                      "flex items-center gap-1.5 px-2.5 py-1.5 text-xs rounded border transition-colors",
                      wordWrap
                        ? "border-blue-500 bg-blue-50 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400 dark:border-blue-500"
                        : "border-zinc-200 dark:border-zinc-700 hover:bg-zinc-50 dark:hover:bg-zinc-800"
                    )}
                  >
                    <WrapText size={12} />
                    Wrap
                  </button>
                  <select
                    value={tail}
                    onChange={(e) => setTail(Number(e.target.value))}
                    className="text-xs px-2 py-1.5 rounded border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-zinc-700 dark:text-zinc-300"
                  >
                    <option value={100}>Son 100 satır</option>
                    <option value={200}>Son 200 satır</option>
                    <option value={500}>Son 500 satır</option>
                    <option value={1000}>Son 1000 satır</option>
                    <option value={0}>Tümü</option>
                  </select>
                  <button
                    onClick={() => loadContent(selectedFile)}
                    disabled={contentLoading}
                    className="flex items-center gap-1.5 px-2.5 py-1.5 text-xs rounded border border-zinc-200 dark:border-zinc-700 hover:bg-zinc-50 dark:hover:bg-zinc-800 transition-colors"
                  >
                    <RefreshCw size={12} className={cn(contentLoading && "animate-spin")} />
                    Yenile
                  </button>
                  <button
                    onClick={() => handleDownload(selectedFile)}
                    className="flex items-center gap-1.5 px-2.5 py-1.5 text-xs rounded border border-zinc-200 dark:border-zinc-700 hover:bg-zinc-50 dark:hover:bg-zinc-800 transition-colors"
                  >
                    <Download size={12} />
                    İndir
                  </button>
                </div>
              </div>
              {/* Search Bar */}
              <div className="flex items-center gap-2">
                <div className="relative flex-1 max-w-sm">
                  <Search size={14} className="absolute left-2.5 top-1/2 -translate-y-1/2 text-zinc-400" />
                  <input
                    type="text"
                    value={searchTerm}
                    onChange={(e) => setSearchTerm(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === "Enter") e.shiftKey ? goToPrevMatch() : goToNextMatch();
                      if (e.key === "Escape") setSearchTerm("");
                    }}
                    placeholder="Log içinde ara..."
                    className="w-full pl-8 pr-8 py-1.5 text-xs rounded border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-900 text-zinc-700 dark:text-zinc-300 placeholder:text-zinc-400"
                  />
                  {searchTerm && (
                    <button
                      onClick={() => setSearchTerm("")}
                      className="absolute right-2 top-1/2 -translate-y-1/2 text-zinc-400 hover:text-zinc-600"
                    >
                      <X size={13} />
                    </button>
                  )}
                </div>
                {searchTerm && (
                  <div className="flex items-center gap-1.5">
                    <span className="text-xs text-zinc-500 dark:text-zinc-400 whitespace-nowrap">
                      {matchIndices.length > 0
                        ? `${currentMatchIdx + 1} / ${matchIndices.length}`
                        : "Sonuç yok"}
                    </span>
                    <button
                      onClick={goToPrevMatch}
                      disabled={matchIndices.length === 0}
                      className="p-1 rounded hover:bg-zinc-100 dark:hover:bg-zinc-800 disabled:opacity-30"
                      title="Önceki (Shift+Enter)"
                    >
                      <ChevronUp size={14} />
                    </button>
                    <button
                      onClick={goToNextMatch}
                      disabled={matchIndices.length === 0}
                      className="p-1 rounded hover:bg-zinc-100 dark:hover:bg-zinc-800 disabled:opacity-30"
                      title="Sonraki (Enter)"
                    >
                      <ChevronDown size={14} />
                    </button>
                  </div>
                )}
              </div>

              <div className="relative">
                {contentLoading && (
                  <div className="absolute inset-0 bg-white/50 dark:bg-zinc-900/50 flex items-center justify-center z-10 rounded-lg">
                    <RefreshCw size={20} className="animate-spin text-blue-500" />
                  </div>
                )}
                <div className="bg-zinc-900 dark:bg-zinc-950 rounded-lg text-xs font-mono overflow-x-auto max-h-[70vh] overflow-y-auto border border-zinc-800">
                  {lines.length > 0 ? (
                    <table className="w-full">
                      <tbody>
                        {lines.map((line, idx) => {
                          const realLineNum = logContent.totalLines - logContent.returnedLines + idx + 1;
                          const isMatch = matchIndices.includes(idx);
                          const isCurrentMatch = matchIndices[currentMatchIdx] === idx;
                          return (
                            <tr
                              key={idx}
                              id={`log-line-${idx}`}
                              className={cn(
                                "hover:bg-zinc-800/50",
                                isMatch && "bg-yellow-900/20",
                                isCurrentMatch && "bg-yellow-900/40"
                              )}
                            >
                              <td className={cn(
                                "px-3 py-0.5 text-right select-none border-r border-zinc-800 sticky left-0 w-[1%] whitespace-nowrap",
                                isCurrentMatch ? "bg-yellow-900/40 text-yellow-400" : "bg-zinc-900 dark:bg-zinc-950 text-zinc-500"
                              )}>
                                {realLineNum}
                              </td>
                              <td className={cn(
                                "px-3 py-0.5 text-zinc-100",
                                wordWrap ? "whitespace-pre-wrap break-all" : "whitespace-pre"
                              )}>
                                {highlightLine(line)}
                              </td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  ) : (
                    <p className="p-4 text-zinc-500">(boş)</p>
                  )}
                </div>
              </div>
            </div>
          ) : (
            <div className="flex items-center justify-center h-64 text-zinc-400 dark:text-zinc-600">
              <div className="text-center">
                <FileText size={40} className="mx-auto mb-2 opacity-50" />
                <p className="text-sm">Görüntülemek için bir log dosyası seçin</p>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
