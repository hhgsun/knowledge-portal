import { useState, useMemo, useRef, useEffect, useId } from "react";
import { Search, Ban, ExternalLink, Check } from "lucide-react";
import { COLOR_MAP, COLOR_KEYS, ALL_ICONS, getColorClasses, getIconComponent, hasIcon, normalizeIconName } from "../lib/lookup-utils";

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
  const [customName, setCustomName] = useState(value);
  const ref = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const customInputId = useId();

  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, []);

  useEffect(() => {
    if (open && inputRef.current) {
      setCustomName(value);
      inputRef.current.focus();
    }
  }, [open, value]);

  const matches = useMemo(() => {
    if (!search.trim()) return ALL_ICONS;
    const q = normalizeIconName(search);
    return ALL_ICONS.filter((i) => i.key.includes(q) || i.name.toLowerCase().includes(q));
  }, [search]);
  // Rendering the complete Lucide catalog at once creates thousands of SVG nodes.
  // Search still covers the entire set; only the visible result batch is capped.
  const visibleMatches = matches.slice(0, 96);
  const normalizedCustomName = normalizeIconName(customName);
  const isCustomNameValid = hasIcon(customName);

  const CurrentIcon = getIconComponent(value);
  const CustomIcon = isCustomNameValid ? getIconComponent(customName) : null;
  const selectedIconName = normalizeIconName(value) || "İkon seç";

  return (
    <div className="relative" ref={ref}>
      <button
        type="button"
        onClick={() => { setOpen(!open); setSearch(""); }}
        className="flex h-8 min-w-32 max-w-48 items-center gap-2 rounded-lg border border-zinc-200 bg-white px-2 text-zinc-600 hover:border-zinc-400 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-300"
        title={value || "Select icon"}
      >
        <CurrentIcon size={16} className="shrink-0" />
        <span className="truncate text-xs font-medium">{selectedIconName}</span>
      </button>
      {open && (
        <div className="absolute z-50 top-10 left-0 w-80 bg-white dark:bg-zinc-800 border border-zinc-200 dark:border-zinc-700 rounded-xl shadow-lg overflow-hidden">
          <div className="p-2 border-b border-zinc-100 dark:border-zinc-700">
            <div className="flex items-center gap-2 px-2 py-1.5 bg-zinc-50 dark:bg-zinc-900 rounded-lg">
              <Search size={14} className="text-zinc-400" />
              <input
                ref={inputRef}
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="İkonlarda ara..."
                className="text-sm bg-transparent border-none outline-none flex-1 text-zinc-700 dark:text-zinc-200 placeholder:text-zinc-400"
              />
            </div>
          </div>
          <div className="p-2 max-h-48 overflow-y-auto">
            <div className="grid grid-cols-8 gap-1">
              {visibleMatches.map((item) => {
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
            {matches.length === 0 && (
              <p className="text-xs text-zinc-400 text-center py-3">Eşleşen ikon bulunamadı</p>
            )}
          </div>
          <div className="space-y-2 border-t border-zinc-100 p-3 dark:border-zinc-700">
            <div className="flex items-center justify-between gap-2 text-[11px] text-zinc-500">
              <span>{Math.min(visibleMatches.length, matches.length)} / {matches.length} sonuç gösteriliyor</span>
              <a
                href="https://lucide.dev/icons/"
                target="_blank"
                rel="noreferrer"
                className="inline-flex items-center gap-1 font-medium text-blue-600 hover:underline dark:text-blue-400"
              >
                Tüm Lucide ikonları <ExternalLink size={11} />
              </a>
            </div>
            <div>
              <label htmlFor={customInputId} className="mb-1 block text-xs font-medium text-zinc-600 dark:text-zinc-300">
                Özel ikon adı
              </label>
              <div className={`flex items-center gap-1 rounded-lg border px-2 ${customName && !isCustomNameValid ? "border-red-300 dark:border-red-800" : "border-zinc-200 dark:border-zinc-700"}`}>
                <span
                  className={`flex h-6 w-6 shrink-0 items-center justify-center rounded ${CustomIcon ? "bg-zinc-100 text-zinc-600 dark:bg-zinc-700 dark:text-zinc-200" : "border border-dashed border-zinc-300 text-zinc-400 dark:border-zinc-600"}`}
                  title={CustomIcon ? `${normalizedCustomName} önizlemesi` : "Geçerli bir ikon adı girin"}
                  aria-hidden="true"
                >
                  {CustomIcon ? <CustomIcon size={15} /> : <span className="text-[10px]">?</span>}
                </span>
                <input
                  id={customInputId}
                  value={customName}
                  onChange={(event) => setCustomName(event.target.value)}
                  onKeyDown={(event) => {
                    if (event.key === "Enter" && isCustomNameValid) {
                      event.preventDefault();
                      onChange(normalizedCustomName);
                      setOpen(false);
                    }
                  }}
                  placeholder="Örn. alarm-clock"
                  aria-invalid={Boolean(customName && !isCustomNameValid)}
                  className="min-w-0 flex-1 bg-transparent py-1.5 text-sm text-zinc-700 outline-none placeholder:text-zinc-400 dark:text-zinc-200"
                />
                <button
                  type="button"
                  disabled={!isCustomNameValid}
                  onClick={() => { onChange(normalizedCustomName); setOpen(false); }}
                  className="inline-flex h-7 items-center gap-1 rounded-md bg-blue-600 px-2 text-xs font-medium text-white hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-40"
                >
                  <Check size={12} /> Kullan
                </button>
              </div>
              {customName && !isCustomNameValid && (
                <p className="mt-1 text-[11px] text-red-600 dark:text-red-400">Lucide setinde bu adla bir ikon bulunamadı.</p>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
