import { useEffect, useState } from "react";
import { useApi } from "./useApi";
import type { FeaturedLink } from "../types/api";

let cache: FeaturedLink[] | null = null;
const listeners = new Set<() => void>();

/** Clears the cache and notifies all mounted consumers (e.g. the sidebar) to refetch. */
export function invalidateFeaturedLinksCache() {
  cache = null;
  listeners.forEach((notify) => notify());
}

/** Active featured links for the sidebar, cached across mounts. */
export function useFeaturedLinks() {
  const { fetchWithAuth } = useApi();
  const [links, setLinks] = useState<FeaturedLink[]>(cache || []);
  const [version, setVersion] = useState(0);

  useEffect(() => {
    const notify = () => setVersion((v) => v + 1);
    listeners.add(notify);
    return () => {
      listeners.delete(notify);
    };
  }, []);

  useEffect(() => {
    if (cache) {
      setLinks(cache);
      return;
    }
    let cancelled = false;
    fetchWithAuth("/api/featured-links")
      .then((res) => res.json())
      .then((data) => {
        if (!cancelled && Array.isArray(data)) {
          cache = data;
          setLinks(data);
        }
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, [fetchWithAuth, version]);

  return { links, invalidateCache: invalidateFeaturedLinksCache };
}

/** Resolves a featured link to a router path or external URL. */
export function resolveFeaturedLinkHref(link: FeaturedLink): { href: string; external: boolean } {
  switch (link.linkType) {
    case "content_type":
      return { href: `/articles?contentType=${encodeURIComponent(link.target)}`, external: false };
    case "tag":
      return { href: `/articles?tag=${encodeURIComponent(link.target)}`, external: false };
    default:
      return { href: link.target, external: /^https?:\/\//i.test(link.target) };
  }
}
