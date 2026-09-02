import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Plus, Tag, X } from "lucide-react";
import { useApi } from "../../hooks/useApi";
import { DropdownSelector } from "../ui/dropdown-selector";

interface Tag {
  id: string;
  name: string;
  slug: string;
  pending?: boolean;
}

interface TagSelectorProps {
  selectedTags: string[];
  onChange: (tagIds: string[]) => void;
  valueField?: "id" | "slug";
  allowCreate?: boolean;
  hideSelectedTags?: boolean;
  label?: string;
  compact?: boolean;
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

export function TagSelector({ selectedTags, onChange, valueField = "id", allowCreate = true, hideSelectedTags = false, label = "Etiket seç", compact = false }: TagSelectorProps) {
  const { fetchWithAuth } = useApi();
  const requestRef = useRef<AbortController | null>(null);
  const loadingRef = useRef(false);
  const cacheRef = useRef<Map<string, CachedTagPage>>(new Map());
  const [tagsById, setTagsById] = useState<Record<string, Tag>>({});
  const [resultIds, setResultIds] = useState<string[]>([]);
  const [searchQuery, setSearchQuery] = useState("");
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

    const params = new URLSearchParams({ page: String(nextPage), limit: String(PAGE_SIZE) });
    if (query.trim()) params.set("q", query.trim());

    try {
      const res = await fetchWithAuth(`/api/tags?${params}`, { signal: controller.signal, noRetry: true });
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
    const knownValues = new Set(Object.values(tagsById).map((tag) => tag[valueField]));
    const missingValues = selectedTags.filter((value) => !knownValues.has(value));
    if (missingValues.length === 0) return;

    const params = new URLSearchParams({ page: "1", limit: "100" });
    missingValues.slice(0, 100).forEach((value) => params.append(valueField === "id" ? "ids" : "slugs", value));
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
  }, [fetchWithAuth, selectedTags, tagsById, valueField]);

  useEffect(() => () => requestRef.current?.abort(), []);

  const handleToggle = (tag: Tag) => {
    const value = tag[valueField];
    onChange(selectedTags.includes(value)
      ? selectedTags.filter((selectedValue) => selectedValue !== value)
      : [...selectedTags, value]);
  };

  const handleCreateTag = () => {
    const name = searchQuery.trim();
    if (!name || name.length > 50) return;
    const duplicate = Object.values(tagsById).find((tag) => queryKey(tag.name) === queryKey(name));
    if (duplicate) {
      const duplicateValue = duplicate[valueField];
      if (!selectedTags.includes(duplicateValue)) onChange([...selectedTags, duplicateValue]);
      setSearchQuery("");
      return;
    }

    const pendingTag: Tag = { id: name, name, slug: "", pending: true };
    setTagsById((current) => ({ ...current, [name]: pendingTag }));
    if (!selectedTags.includes(name)) onChange([...selectedTags, name]);
    setSearchQuery("");
  };

  const selectedTagObjects = useMemo(
    () => selectedTags
      .map((value) => Object.values(tagsById).find((tag) => tag[valueField] === value))
      .filter((tag): tag is Tag => Boolean(tag)),
    [selectedTags, tagsById, valueField],
  );
  const visibleTags = useMemo(
    () => resultIds.map((id) => tagsById[id]).filter((tag): tag is Tag => Boolean(tag)),
    [resultIds, tagsById],
  );
  const hasMore = resultIds.length < total;
  const normalizedSearch = queryKey(searchQuery);
  const hasExactMatch = visibleTags.some((tag) => queryKey(tag.name) === normalizedSearch);
  const showCreateAction = allowCreate && normalizedSearch.length > 0 && searchQuery.trim().length <= 50 && !hasExactMatch && !isLoading;

  return (
    <div className="space-y-2">
      <DropdownSelector
        label={label}
        options={visibleTags.map((tag) => ({ value: tag[valueField], label: tag.name }))}
        selected={selectedTags}
        onChange={onChange}
        multiple
        compact={compact}
        leadingIcon={<Tag size={13} className="shrink-0 opacity-70" aria-hidden="true" />}
        searchable
        searchValue={searchQuery}
        onSearchChange={setSearchQuery}
        onSearchSubmit={showCreateAction ? handleCreateTag : undefined}
        filterOptions={false}
        loading={isLoading}
        loadingText="Etiketler yükleniyor..."
        emptyText="Etiket bulunamadı."
        onOpenChange={setIsOpen}
        onListScroll={(event) => {
          const element = event.currentTarget;
          if (hasMore && !isLoading && element.scrollHeight - element.scrollTop - element.clientHeight < 48) {
            void loadPage(page + 1, searchQuery, false);
          }
        }}
        beforeOptions={showCreateAction ? (
          <button type="button" onClick={handleCreateTag} className="flex w-full items-center gap-2 rounded-md px-2.5 py-2 text-left text-xs font-medium text-blue-600 hover:bg-blue-50 dark:text-blue-400 dark:hover:bg-blue-950/50">
            <Plus size={14} className="shrink-0" />
            <span className="truncate">{`“${searchQuery.trim()}” yeni etiket olarak ekle`}</span>
          </button>
        ) : undefined}
        afterOptions={searchQuery.trim().length > 50
          ? <p className="px-3 py-2 text-xs text-red-500">Etiket adı en fazla 50 karakter olabilir.</p>
          : undefined}
      />

      {selectedTagObjects.length > 0 && !hideSelectedTags && (
        <div className="subtle-scrollbar flex max-h-28 flex-wrap gap-1.5 overflow-y-auto rounded-lg pr-1">
          {selectedTagObjects.map((tag) => (
            <span key={tag.id} className="inline-flex items-center gap-1 rounded-full bg-indigo-50 px-2.5 py-1 text-xs text-indigo-600 dark:bg-indigo-950 dark:text-indigo-400">
              {tag.name}
              {tag.pending && <span className="text-[10px] font-medium text-blue-500 dark:text-blue-400">Yeni</span>}
              <button type="button" onClick={() => handleToggle(tag)} className="hover:text-red-500" aria-label={`${tag.name} etiketini kaldır`}>
                <X size={12} />
              </button>
            </span>
          ))}
        </div>
      )}
    </div>
  );
}
