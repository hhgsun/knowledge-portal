import { icons, type LucideIcon } from "lucide-react";

// ─── Dynamic Color Definitions ────────────────────────────────
// All Tailwind color keys supported. Add new colors here and they work everywhere.
export const COLOR_MAP: Record<string, { bg: string; text: string; dot: string }> = {
  slate: { bg: "bg-slate-100 dark:bg-slate-900/30", text: "text-slate-700 dark:text-slate-300", dot: "bg-slate-500" },
  gray: { bg: "bg-gray-100 dark:bg-gray-900/30", text: "text-gray-700 dark:text-gray-300", dot: "bg-gray-500" },
  zinc: { bg: "bg-zinc-100 dark:bg-zinc-900/30", text: "text-zinc-700 dark:text-zinc-300", dot: "bg-zinc-500" },
  red: { bg: "bg-red-100 dark:bg-red-900/30", text: "text-red-700 dark:text-red-300", dot: "bg-red-500" },
  orange: { bg: "bg-orange-100 dark:bg-orange-900/30", text: "text-orange-700 dark:text-orange-300", dot: "bg-orange-500" },
  amber: { bg: "bg-amber-100 dark:bg-amber-900/30", text: "text-amber-700 dark:text-amber-300", dot: "bg-amber-500" },
  yellow: { bg: "bg-yellow-100 dark:bg-yellow-900/30", text: "text-yellow-700 dark:text-yellow-300", dot: "bg-yellow-500" },
  lime: { bg: "bg-lime-100 dark:bg-lime-900/30", text: "text-lime-700 dark:text-lime-300", dot: "bg-lime-500" },
  green: { bg: "bg-green-100 dark:bg-green-900/30", text: "text-green-700 dark:text-green-300", dot: "bg-green-500" },
  emerald: { bg: "bg-emerald-100 dark:bg-emerald-900/30", text: "text-emerald-700 dark:text-emerald-300", dot: "bg-emerald-500" },
  teal: { bg: "bg-teal-100 dark:bg-teal-900/30", text: "text-teal-700 dark:text-teal-300", dot: "bg-teal-500" },
  cyan: { bg: "bg-cyan-100 dark:bg-cyan-900/30", text: "text-cyan-700 dark:text-cyan-300", dot: "bg-cyan-500" },
  sky: { bg: "bg-sky-100 dark:bg-sky-900/30", text: "text-sky-700 dark:text-sky-300", dot: "bg-sky-500" },
  blue: { bg: "bg-blue-100 dark:bg-blue-900/30", text: "text-blue-700 dark:text-blue-300", dot: "bg-blue-500" },
  indigo: { bg: "bg-indigo-100 dark:bg-indigo-900/30", text: "text-indigo-700 dark:text-indigo-300", dot: "bg-indigo-500" },
  violet: { bg: "bg-violet-100 dark:bg-violet-900/30", text: "text-violet-700 dark:text-violet-300", dot: "bg-violet-500" },
  purple: { bg: "bg-purple-100 dark:bg-purple-900/30", text: "text-purple-700 dark:text-purple-300", dot: "bg-purple-500" },
  fuchsia: { bg: "bg-fuchsia-100 dark:bg-fuchsia-900/30", text: "text-fuchsia-700 dark:text-fuchsia-300", dot: "bg-fuchsia-500" },
  pink: { bg: "bg-pink-100 dark:bg-pink-900/30", text: "text-pink-700 dark:text-pink-300", dot: "bg-pink-500" },
  rose: { bg: "bg-rose-100 dark:bg-rose-900/30", text: "text-rose-700 dark:text-rose-300", dot: "bg-rose-500" },
};

export const COLOR_KEYS = Object.keys(COLOR_MAP);

// ─── Dynamic Icon Access ──────────────────────────────────────
// Convert PascalCase icon name to kebab-case key for storage
function toKebab(name: string): string {
  return name.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}

// Build a map of kebab-case key → { component, displayName }
const iconEntries = Object.entries(icons)
  .filter(([name]) => name !== "createLucideIcon" && name !== "default")
  .map(([name, component]) => ({
    key: toKebab(name),
    name,
    component: component as LucideIcon,
  }));

// Sorted once for consistent ordering
iconEntries.sort((a, b) => a.key.localeCompare(b.key));

export const ALL_ICONS = iconEntries;

// ─── Utility Functions ────────────────────────────────────────
export function getColorClasses(color?: string) {
  if (!color) return COLOR_MAP.blue;
  return COLOR_MAP[color] || COLOR_MAP.blue;
}

export function getIconComponent(iconKey?: string): LucideIcon {
  if (!iconKey) return icons.FileText;
  const entry = ALL_ICONS.find((i) => i.key === iconKey);
  return entry?.component || icons.FileText;
}
