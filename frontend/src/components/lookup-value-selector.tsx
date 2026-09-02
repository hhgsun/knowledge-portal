import { useEffect, useId, useRef, useState } from "react";
import { Check, ChevronDown, Search, X } from "lucide-react";
import type { LookupValue } from "../types/api";
import { getColorClasses, getIconComponent } from "../lib/lookup-utils";

interface LookupValueSelectorProps {
  label: string;
  options: LookupValue[];
  selected: string[];
  onChange: (values: string[]) => void;
  multiple?: boolean;
  required?: boolean;
  showSelectionInTrigger?: boolean;
  showSelectedChips?: boolean;
  className?: string;
}

export function LookupValueSelector({
  label,
  options,
  selected,
  onChange,
  multiple = true,
  required = false,
  showSelectionInTrigger = true,
  showSelectedChips = false,
  className = "",
}: LookupValueSelectorProps) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const rootRef = useRef<HTMLDivElement>(null);
  const searchRef = useRef<HTMLInputElement>(null);
  const listboxId = useId();
  const showSearch = options.length > 10;
  const selectedOptions = selected
    .map(value => options.find(option => option.value === value))
    .filter((option): option is LookupValue => Boolean(option));
  const singleSelection = !multiple ? selectedOptions[0] : undefined;
  const normalizedQuery = query.trim().toLocaleLowerCase("tr-TR");
  const visibleOptions = normalizedQuery
    ? options.filter(option => option.label.toLocaleLowerCase("tr-TR").includes(normalizedQuery)
      || option.value.toLocaleLowerCase("tr-TR").includes(normalizedQuery))
    : options;

  useEffect(() => {
    if (open && showSearch) searchRef.current?.focus();
    const handlePointerDown = (event: MouseEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) {
        setOpen(false);
        setQuery("");
      }
    };
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setOpen(false);
        setQuery("");
      }
    };
    document.addEventListener("mousedown", handlePointerDown);
    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("mousedown", handlePointerDown);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [open, showSearch]);

  const toggle = (value: string) => {
    if (!multiple) {
      onChange([value]);
      setOpen(false);
      setQuery("");
      return;
    }
    onChange(selected.includes(value) ? selected.filter(item => item !== value) : [...selected, value]);
  };

  const triggerColors = singleSelection ? getColorClasses(singleSelection.color) : undefined;
  const TriggerIcon = singleSelection ? getIconComponent(singleSelection.icon) : null;
  const triggerLabel = showSelectionInTrigger && singleSelection
    ? singleSelection.label
    : `${label}${selected.length > 0 ? ` (${selected.length})` : ""}`;

  return (
    <div ref={rootRef} className={`relative ${className}`}>
      <button
        type="button"
        onClick={() => { setOpen(current => !current); setQuery(""); }}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={listboxId}
        style={triggerColors ? { ...triggerColors.bgStyle, ...triggerColors.textStyle } : undefined}
        className={`flex h-9 w-full min-w-36 items-center justify-between gap-2 rounded-lg border px-2.5 text-xs font-medium transition-colors ${triggerColors
          ? `${triggerColors.bg} ${triggerColors.text} border-black/5 dark:border-white/10`
          : selected.length > 0
            ? "border-blue-300 bg-blue-50 text-blue-700 dark:border-blue-700 dark:bg-blue-950 dark:text-blue-300"
            : "border-zinc-300 bg-white text-zinc-700 hover:bg-zinc-50 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-200 dark:hover:bg-zinc-700"}`}
      >
        <span className="flex min-w-0 items-center gap-1.5">
          {TriggerIcon && <TriggerIcon size={14} className="shrink-0" aria-hidden="true" />}
          <span className="truncate">{triggerLabel}</span>
        </span>
        <ChevronDown size={14} className={`shrink-0 transition-transform ${open ? "rotate-180" : ""}`} />
      </button>

      {showSelectedChips && multiple && selectedOptions.length > 0 && (
        <div className="mt-1.5 flex flex-wrap gap-1">
          {selectedOptions.map(option => {
            const colors = getColorClasses(option.color);
            const Icon = getIconComponent(option.icon);
            return (
              <span key={option.id} style={{ ...colors.bgStyle, ...colors.textStyle }} className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11px] font-medium ${colors.bg} ${colors.text}`}>
                <Icon size={11} /> {option.label}
                <button type="button" onClick={() => toggle(option.value)} aria-label={`${option.label} seçimini kaldır`} className="rounded-full hover:text-rose-600"><X size={11} /></button>
              </span>
            );
          })}
        </div>
      )}

      {open && (
        <div className="absolute left-0 z-50 mt-1 w-72 max-w-[calc(100vw-2rem)] overflow-hidden rounded-xl border border-zinc-200 bg-white shadow-xl shadow-zinc-950/10 dark:border-zinc-700 dark:bg-zinc-900">
          {showSearch && (
            <div className="relative border-b border-zinc-200 p-2 dark:border-zinc-700">
              <Search size={14} className="absolute left-4 top-1/2 -translate-y-1/2 text-zinc-400" />
              <input ref={searchRef} type="search" value={query} onChange={event => setQuery(event.target.value)} placeholder={`${label} ara...`} className="w-full rounded-md border border-zinc-300 bg-white py-1.5 pl-8 pr-2 text-xs outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500 dark:border-zinc-700 dark:bg-zinc-800" />
            </div>
          )}
          <div id={listboxId} role="listbox" aria-label={label} aria-multiselectable={multiple || undefined} className="subtle-scrollbar max-h-60 overflow-y-auto p-1">
            {!required && !multiple && (
              <button type="button" role="option" aria-selected={selected.length === 0} onClick={() => { onChange([]); setOpen(false); }} className="flex w-full items-center justify-between rounded-md px-2.5 py-2 text-left text-xs text-zinc-500 hover:bg-zinc-100 dark:hover:bg-zinc-800">
                Seçilmedi {selected.length === 0 && <Check size={14} className="text-blue-600" />}
              </button>
            )}
            {visibleOptions.map(option => {
              const isSelected = selected.includes(option.value);
              const colors = getColorClasses(option.color);
              const Icon = getIconComponent(option.icon);
              return (
                <button key={option.id} type="button" role="option" aria-selected={isSelected} onClick={() => toggle(option.value)} className="flex w-full items-center justify-between gap-3 rounded-md px-2.5 py-2 text-left hover:bg-zinc-100 dark:hover:bg-zinc-800">
                  <span style={colors.textStyle} className={`flex min-w-0 items-center gap-2 text-xs font-medium ${colors.text}`}>
                    <span style={{ ...colors.bgStyle, ...colors.textStyle }} className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-md ${colors.bg} ${colors.text}`}><Icon size={14} /></span>
                    <span className="truncate">{option.label}</span>
                  </span>
                  {isSelected && <Check size={14} className="shrink-0 text-blue-600 dark:text-blue-400" />}
                </button>
              );
            })}
            {visibleOptions.length === 0 && <p className="px-3 py-5 text-center text-xs text-zinc-500">Eşleşen değer bulunamadı.</p>}
          </div>
        </div>
      )}
    </div>
  );
}
