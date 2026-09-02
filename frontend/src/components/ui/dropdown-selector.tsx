import {
  useEffect,
  useId,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
  type ReactNode,
  type UIEvent,
} from "react";
import { Check, ChevronDown, Search } from "lucide-react";
import { cn } from "../../lib/utils";

export interface DropdownOption {
  value: string;
  label: string;
  searchText?: string;
  disabled?: boolean;
}

interface DropdownSelectorProps {
  label: string;
  options: DropdownOption[];
  selected: string[];
  onChange: (values: string[]) => void;
  multiple?: boolean;
  disabled?: boolean;
  clearable?: boolean;
  emptySelectionLabel?: string;
  searchable?: boolean;
  searchPlaceholder?: string;
  searchValue?: string;
  onSearchChange?: (value: string) => void;
  onSearchSubmit?: () => void;
  filterOptions?: boolean;
  loading?: boolean;
  loadingText?: string;
  emptyText?: string;
  leadingIcon?: ReactNode;
  renderOption?: (option: DropdownOption, selected: boolean) => ReactNode;
  renderValue?: (selectedOptions: DropdownOption[]) => ReactNode;
  beforeOptions?: ReactNode;
  afterOptions?: ReactNode;
  onListScroll?: (event: UIEvent<HTMLDivElement>) => void;
  onOpenChange?: (open: boolean) => void;
  id?: string;
  ariaDescribedBy?: string;
  title?: string;
  className?: string;
  triggerClassName?: string;
  panelClassName?: string;
  panelAlign?: "start" | "end";
  triggerStyle?: CSSProperties;
  compact?: boolean;
}

/**
 * Shared dropdown interaction and visual shell for every domain selector.
 * Data fetching and domain-specific option rendering stay in small wrappers.
 */
