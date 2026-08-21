import { AlertCircle, CheckCircle2, Clock3, LoaderCircle, RefreshCw } from "lucide-react";
import type { ArticleIndexingStatus } from "../types/api";

const CONFIG = {
  indexed: {
    label: "İndekslendi",
    className: "bg-emerald-50 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300",
    icon: CheckCircle2,
  },
  indexing: {
    label: "İndeksleniyor",
    className: "bg-blue-50 text-blue-700 dark:bg-blue-950 dark:text-blue-300",
    icon: LoaderCircle,
  },
  pending: {
    label: "İndeks bekliyor",
    className: "bg-amber-50 text-amber-700 dark:bg-amber-950 dark:text-amber-300",
    icon: Clock3,
  },
  stale: {
    label: "İndeks güncel değil",
    className: "bg-orange-50 text-orange-700 dark:bg-orange-950 dark:text-orange-300",
    icon: RefreshCw,
  },
  failed: {
    label: "İndeksleme başarısız",
    className: "bg-red-50 text-red-700 dark:bg-red-950 dark:text-red-300",
    icon: AlertCircle,
  },
} as const;

function tooltip(status: ArticleIndexingStatus) {
  if (status.state === "indexed" && status.indexedAt) {
    return `Arama indeksi güncel. Son indeksleme: ${new Date(status.indexedAt).toLocaleString("tr-TR")}`;
  }
  if (status.state === "failed") return "İndeksleme tamamlanamadı. Ayrıntılar için arama tanılama ekranını kontrol edin.";
  if (status.state === "stale") return "Makalenin son değişiklikleri henüz arama indeksine yansımadı.";
  if (status.state === "indexing") return "Makalenin arama indeksi şu anda oluşturuluyor.";
  return "Makale arama indeksleme kuyruğunda bekliyor.";
}

export function ArticleIndexStatusBadge({ status }: { status?: ArticleIndexingStatus | null }) {
  if (!status || status.state === "not_applicable") return null;

  const config = CONFIG[status.state];
  const Icon = config.icon;
  const description = tooltip(status);
  return (
    <span
      title={description}
      aria-label={description}
      className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium ${config.className}`}
    >
      <Icon size={12} className={status.state === "indexing" ? "animate-spin" : undefined} />
      {config.label}
    </span>
  );
}
