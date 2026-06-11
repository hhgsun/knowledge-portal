import { useEffect, useState } from "react";
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

  const contentTypes = lookups.filter((l) => l.category === "content_type");
  const difficulties = lookups.filter((l) => l.category === "difficulty");

  const invalidateCache = () => {
    cache = null;
  };

  return { lookups, contentTypes, difficulties, loading, invalidateCache };
}
