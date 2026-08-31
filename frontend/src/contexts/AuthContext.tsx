import { createContext, useContext, useState, useEffect, useCallback, type ReactNode } from "react";
import { useMsal } from "@azure/msal-react";
import { loginRequest } from "../config/msalConfig";
import { apiErrorMessage, readApiError, readApiJson } from "../lib/api-response";

interface User {
  id: string;
  name: string;
  email: string;
  role: string;
  isAzureUser?: boolean;
  preferredLlmModel?: string | null;
}

interface AuthContextType {
  user: User | null;
  token: string | null;
  loading: boolean;
  login: (email: string, password: string) => Promise<{ error?: string }>;
  loginWithAzure: () => Promise<{ error?: string }>;
  logout: () => void;
  register: (name: string, email: string, password: string) => Promise<{ error?: string }>;
  refreshUser: () => Promise<void>;
}

const AuthContext = createContext<AuthContextType | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [token, setToken] = useState<string | null>(() => localStorage.getItem("token"));
  const [loading, setLoading] = useState(true);
  const { instance: msalInstance } = useMsal();

  const logout = useCallback(() => {
    setUser(null);
    setToken(null);
    localStorage.removeItem("token");
    // Clear MSAL account cache to prevent auto-silent login after logout
    msalInstance.clearCache();
  }, [msalInstance]);

  // Load user on mount / token change
  useEffect(() => {
    if (!token) {
      setLoading(false);
      return;
    }

    fetch("/api/auth/me", {
      headers: { Authorization: `Bearer ${token}` },
    })
      .then((res) => {
        if (!res.ok) {
          // Only logout on auth errors (401/403), not network issues
          if (res.status === 401 || res.status === 403) {
            logout();
          }
          throw new Error("Unauthorized");
        }
        return readApiJson<User>(res);
      })
      .then((data) => {
        setUser(data);
        setLoading(false);
      })
      .catch(() => {
        setLoading(false);
      });
  }, [token, logout]);

  const refreshUser = useCallback(async () => {
    if (!token) return;
    try {
      const res = await fetch("/api/auth/me", {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (res.ok) {
        setUser(await readApiJson<User>(res));
      }
    } catch {
      // The initiating screen already reports request failures through useApi.
    }
  }, [token]);

  const login = async (email: string, password: string): Promise<{ error?: string }> => {
    try {
      const res = await fetch("/api/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email, password }),
      });

      if (!res.ok) {
        return { error: await readApiError(res, "Invalid credentials") };
      }

      const data = await readApiJson<{ token: string; user: User }>(res);
      setToken(data.token);
      setUser(data.user);
      localStorage.setItem("token", data.token);
      return {};
    } catch (error) {
      return { error: apiErrorMessage(error) };
    }
  };

  const register = async (name: string, email: string, password: string): Promise<{ error?: string }> => {
    try {
      const res = await fetch("/api/auth/register", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ name, email, password }),
      });

      if (!res.ok) {
        return { error: await readApiError(res, "Registration failed") };
      }

      return {};
    } catch (error) {
      return { error: apiErrorMessage(error) };
    }
  };

  const loginWithAzure = async (): Promise<{ error?: string }> => {
    try {
      // Try silent login first (user already has active Azure session)
      let azureResponse;
      const accounts = msalInstance.getAllAccounts();
      if (accounts.length > 0) {
        try {
          azureResponse = await msalInstance.acquireTokenSilent({
            ...loginRequest,
            account: accounts[0],
          });
        } catch {
          // Silent failed, do popup
          azureResponse = await msalInstance.acquireTokenPopup(loginRequest);
        }
      } else {
        azureResponse = await msalInstance.acquireTokenPopup(loginRequest);
      }

      if (!azureResponse?.accessToken) {
        return { error: "Azure authentication failed" };
      }

      // Send Azure token to our backend
      const res = await fetch("/api/auth/azure-login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ accessToken: azureResponse.accessToken }),
      });

      if (!res.ok) {
        return { error: await readApiError(res, "Azure login failed") };
      }

      const data = await readApiJson<{ token: string; user: User }>(res);
      setToken(data.token);
      setUser(data.user);
      localStorage.setItem("token", data.token);
      return {};
    } catch (err: unknown) {
      // If another interaction is in progress, silently ignore
      if (err instanceof Error && err.message?.includes("interaction_in_progress")) {
        return { error: "Login is already in progress, please wait" };
      }
      return { error: apiErrorMessage(err, "Azure authentication failed") };
    }
  };

  return (
    <AuthContext.Provider value={{ user, token, loading, login, loginWithAzure, logout, register, refreshUser }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within AuthProvider");
  return ctx;
}
