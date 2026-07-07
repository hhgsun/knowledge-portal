import { useState, useEffect, useCallback } from "react";
import { toast } from "sonner";

export function useNetworkStatus() {
  const [isOnline, setIsOnline] = useState(navigator.onLine);

  useEffect(() => {
    const handleOnline = () => {
      setIsOnline(true);
      toast.success("Connection restored");
    };
    const handleOffline = () => {
      setIsOnline(false);
      toast.error("You are offline. Some features may not work.");
    };

    window.addEventListener("online", handleOnline);
    window.addEventListener("offline", handleOffline);
    return () => {
      window.removeEventListener("online", handleOnline);
      window.removeEventListener("offline", handleOffline);
    };
  }, []);

  return { isOnline };
}

/** Retry a fetch call with exponential backoff */
export async function fetchWithRetry(
  fn: () => Promise<Response>,
  options: { maxRetries?: number; baseDelay?: number; retryOn?: (res: Response) => boolean } = {}
): Promise<Response> {
  const { maxRetries = 3, baseDelay = 1000, retryOn } = options;

  for (let attempt = 0; attempt <= maxRetries; attempt++) {
    try {
      const res = await fn();
      // Don't retry client errors (4xx) unless explicitly told to
      if (retryOn ? retryOn(res) : (res.status >= 500 || res.status === 0)) {
        if (attempt < maxRetries) {
          await delay(baseDelay * Math.pow(2, attempt));
          continue;
        }
      }
      return res;
    } catch (err) {
      // Network error — retry if attempts remain
      if (attempt < maxRetries) {
        await delay(baseDelay * Math.pow(2, attempt));
        continue;
      }
      throw err;
    }
  }

  // Should never reach here, but satisfy TS
  return fn();
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/** Hook for debouncing a value */
export function useDebounce<T>(value: T, delayMs: number): T {
  const [debouncedValue, setDebouncedValue] = useState(value);

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedValue(value), delayMs);
    return () => clearTimeout(timer);
  }, [value, delayMs]);

  return debouncedValue;
}

const SEARCH_HISTORY_KEY = "kp_search_history";
const MAX_HISTORY = 10;

export function useSearchHistory() {
  const [history, setHistory] = useState<string[]>(() => {
    try {
      const stored = localStorage.getItem(SEARCH_HISTORY_KEY);
      return stored ? JSON.parse(stored) : [];
    } catch {
      return [];
    }
  });

  const addToHistory = useCallback((query: string) => {
    const trimmed = query.trim();
    if (!trimmed) return;
    setHistory((prev) => {
      const filtered = prev.filter((item) => item !== trimmed);
      const updated = [trimmed, ...filtered].slice(0, MAX_HISTORY);
      localStorage.setItem(SEARCH_HISTORY_KEY, JSON.stringify(updated));
      return updated;
    });
  }, []);

  const removeFromHistory = useCallback((query: string) => {
    setHistory((prev) => {
      const updated = prev.filter((item) => item !== query);
      localStorage.setItem(SEARCH_HISTORY_KEY, JSON.stringify(updated));
      return updated;
    });
  }, []);

  const clearHistory = useCallback(() => {
    setHistory([]);
    localStorage.removeItem(SEARCH_HISTORY_KEY);
  }, []);

  return { history, addToHistory, removeFromHistory, clearHistory };
}
