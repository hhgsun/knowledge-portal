import { Archive, CircleCheck, FilePenLine, type LucideIcon } from "lucide-react";
import { DropdownSelector, type DropdownOption } from "./ui/dropdown-selector";

interface StatusDefinition extends DropdownOption {
  icon: LucideIcon;
  color: string;
  background: string;
}

export const ARTICLE_STATUS_OPTIONS: StatusDefinition[] = [
  { value: "draft", label: "Taslak", icon: FilePenLine, color: "text-zinc-600 dark:text-zinc-300", background: "bg-zinc-100 dark:bg-zinc-800" },
  { value: "published", label: "Yayımlandı", icon: CircleCheck, color: "text-emerald-700 dark:text-emerald-300", background: "bg-emerald-50 dark:bg-emerald-950/50" },
  { value: "archived", label: "Arşivlendi", icon: Archive, color: "text-rose-700 dark:text-rose-300", background: "bg-rose-50 dark:bg-rose-950/50" },
];

interface ArticleStatusSelectorProps {
  value?: string;
  values?: string[];
  onChange: (values: string[]) => void;
  multiple?: boolean;
  includeArchived?: boolean;
  ariaDescribedBy?: string;
  compact?: boolean;
}

export function ArticleStatusSelector({
  value,
  values,
  onChange,
  multiple = false,
  includeArchived = true,
  ariaDescribedBy,
  compact = true,
}: ArticleStatusSelectorProps) {
  const options = ARTICLE_STATUS_OPTIONS.filter((option) => includeArchived || option.value !== "archived");
  const selected = values ?? (value ? [value] : []);
  const selectedStatus = !multiple ? options.find((option) => option.value === selected[0]) : undefined;

  return (
    <DropdownSelector
      label="Yayın durumu"
      options={options}
      selected={selected}
      onChange={onChange}
      multiple={multiple}
      compact={compact}
      ariaDescribedBy={ariaDescribedBy}
      triggerClassName={selectedStatus ? `${selectedStatus.background} ${selectedStatus.color} border-black/5 dark:border-white/10` : undefined}
      renderValue={() => {
        if (multiple) return <span className="truncate">Durum{selected.length ? ` (${selected.length})` : ""}</span>;
        const status = selectedStatus ?? options[0];
        const Icon = status.icon;
        return <><Icon size={13} aria-hidden="true" className="shrink-0" /><span className="truncate">{status.label}</span></>;
      }}
      renderOption={(option) => {
        const status = options.find((item) => item.value === option.value)!;
        const Icon = status.icon;
        return (
          <span className={`flex items-center gap-2 font-medium ${status.color}`}>
            <span className={`flex h-7 w-7 items-center justify-center rounded-md ${status.background}`}><Icon size={14} aria-hidden="true" /></span>
            {status.label}
          </span>
        );
      }}
    />
  );
}
