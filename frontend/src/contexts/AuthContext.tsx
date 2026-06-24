import { createContext, useContext, useState, useEffect, useCallback, type ReactNode } from "react";
import { useMsal } from "@azure/msal-react";
import { loginRequest } from "../config/msalConfig";

interface User {
  id: string;
  name: string;
  email: string;
  role: string;
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
  }, []);

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
        return res.json();
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
    const res = await fetch("/api/auth/me", {
      headers: { Authorization: `Bearer ${token}` },
    });
    if (res.ok) {
      const data = await res.json();
      setUser(data);
    }
  }, [token]);

  const login = async (email: string, password: string): Promise<{ error?: string }> => {
    const res = await fetch("/api/auth/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, password }),
    });

    if (!res.ok) {
      const data = await res.json();
      return { error: data.error || "Invalid credentials" };
    }

    const data = await res.json();
    setToken(data.token);
    setUser(data.user);
    localStorage.setItem("token", data.token);
    return {};
  };

  const register = async (name: string, email: string, password: string): Promise<{ error?: string }> => {
    const res = await fetch("/api/auth/register", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name, email, password }),
    });

    if (!res.ok) {
      const data = await res.json();
      return { error: data.error || "Registration failed" };
    }

    return {};
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
        const data = await res.json();
        return { error: data.error || "Azure login failed" };
      }

      const data = await res.json();
      setToken(data.token);
      setUser(data.user);
      localStorage.setItem("token", data.token);
      return {};
    } catch (err: unknown) {
      // If another interaction is in progress, silently ignore
      if (err instanceof Error && err.message?.includes("interaction_in_progress")) {
        return { error: "Login is already in progress, please wait" };
      }
      const message = err instanceof Error ? err.message : "Azure authentication failed";
      return { error: message };
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
