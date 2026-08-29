import { useEffect, useMemo, useState } from "react";
import { useApi } from "./useApi";
import type { LookupCategory, LookupValue } from "../types/api";

let cache: LookupValue[] | null = null;
let categoryCache: LookupCategory[] | null = null;

export function useLookups() {
  const { fetchWithAuth } = useApi();
  const [lookups, setLookups] = useState<LookupValue[]>(cache || []);
  const [categories, setCategories] = useState<LookupCategory[]>(categoryCache || []);
  const [loading, setLoading] = useState(!cache || !categoryCache);

  useEffect(() => {
    if (cache && categoryCache) return;
    Promise.all([fetchWithAuth("/api/lookups"), fetchWithAuth("/api/lookups/categories")])
      .then(async ([valuesResponse, categoriesResponse]) => [
        await valuesResponse.json(), await categoriesResponse.json(),
      ] as const)
      .then(([values, categoryDefinitions]) => {
        if (Array.isArray(values)) { cache = values; setLookups(values); }
        if (Array.isArray(categoryDefinitions)) {
          categoryCache = categoryDefinitions;
          setCategories(categoryDefinitions);
        }
        setLoading(false);
      })
      .catch(() => setLoading(false));
  }, [fetchWithAuth]);

  // Memoized so consumers can safely use contentTypes as an effect dependency
  const contentTypes = useMemo(
    () => categories.some((category) => category.key === "content_type" && category.isActive)
      ? lookups.filter((lookup) => lookup.category === "content_type" && lookup.isActive)
      : [],
    [categories, lookups],
  );

  const invalidateCache = () => {
    cache = null;
    categoryCache = null;
  };

  return { lookups, categories, contentTypes, loading, invalidateCache };
}