export function DropdownSelector({
  label,
  options,
  selected,
  onChange,
  multiple = false,
  disabled = false,
  clearable = false,
  emptySelectionLabel = "Seçilmedi",
  searchable = false,
  searchPlaceholder,
  searchValue,
  onSearchChange,
  onSearchSubmit,
  filterOptions = true,
  loading = false,
  loadingText = "Yükleniyor...",
  emptyText = "Değer bulunamadı.",
  leadingIcon,
  renderOption,
  renderValue,
  beforeOptions,
  afterOptions,
  onListScroll,
  onOpenChange,
  id,
  ariaDescribedBy,
  title,
  className,
  triggerClassName,
  panelClassName,
  panelAlign = "start",
  triggerStyle,
  compact = false,
}: DropdownSelectorProps) {
  const [open, setOpen] = useState(false);
  const [internalSearch, setInternalSearch] = useState("");
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const searchRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const generatedId = useId();
  const listboxId = `${id ?? generatedId}-listbox`;
  const query = searchValue ?? internalSearch;

  const updateSearch = (value: string) => {
    if (onSearchChange) onSearchChange(value);
    else setInternalSearch(value);
  };

  const updateOpen = (nextOpen: boolean, restoreFocus = false) => {
    if (disabled) return;
    setOpen(nextOpen);
    onOpenChange?.(nextOpen);
    if (!nextOpen) updateSearch("");
    if (!nextOpen && restoreFocus) window.requestAnimationFrame(() => triggerRef.current?.focus());
  };

  useEffect(() => {
    if (!open) return;
    const focusFrame = window.requestAnimationFrame(() => {
      if (searchable) searchRef.current?.focus();
      else {
        const selectedOption = listRef.current?.querySelector<HTMLElement>('[role="option"][aria-selected="true"]');
        (selectedOption ?? listRef.current?.querySelector<HTMLElement>('[role="option"]'))?.focus();
      }
    });

    const handlePointerDown = (event: MouseEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) updateOpen(false);
    };
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") updateOpen(false, true);
    };

    document.addEventListener("mousedown", handlePointerDown);
    document.addEventListener("keydown", handleKeyDown);
    return () => {
      window.cancelAnimationFrame(focusFrame);
      document.removeEventListener("mousedown", handlePointerDown);
      document.removeEventListener("keydown", handleKeyDown);
    };
    // Domain callbacks should not cause document listeners to churn while open.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, searchable]);

  const visibleOptions = useMemo(() => {
    const normalizedQuery = query.trim().toLocaleLowerCase("tr-TR");
    if (!filterOptions || !normalizedQuery) return options;
    return options.filter((option) =>
      `${option.label} ${option.value} ${option.searchText ?? ""}`
        .toLocaleLowerCase("tr-TR")
        .includes(normalizedQuery));
  }, [filterOptions, options, query]);

  const selectedOptions = selected
    .map((value) => options.find((option) => option.value === value))
    .filter((option): option is DropdownOption => Boolean(option));

  const choose = (option: DropdownOption) => {
    if (option.disabled) return;
    if (multiple) {
      onChange(selected.includes(option.value)
        ? selected.filter((value) => value !== option.value)
        : [...selected, option.value]);
      return;
    }
    onChange([option.value]);
    updateOpen(false, true);
  };

  const triggerContent = renderValue
    ? renderValue(selectedOptions)
    : multiple
      ? <span className="truncate">{label}{selected.length > 0 ? ` (${selected.length})` : ""}</span>
      : <span className="truncate">{selectedOptions[0]?.label ?? label}</span>;

  return (
    <div ref={rootRef} className={cn("relative", className)}>
      <button
        ref={triggerRef}
        id={id}
        type="button"
        disabled={disabled}
        onClick={() => updateOpen(!open)}
        onKeyDown={(event) => {
          if (event.key === "ArrowDown") {
            event.preventDefault();
            updateOpen(true);
          }
        }}
        aria-label={label}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={listboxId}
        aria-describedby={ariaDescribedBy}
        title={title}
        style={triggerStyle}
        className={cn(
          "flex w-full min-w-36 items-center justify-between gap-2 rounded-lg border border-zinc-300 bg-white px-2.5 text-xs font-medium text-zinc-700 outline-none transition-colors hover:bg-zinc-50 focus-visible:border-blue-500 focus-visible:ring-2 focus-visible:ring-blue-500/20 disabled:cursor-not-allowed disabled:opacity-50 dark:border-zinc-700 dark:bg-zinc-900 dark:text-zinc-200 dark:hover:bg-zinc-800",
          compact ? "h-8" : "h-9",
          multiple && selected.length > 0 && "border-blue-300 bg-blue-50 text-blue-700 dark:border-blue-700 dark:bg-blue-950 dark:text-blue-300",
          triggerClassName,
        )}
      >
        <span className="flex min-w-0 items-center gap-1.5">
          {leadingIcon}
          {triggerContent}
        </span>
        <ChevronDown size={14} aria-hidden="true" className={cn("shrink-0 opacity-70 transition-transform", open && "rotate-180")} />
      </button>

      {open && (
        <div className={cn(
          "absolute z-50 mt-1 w-72 max-w-[calc(100vw-2rem)] overflow-hidden rounded-xl border border-zinc-200 bg-white shadow-xl shadow-zinc-950/10 dark:border-zinc-700 dark:bg-zinc-900 dark:shadow-black/30",
          panelAlign === "end" ? "right-0" : "left-0",
          panelClassName,
        )}>
          {searchable && (
            <div className="relative border-b border-zinc-200 p-2 dark:border-zinc-700">
              <Search size={14} aria-hidden="true" className="absolute left-4 top-1/2 -translate-y-1/2 text-zinc-400" />
              <input
                ref={searchRef}
                type="search"
                value={query}
                onChange={(event) => updateSearch(event.target.value)}
                onKeyDown={(event) => {
                  if (event.key === "ArrowDown") {
                    event.preventDefault();
                    listRef.current?.querySelector<HTMLElement>('[role="option"]')?.focus();
                  } else if (event.key === "Enter" && onSearchSubmit) {
                    event.preventDefault();
                    onSearchSubmit();
                  }
                }}
                placeholder={searchPlaceholder ?? `${label} ara...`}
                className="w-full rounded-md border border-zinc-300 bg-white py-1.5 pl-8 pr-2 text-xs outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500 dark:border-zinc-700 dark:bg-zinc-800"
              />
            </div>
          )}

          <div
            ref={listRef}
            id={listboxId}
            role="listbox"
            aria-label={label}
            aria-multiselectable={multiple || undefined}
            className="subtle-scrollbar max-h-60 overflow-y-auto p-1"
            onScroll={onListScroll}
            onKeyDown={(event) => {
              if (event.key !== "ArrowDown" && event.key !== "ArrowUp" && event.key !== "Home" && event.key !== "End") return;
              const items = Array.from(event.currentTarget.querySelectorAll<HTMLElement>('[role="option"]:not(:disabled)'));
              if (items.length === 0) return;
              event.preventDefault();
              const currentIndex = items.indexOf(document.activeElement as HTMLElement);
              const nextIndex = event.key === "Home"
                ? 0
                : event.key === "End"
                  ? items.length - 1
                  : event.key === "ArrowDown"
                    ? Math.min(items.length - 1, currentIndex + 1)
                    : Math.max(0, currentIndex < 0 ? items.length - 1 : currentIndex - 1);
              items[nextIndex]?.focus();
            }}
          >
            {beforeOptions}
            {clearable && !multiple && (
              <button
                type="button"
                role="option"
                aria-selected={selected.length === 0}
                onClick={() => { onChange([]); updateOpen(false, true); }}
                className="flex w-full items-center justify-between rounded-md px-2.5 py-2 text-left text-xs text-zinc-500 hover:bg-zinc-100 dark:hover:bg-zinc-800"
              >
                {emptySelectionLabel}
                {selected.length === 0 && <Check size={14} aria-hidden="true" className="text-blue-600 dark:text-blue-400" />}
              </button>
            )}
            {visibleOptions.map((option) => {
              const isSelected = selected.includes(option.value);
              return (
                <button
                  key={option.value}
                  type="button"
                  role="option"
                  aria-selected={isSelected}
                  disabled={option.disabled}
                  onClick={() => choose(option)}
                  className="flex w-full items-center justify-between gap-3 rounded-md px-2.5 py-2 text-left text-xs text-zinc-700 transition-colors hover:bg-zinc-100 disabled:cursor-not-allowed disabled:opacity-45 dark:text-zinc-200 dark:hover:bg-zinc-800"
                >
                  <span className="min-w-0 flex-1 truncate">
                    {renderOption ? renderOption(option, isSelected) : option.label}
                  </span>
                  {isSelected && <Check size={14} aria-hidden="true" className="shrink-0 text-blue-600 dark:text-blue-400" />}
                </button>
              );
            })}
            {!loading && visibleOptions.length === 0 && !beforeOptions && (
              <p className="px-3 py-5 text-center text-xs text-zinc-500">{emptyText}</p>
            )}
            {loading && <p className="px-3 py-3 text-center text-xs text-zinc-500" role="status">{loadingText}</p>}
            {afterOptions}
          </div>
        </div>
      )}
    </div>
  );
}
