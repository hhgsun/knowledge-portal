import { useEffect, useMemo, useState } from "react";
import { useApi } from "./useApi";
import type { LookupValue } from "../types/api";

let cache: LookupValue[] | null = null;

export function useLookups() {
  const { fetchWithAuth } = useApi();
  const [lookups, setLookups] = useState<LookupValue[]>(cache || []);
  const [loading, setLoading] = useState(!cache);

  useEffect(() => {
    if (cache) return;
    fetchWithAuth("/api/lookups")
      .then((res) => res.json())
      .then((data) => {
        if (Array.isArray(data)) {
          cache = data;
          setLookups(data);
        }
        setLoading(false);
      })
      .catch(() => setLoading(false));
  }, [fetchWithAuth]);

  // Memoized so consumers can safely use contentTypes as an effect dependency
  const contentTypes = useMemo(
    () => lookups.filter((lookup) => lookup.category === "content_type" && lookup.isActive),
    [lookups],
  );

  const invalidateCache = () => {
    cache = null;
  };

  return { lookups, contentTypes, loading, invalidateCache };
}
