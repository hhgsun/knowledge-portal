import { useCallback } from "react";
import { useAuth } from "../contexts/AuthContext";
import { toast } from "sonner";
import { fetchWithRetry } from "./useNetworkStatus";
import { networkErrorMessage, normalizeApiErrorResponse } from "../lib/api-response";

export interface FetchOptions extends RequestInit {
  /** Disable automatic retry for this request */
  noRetry?: boolean;
}

export function useApi() {
  const { token, logout } = useAuth();

  const fetchWithAuth = useCallback(
    async (url: string, options: FetchOptions = {}): Promise<Response> => {
      const { noRetry, ...fetchOpts } = options;
      const headers = new Headers(fetchOpts.headers);
      if (token) {
        headers.set("Authorization", `Bearer ${token}`);
      }
      if (!headers.has("Content-Type") && fetchOpts.body && typeof fetchOpts.body === "string") {
        headers.set("Content-Type", "application/json");
      }

      const doFetch = () => fetch(url, { ...fetchOpts, headers });

      let res: Response;
      try {
        // Only retry GET requests and non-mutating calls by default
        const method = (fetchOpts.method || "GET").toUpperCase();
        const shouldRetry = !noRetry && (method === "GET" || method === "HEAD");
        res = shouldRetry ? await fetchWithRetry(doFetch) : await doFetch();
      } catch (err) {
        if (err instanceof DOMException && err.name === "AbortError") throw err;
        toast.error(networkErrorMessage(), { id: "api-network-error" });
        throw err;
      }

      const normalized = await normalizeApiErrorResponse(res);
      res = normalized.response;

      if (normalized.usedFallback && res.status !== 401) {
        toast.error(normalized.message, { id: "api-response-error" });
      }

      if (res.status === 401) {
        toast.error("Session expired. Please log in again.");
        logout();
      }

      return res;
    },
    [token, logout]
  );

  return { fetchWithAuth };
}
