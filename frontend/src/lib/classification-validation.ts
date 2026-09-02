import type { LookupCategory, LookupValue } from "../types/api";

export function missingRequiredClassifications(
  categories: LookupCategory[],
  lookups: LookupValue[],
  classifications: Record<string, string[]>,
  contentType = "",
) {
  return categories.filter(category => {
    if (!category.isActive || !category.isRequired) return false;

    const supplied = classifications[category.key];
    if (supplied?.some(value => value.trim())) return false;
    if (category.key === "content_type" && contentType.trim()) return false;

    // Create requests use an active configured default when the category was
    // omitted. An explicitly cleared category must still fail validation.
    if (supplied === undefined && category.defaultValueId) {
      const activeDefault = lookups.some(value =>
        value.id === category.defaultValueId
        && value.category === category.key
        && value.isActive);
      if (activeDefault) return false;
    }
    return true;
  });
}

export function requiredClassificationMessage(labels: string[]) {
  return labels.length === 1
    ? `${labels[0]} alanı zorunludur.`
    : `Şu alanlar zorunludur: ${labels.join(", ")}.`;
}
