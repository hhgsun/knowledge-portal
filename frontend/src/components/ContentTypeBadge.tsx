import { useNavigate } from "react-router-dom";
import { useLookups } from "../hooks/useLookups";
import { getColorClasses, getIconComponent } from "../lib/lookup-utils";

interface ContentTypeBadgeProps {
  contentType?: string;
  size?: "sm" | "md";
  clickable?: boolean;
}

export function ContentTypeBadge({ contentType, size = "sm", clickable = false }: ContentTypeBadgeProps) {
  const { contentTypes } = useLookups();
  const navigate = useNavigate();
  const lookup = contentTypes.find((ct) => ct.value === contentType);

  // preventDefault/stopPropagation: badge may sit inside a card that is itself a Link
  const handleClick = clickable && contentType
    ? (e: React.MouseEvent) => {
        e.preventDefault();
        e.stopPropagation();
        navigate(`/articles?contentType=${encodeURIComponent(contentType)}`);
      }
    : undefined;
  const clickClasses = handleClick ? " cursor-pointer hover:opacity-75 transition-opacity" : "";

  if (!lookup) {
    return <span className="text-xs text-zinc-400">{contentType}</span>;
  }

  const colorClasses = getColorClasses(lookup.color);
  const Icon = getIconComponent(lookup.icon);

  if (size === "md") {
    return (
      <span
        onClick={handleClick}
        className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-sm font-medium ${colorClasses.bg} ${colorClasses.text}${clickClasses}`}
      >
        <Icon size={14} />
        {lookup.label}
      </span>
    );
  }

  return (
    <span
      onClick={handleClick}
      className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-xs font-medium ${colorClasses.bg} ${colorClasses.text}${clickClasses}`}
    >
      <Icon size={12} />
      {lookup.label}
    </span>
  );
}
