import { useState, useMemo, useRef, useEffect } from "react";
import { Search, Ban } from "lucide-react";
import { COLOR_MAP, COLOR_KEYS, ALL_ICONS, getColorClasses, getIconComponent } from "../lib/lookup-utils";

// ─── Color Picker ─────────────────────────────────────────────
// With allowNone, an empty value means "no color" (inherit the default styling).
export function ColorPicker({ value, onChange, allowNone = false }: { value: string; onChange: (color: string) => void; allowNone?: boolean }) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, []);

  const isNone = allowNone && !value;
  const current = getColorClasses(value);

  return (
    <div className="relative" ref={ref}>
      <button
        type="button"
        onClick={() => setOpen(!open)}
        className={`w-8 h-8 rounded-lg border border-zinc-200 dark:border-zinc-700 ${
          isNone ? "bg-white dark:bg-zinc-800 flex items-center justify-center text-zinc-400" : current.dot
        } ring-2 ring-offset-2 ring-offset-white dark:ring-offset-zinc-900 ring-zinc-300 dark:ring-zinc-600`}
        title={value || (allowNone ? "No color (default)" : "Select color")}
      >
        {isNone && <Ban size={14} />}
      </button>
      {open && (
        <div className="absolute z-50 top-10 left-0 p-2 bg-white dark:bg-zinc-800 border border-zinc-200 dark:border-zinc-700 rounded-xl shadow-lg">
          <div className="grid grid-cols-5 gap-1.5 w-fit">
            {allowNone && (
              <button
                type="button"
                onClick={() => { onChange(""); setOpen(false); }}
                className={`w-7 h-7 rounded-full border border-zinc-300 dark:border-zinc-600 flex items-center justify-center text-zinc-400 bg-white dark:bg-zinc-800 ${
                  isNone ? "ring-2 ring-offset-1 ring-zinc-500 dark:ring-offset-zinc-800" : "hover:ring-1 hover:ring-zinc-300"
                }`}
                title="No color (default)"
              >
                <Ban size={13} />
              </button>
            )}
            {COLOR_KEYS.map((key) => (
              <button
                key={key}
                type="button"
                onClick={() => { onChange(key); setOpen(false); }}
                className={`w-7 h-7 rounded-full ${COLOR_MAP[key].dot} ${value === key ? "ring-2 ring-offset-1 ring-zinc-500 dark:ring-offset-zinc-800" : "hover:ring-1 hover:ring-zinc-300"}`}
                title={key}
              />
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

// ─── Icon Picker ──────────────────────────────────────────────
export function IconPicker({ value, onChange }: { value: string; onChange: (icon: string) => void }) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const ref = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, []);

  useEffect(() => {
    if (open && inputRef.current) inputRef.current.focus();
  }, [open]);

  const filtered = useMemo(() => {
    if (!search.trim()) return ALL_ICONS.slice(0, 60);
    const q = search.toLowerCase();
    return ALL_ICONS.filter((i) => i.key.includes(q) || i.name.toLowerCase().includes(q)).slice(0, 60);
  }, [search]);

  const CurrentIcon = getIconComponent(value);

  return (
    <div className="relative" ref={ref}>
      <button
        type="button"
        onClick={() => { setOpen(!open); setSearch(""); }}
        className="w-8 h-8 rounded-lg border border-zinc-200 dark:border-zinc-700 flex items-center justify-center bg-white dark:bg-zinc-800 hover:border-zinc-400 text-zinc-600 dark:text-zinc-300"
        title={value || "Select icon"}
      >
        <CurrentIcon size={16} />
      </button>
      {open && (
        <div className="absolute z-50 top-10 left-0 w-72 bg-white dark:bg-zinc-800 border border-zinc-200 dark:border-zinc-700 rounded-xl shadow-lg overflow-hidden">
          <div className="p-2 border-b border-zinc-100 dark:border-zinc-700">
            <div className="flex items-center gap-2 px-2 py-1.5 bg-zinc-50 dark:bg-zinc-900 rounded-lg">
              <Search size={14} className="text-zinc-400" />
              <input
                ref={inputRef}
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Search icons..."
                className="text-sm bg-transparent border-none outline-none flex-1 text-zinc-700 dark:text-zinc-200 placeholder:text-zinc-400"
              />
            </div>
          </div>
          <div className="p-2 max-h-48 overflow-y-auto">
            <div className="grid grid-cols-8 gap-1">
              {filtered.map((item) => {
                const Icon = item.component;
                return (
                  <button
                    key={item.key}
                    type="button"
                    onClick={() => { onChange(item.key); setOpen(false); }}
                    className={`w-7 h-7 rounded-lg flex items-center justify-center ${value === item.key ? "bg-blue-100 dark:bg-blue-900/40 text-blue-600 ring-1 ring-blue-400" : "text-zinc-500 hover:bg-zinc-100 dark:hover:bg-zinc-700 hover:text-zinc-700 dark:hover:text-zinc-200"}`}
                    title={item.key}
                  >
                    <Icon size={14} />
                  </button>
                );
              })}
            </div>
            {filtered.length === 0 && (
              <p className="text-xs text-zinc-400 text-center py-3">No icons found</p>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
