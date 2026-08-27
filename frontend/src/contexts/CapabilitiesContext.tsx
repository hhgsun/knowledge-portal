import { createContext, useContext, useEffect, useMemo, useState } from "react";
import { assistantEnabled as compiledAssistantEnabled } from "../config/features";
import { useAuth } from "./AuthContext";
import { useApi } from "../hooks/useApi";
import type { AssistantCapabilities } from "../types/api";

interface CapabilitiesState {
  assistantEnabled: boolean;
  assistantLoading: boolean;
  capabilities: AssistantCapabilities | null;
}

const CapabilitiesContext = createContext<CapabilitiesState>({
  assistantEnabled: false,
  assistantLoading: false,
  capabilities: null,
});

export function CapabilitiesProvider({ children }: { children: React.ReactNode }) {
  const { user } = useAuth();
  const { fetchWithAuth } = useApi();
  const [capabilities, setCapabilities] = useState<AssistantCapabilities | null>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!compiledAssistantEnabled || !user) {
      setCapabilities(null);
      setLoading(false);
      return;
    }
    const controller = new AbortController();
    setLoading(true);
    void fetchWithAuth("/api/capabilities", { signal: controller.signal, noRetry: true })
      .then(async response => {
        if (!response.ok) throw new Error("capabilities unavailable");
        setCapabilities(await response.json() as AssistantCapabilities);
      })
      .catch(error => {
        if (error instanceof DOMException && error.name === "AbortError") return;
        // A transient capability-probe failure must not remove a compiled feature.
        setCapabilities(null);
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [fetchWithAuth, user]);

  const value = useMemo<CapabilitiesState>(() => ({
    assistantEnabled: compiledAssistantEnabled && (capabilities?.enabled ?? true),
    assistantLoading: loading,
    capabilities,
  }), [capabilities, loading]);

  return <CapabilitiesContext.Provider value={value}>{children}</CapabilitiesContext.Provider>;
}

export function useCapabilities() {
  return useContext(CapabilitiesContext);
}
