import { createElement, useEffect, useId, useRef, useState } from "react";
import { Check, ChevronDown } from "lucide-react";
import type { LookupValue } from "../../types/api";
import { getColorClasses, getIconComponent } from "../../lib/lookup-utils";

interface ContentTypeSelectProps {
  options: LookupValue[];
  value: string;
  onChange: (value: string) => void;
}

export function ContentTypeSelect({ options, value, onChange }: ContentTypeSelectProps) {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);
  const listboxId = useId();
  const selected = options.find((option) => option.value === value);
  const selectedColors = getColorClasses(selected?.color);

  useEffect(() => {
    if (!open) return;

    const handlePointerDown = (event: MouseEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false);
    };
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") setOpen(false);
    };

    document.addEventListener("mousedown", handlePointerDown);
    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("mousedown", handlePointerDown);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [open]);

  return (
    <div ref={rootRef} className="relative">
      <button
        type="button"
        onClick={() => setOpen((current) => !current)}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={listboxId}
        className={`inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1 text-sm font-medium ring-1 ring-black/5 transition-all hover:-translate-y-px hover:shadow focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500/50 dark:ring-white/10 ${selectedColors.bg} ${selectedColors.text}`}
      >
        {createElement(getIconComponent(selected?.icon), { size: 14, "aria-hidden": true })}
        <span>{selected?.label ?? value}</span>
        <ChevronDown
          size={13}
          aria-hidden="true"
          className={`ml-0.5 opacity-60 transition-transform ${open ? "rotate-180" : ""}`}
        />
      </button>

      {open && (
        <div
          id={listboxId}
          role="listbox"
          aria-label="İçerik türü"
          className="absolute left-0 z-30 mt-1.5 w-60 max-w-[calc(100vw-2rem)] overflow-hidden rounded-xl border border-zinc-200 bg-white p-1 shadow-xl shadow-zinc-950/10 dark:border-zinc-700 dark:bg-zinc-900 dark:shadow-black/30"
        >
          <div className="px-2 pb-1 pt-0.5 text-[10px] font-semibold uppercase tracking-wider text-zinc-400">
            İçerik türü
          </div>
          <div className="space-y-px">
            {options.map((option) => {
              const colors = getColorClasses(option.color);
              const isSelected = option.value === value;

              return (
                <button
                  key={option.id}
                  type="button"
                  role="option"
                  aria-selected={isSelected}
                  onClick={() => {
                    onChange(option.value);
                    setOpen(false);
                  }}
                  className={`flex w-full items-center gap-2 rounded-lg px-2 py-1.5 text-left transition-colors ${isSelected
                    ? "bg-zinc-100 dark:bg-zinc-800"
                    : "hover:bg-zinc-50 dark:hover:bg-zinc-800/60"
                    }`}
                >
                  <span className={`flex size-7 shrink-0 items-center justify-center rounded-md ${colors.bg} ${colors.text}`}>
                    {createElement(getIconComponent(option.icon), { size: 14, "aria-hidden": true })}
                  </span>
                  <span className={`min-w-0 flex-1 truncate text-xs font-medium ${colors.text}`}>
                    {option.label}
                  </span>
                  {isSelected && <Check size={14} className="shrink-0 text-zinc-500" aria-hidden="true" />}
                </button>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}
