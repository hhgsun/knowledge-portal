import { useLookups } from "../hooks/useLookups";
import { getColorClasses, getIconComponent } from "../lib/lookup-utils";

interface ContentTypeBadgeProps {
  contentType?: string;
  size?: "sm" | "md";
}

export function ContentTypeBadge({ contentType, size = "sm" }: ContentTypeBadgeProps) {
  const { contentTypes } = useLookups();
  const lookup = contentTypes.find((ct) => ct.value === contentType);

  if (!lookup) {
    return <span className="text-xs text-zinc-400">{contentType}</span>;
  }

  const colorClasses = getColorClasses(lookup.color);
  const Icon = getIconComponent(lookup.icon);

  if (size === "md") {
    return (
      <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-sm font-medium ${colorClasses.bg} ${colorClasses.text}`}>
        <Icon size={14} />
        {lookup.label}
      </span>
    );
  }

  return (
    <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-xs font-medium ${colorClasses.bg} ${colorClasses.text}`}>
      <Icon size={12} />
      {lookup.label}
    </span>
  );
}
