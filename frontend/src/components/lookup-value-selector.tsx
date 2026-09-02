import { X } from "lucide-react";
import type { LookupValue } from "../types/api";
import { getColorClasses, getIconComponent } from "../lib/lookup-utils";
import { DropdownSelector, type DropdownOption } from "./ui/dropdown-selector";

interface LookupValueSelectorProps {
  label: string;
  options: LookupValue[];
  selected: string[];
  onChange: (values: string[]) => void;
  multiple?: boolean;
  required?: boolean;
  showSelectionInTrigger?: boolean;
  showSelectedChips?: boolean;
  compact?: boolean;
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
  compact = false,
  className,
}: LookupValueSelectorProps) {
  const selectorOptions: DropdownOption[] = options.map((option) => ({
    value: option.value,
    label: option.label,
  }));
  const selectedOptions = selected
    .map((value) => options.find((option) => option.value === value))
    .filter((option): option is LookupValue => Boolean(option));
  const singleSelection = !multiple ? selectedOptions[0] : undefined;
  const triggerColors = singleSelection ? getColorClasses(singleSelection.color) : undefined;

  return (
    <div className={className}>
      <DropdownSelector
        label={label}
        options={selectorOptions}
        selected={selected}
        onChange={onChange}
        multiple={multiple}
        compact={compact}
        clearable={!required}
        searchable={options.length > 10}
        searchPlaceholder={`${label} ara...`}
        emptyText="Eşleşen değer bulunamadı."
        triggerStyle={triggerColors ? { ...triggerColors.bgStyle, ...triggerColors.textStyle } : undefined}
        triggerClassName={triggerColors
          ? `${triggerColors.bg} ${triggerColors.text} border-black/5 dark:border-white/10`
          : undefined}
        renderValue={() => {
          if (showSelectionInTrigger && singleSelection) {
            const Icon = getIconComponent(singleSelection.icon);
            return <><Icon size={14} className="shrink-0" aria-hidden="true" /><span className="truncate">{singleSelection.label}</span></>;
          }
          return <span className="truncate">{label}{selected.length > 0 ? ` (${selected.length})` : ""}</span>;
        }}
        renderOption={(selectorOption) => {
          const option = options.find((item) => item.value === selectorOption.value)!;
          const colors = getColorClasses(option.color);
          const Icon = getIconComponent(option.icon);
          return (
            <span style={colors.textStyle} className={`flex min-w-0 items-center gap-2 font-medium ${colors.text}`}>
              <span style={{ ...colors.bgStyle, ...colors.textStyle }} className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-md ${colors.bg} ${colors.text}`}>
                <Icon size={14} aria-hidden="true" />
              </span>
              <span className="truncate">{option.label}</span>
            </span>
          );
        }}
      />

      {showSelectedChips && multiple && selectedOptions.length > 0 && (
        <div className="mt-1.5 flex flex-wrap gap-1">
          {selectedOptions.map((option) => {
            const colors = getColorClasses(option.color);
            const Icon = getIconComponent(option.icon);
            return (
              <span key={option.id} style={{ ...colors.bgStyle, ...colors.textStyle }} className={`inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11px] font-medium ${colors.bg} ${colors.text}`}>
                <Icon size={11} aria-hidden="true" /> {option.label}
                <button type="button" onClick={() => onChange(selected.filter((value) => value !== option.value))} aria-label={`${option.label} seçimini kaldır`} className="rounded-full hover:text-rose-600">
                  <X size={11} />
                </button>
              </span>
            );
          })}
        </div>
      )}
    </div>
  );
}
