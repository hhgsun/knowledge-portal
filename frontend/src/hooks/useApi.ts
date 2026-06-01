import { useCallback } from "react";
import { useAuth } from "../contexts/AuthContext";
import { toast } from "sonner";

export function useApi() {
  const { token, logout } = useAuth();

  const fetchWithAuth = useCallback(
    async (url: string, options: RequestInit = {}): Promise<Response> => {
      const headers = new Headers(options.headers);
      if (token) {
        headers.set("Authorization", `Bearer ${token}`);
      }
      if (!headers.has("Content-Type") && options.body && typeof options.body === "string") {
        headers.set("Content-Type", "application/json");
      }

      const res = await fetch(url, { ...options, headers });

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
