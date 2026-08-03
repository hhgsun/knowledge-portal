import { useCallback, useEffect, useId, useMemo, useRef, useState } from "react";
import { Check, ChevronDown, Plus, Search, X } from "lucide-react";
import { useApi } from "../../hooks/useApi";

interface Tag {
  id: string;
  name: string;
  slug: string;
}

interface TagSelectorProps {
  selectedTags: string[];
  onChange: (tagIds: string[]) => void;
}

interface TagPage {
  tags: Tag[];
  total: number;
  page: number;
  totalPages: number;
}

interface CachedTagPage {
  ids: string[];
  page: number;
  total: number;
}

const PAGE_SIZE = 30;
const queryKey = (query: string) => query.trim().toLocaleLowerCase("tr-TR");

export function TagSelector({ selectedTags, onChange }: TagSelectorProps) {
  const { fetchWithAuth } = useApi();
  const listboxId = useId();
  const dropdownRef = useRef<HTMLDivElement>(null);
  const searchInputRef = useRef<HTMLInputElement>(null);
  const requestRef = useRef<AbortController | null>(null);
  const loadingRef = useRef(false);
  const cacheRef = useRef<Map<string, CachedTagPage>>(new Map());
  const [tagsById, setTagsById] = useState<Record<string, Tag>>({});
  const [resultIds, setResultIds] = useState<string[]>([]);
  const [newTagName, setNewTagName] = useState("");
  const [searchQuery, setSearchQuery] = useState("");
  const [showInput, setShowInput] = useState(false);
  const [isOpen, setIsOpen] = useState(false);
  const [page, setPage] = useState(0);
  const [total, setTotal] = useState(0);
  const [isLoading, setIsLoading] = useState(false);

  const loadPage = useCallback(async (nextPage: number, query: string, replace: boolean) => {
    if (loadingRef.current && !replace) return;
    if (replace) requestRef.current?.abort();

    const controller = new AbortController();
    requestRef.current = controller;
    loadingRef.current = true;
    setIsLoading(true);

    const params = new URLSearchParams({
      page: String(nextPage),
      limit: String(PAGE_SIZE),
    });
    if (query.trim()) params.set("q", query.trim());

    try {
      const res = await fetchWithAuth(`/api/tags?${params}`, {
        signal: controller.signal,
        noRetry: true,
      });
      if (!res.ok) return;
      const data = await res.json() as TagPage;
      const key = queryKey(query);
      const cachedIds = cacheRef.current.get(key)?.ids ?? [];
      const nextIds = replace
        ? data.tags.map((tag) => tag.id)
        : [...cachedIds, ...data.tags.map((tag) => tag.id).filter((id) => !cachedIds.includes(id))];
      setTagsById((current) => {
        const next = { ...current };
        data.tags.forEach((tag) => { next[tag.id] = tag; });
        return next;
      });
      cacheRef.current.set(key, { ids: nextIds, page: data.page, total: data.total });
      setResultIds(nextIds);
      setPage(data.page);
      setTotal(data.total);
    } catch (error) {
      if (!(error instanceof DOMException && error.name === "AbortError")) throw error;
    } finally {
      if (requestRef.current === controller) {
        loadingRef.current = false;
        setIsLoading(false);
      }
    }
  }, [fetchWithAuth]);

  useEffect(() => {
    if (!isOpen) return;
    const cached = cacheRef.current.get(queryKey(searchQuery));
    if (cached) {
      requestRef.current?.abort();
      loadingRef.current = false;
      setIsLoading(false);
      setResultIds(cached.ids);
      setPage(cached.page);
      setTotal(cached.total);
      return;
    }

    const timer = window.setTimeout(() => loadPage(1, searchQuery, true), 250);
    return () => window.clearTimeout(timer);
  }, [isOpen, loadPage, searchQuery]);

  useEffect(() => {
    const missingIds = selectedTags.filter((id) => !tagsById[id]);
    if (missingIds.length === 0) return;

    const params = new URLSearchParams({ page: "1", limit: "100" });
    missingIds.slice(0, 100).forEach((id) => params.append("ids", id));
    const controller = new AbortController();
    fetchWithAuth(`/api/tags?${params}`, { signal: controller.signal, noRetry: true })
      .then((res) => res.ok ? res.json() : null)
      .then((data: TagPage | null) => {
        if (!data) return;
        setTagsById((current) => {
          const next = { ...current };
          data.tags.forEach((tag) => { next[tag.id] = tag; });
          return next;
        });
      })
      .catch((error) => {
        if (!(error instanceof DOMException && error.name === "AbortError")) throw error;
      });
    return () => controller.abort();
  }, [fetchWithAuth, selectedTags, tagsById]);

  useEffect(() => () => requestRef.current?.abort(), []);

  useEffect(() => {
    if (!isOpen) return;

    searchInputRef.current?.focus();
    const handlePointerDown = (event: MouseEvent) => {
      if (!dropdownRef.current?.contains(event.target as Node)) {
        setIsOpen(false);
        setSearchQuery("");
      }
    };
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setIsOpen(false);
        setSearchQuery("");
      }
    };

    document.addEventListener("mousedown", handlePointerDown);
    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("mousedown", handlePointerDown);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [isOpen]);

  const handleToggle = (tagId: string) => {
    if (selectedTags.includes(tagId)) {
      onChange(selectedTags.filter((id) => id !== tagId));
    } else {
      onChange([...selectedTags, tagId]);
    }
  };

  const handleCreateTag = async () => {
    if (!newTagName.trim()) return;

    const duplicate = Object.values(tagsById).find(
      (t) => t.name.toLocaleLowerCase("tr-TR") === newTagName.trim().toLocaleLowerCase("tr-TR")
    );
    if (duplicate) {
      if (!selectedTags.includes(duplicate.id)) onChange([...selectedTags, duplicate.id]);
      setNewTagName("");
      setShowInput(false);
      return;
    }

    const res = await fetchWithAuth("/api/tags", {
      method: "POST",
      body: JSON.stringify({ name: newTagName.trim() }),
    });
    if (res.ok) {
      const tag = await res.json();
      cacheRef.current.clear();
      setTagsById((current) => ({ ...current, [tag.id]: tag }));
      setResultIds((current) => current.includes(tag.id) ? current : [tag.id, ...current]);
      if (!selectedTags.includes(tag.id)) onChange([...selectedTags, tag.id]);
      setNewTagName("");
      setShowInput(false);
    }
  };

  const selectedTagObjects = useMemo(
    () => selectedTags.map((id) => tagsById[id]).filter((tag): tag is Tag => Boolean(tag)),
    [selectedTags, tagsById]
  );
  const visibleTags = useMemo(
    () => resultIds.map((id) => tagsById[id]).filter((tag): tag is Tag => Boolean(tag)),
    [resultIds, tagsById]
  );
  const hasMore = resultIds.length < total;

  return (
    <div className="space-y-2">
      {selectedTagObjects.length > 0 && (
        <div className="flex max-h-28 flex-wrap gap-1.5 overflow-y-auto rounded-lg pr-1">
          {selectedTagObjects.map((tag) => (
            <span
              key={tag.id}
              className="inline-flex items-center gap-1 px-2 py-0.5 bg-blue-100 dark:bg-blue-900 text-blue-700 dark:text-blue-300 text-xs rounded-full"
            >
              {tag.name}
              <button
                type="button"
                onClick={() => handleToggle(tag.id)}
                className="hover:text-red-500"
                aria-label={`${tag.name} etiketini kaldır`}
              >
                <X size={12} />
              </button>
            </span>
          ))}
        </div>
      )}

      <div className="flex items-center gap-2 flex-wrap">
        <div ref={dropdownRef} className="relative">
            <button
              type="button"
              onClick={() => setIsOpen((open) => !open)}
              className="flex min-w-36 items-center justify-between gap-2 rounded-lg border border-zinc-300 bg-white px-2.5 py-1.5 text-xs text-zinc-700 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-200"
              aria-haspopup="listbox"
              aria-expanded={isOpen}
              aria-controls={listboxId}
            >
              <span>Etiket seç{selectedTags.length > 0 ? ` (${selectedTags.length})` : ""}</span>
              <ChevronDown size={14} className={`transition-transform ${isOpen ? "rotate-180" : ""}`} />
            </button>

            {isOpen && (
              <div className="absolute left-0 z-30 mt-1 w-72 max-w-[calc(100vw-2rem)] overflow-hidden rounded-lg border border-zinc-200 bg-white shadow-lg dark:border-zinc-700 dark:bg-zinc-900">
                <div className="relative border-b border-zinc-200 p-2 dark:border-zinc-700">
                  <Search size={14} className="absolute left-4 top-1/2 -translate-y-1/2 text-zinc-400" />
                  <input
                    ref={searchInputRef}
                    type="search"
                    value={searchQuery}
                    onChange={(event) => setSearchQuery(event.target.value)}
                    placeholder="Etiket ara..."
                    className="w-full rounded-md border border-zinc-300 bg-white py-1.5 pl-8 pr-2 text-xs outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500 dark:border-zinc-700 dark:bg-zinc-800"
                  />
                </div>
                <div
                  id={listboxId}
                  role="listbox"
                  aria-multiselectable="true"
                  className="max-h-60 overflow-y-auto p-1"
                  onScroll={(event) => {
                    const element = event.currentTarget;
                    if (hasMore && !isLoading && element.scrollHeight - element.scrollTop - element.clientHeight < 48) {
                      loadPage(page + 1, searchQuery, false);
                    }
                  }}
                >
                  {visibleTags.length > 0 ? visibleTags.map((tag) => {
                    const selected = selectedTags.includes(tag.id);
                    return (
                      <button
                        key={tag.id}
                        type="button"
                        role="option"
                        aria-selected={selected}
                        onClick={() => handleToggle(tag.id)}
                        className="flex w-full items-center justify-between gap-3 rounded-md px-2.5 py-2 text-left text-xs text-zinc-700 hover:bg-zinc-100 dark:text-zinc-200 dark:hover:bg-zinc-800"
                      >
                        <span className="truncate" title={tag.name}>{tag.name}</span>
                        {selected && <Check size={14} className="shrink-0 text-blue-600" />}
                      </button>
                    );
                  }) : !isLoading && (
                    <p className="px-3 py-5 text-center text-xs text-zinc-500">Etiket bulunamadı.</p>
                  )}
                  {isLoading && (
                    <p className="px-3 py-3 text-center text-xs text-zinc-500" role="status">Etiketler yükleniyor...</p>
                  )}
                </div>
              </div>
            )}
          </div>

        {showInput ? (
          <div className="flex items-center gap-1">
            <input
              type="text"
              value={newTagName}
              onChange={(e) => setNewTagName(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") {
                  e.preventDefault();
                  handleCreateTag();
                }
              }}
              placeholder="Yeni etiket..."
              className="px-2 py-1 text-xs border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800 w-32"
              autoFocus
            />
            <button type="button" onClick={handleCreateTag} className="text-blue-600 hover:text-blue-700 text-xs font-medium">Ekle</button>
            <button type="button" onClick={() => { setShowInput(false); setNewTagName(""); }} className="text-zinc-400 hover:text-zinc-600 text-xs">İptal</button>
          </div>
        ) : (
          <button
            type="button"
            onClick={() => setShowInput(true)}
            className="flex items-center gap-1 text-xs text-zinc-500 hover:text-blue-600"
          >
            <Plus size={12} />
            Yeni etiket
          </button>
        )}
      </div>
    </div>
  );
}
