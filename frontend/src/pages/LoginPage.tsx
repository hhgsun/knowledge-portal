import { useState, useEffect } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { BookSearch } from "lucide-react";
import { useAuth } from "../contexts/AuthContext";
import { useMsal } from "@azure/msal-react";
import { loginRequest } from "../config/msalConfig";

export default function LoginPage() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const callbackUrl = searchParams.get("callbackUrl") || "/";
  const { login, loginWithAzure } = useAuth();
  const { instance: msalInstance } = useMsal();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const [autoLoginAttempted, setAutoLoginAttempted] = useState(false);

  // Try silent Azure login if user has an active Azure session
  useEffect(() => {
    if (autoLoginAttempted) return;
    setAutoLoginAttempted(true);

    const accounts = msalInstance.getAllAccounts();
    if (accounts.length === 0) return;

    setLoading(true);
    msalInstance
      .acquireTokenSilent({ ...loginRequest, account: accounts[0] })
      .then(async (response) => {
        if (response?.accessToken) {
          const res = await fetch("/api/auth/azure-login", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ accessToken: response.accessToken }),
          });
          if (res.ok) {
            const data = await res.json();
            localStorage.setItem("token", data.token);
            window.location.href = callbackUrl;
            return;
          }
        }
        setLoading(false);
      })
      .catch(() => {
        setLoading(false);
      });
  }, [autoLoginAttempted, msalInstance, callbackUrl]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError("");
    setLoading(true);

    const result = await login(email, password);

    if (result.error) {
      setError(result.error);
      setLoading(false);
    } else {
      navigate(callbackUrl);
    }
  };

  const handleAzureLogin = async () => {
    setError("");
    setLoading(true);

    const result = await loginWithAzure();

    if (result.error) {
      setError(result.error);
      setLoading(false);
    } else {
      navigate(callbackUrl);
    }
  };

  return (
    <div className="w-full min-h-screen flex items-center justify-center bg-zinc-50 dark:bg-zinc-950 px-4">
      <div className="w-full max-w-sm">
        <div className="text-center mb-8">
          <BookSearch size={40} className="mx-auto text-blue-600 mb-3" />
          <h1 className="text-2xl font-bold text-zinc-900 dark:text-zinc-100">
            Knowledge Portal
          </h1>
          <p className="text-sm text-zinc-500 mt-1">
            Sign in to access your knowledge base
          </p>
        </div>

        <div className="bg-white dark:bg-zinc-900 rounded-xl border border-zinc-200 dark:border-zinc-800 p-6 space-y-4">
          {error && (
            <div className="p-3 bg-red-50 dark:bg-red-950 border border-red-200 dark:border-red-800 rounded-lg text-sm text-red-600 dark:text-red-400">
              {error}
            </div>
          )}

          {/* Azure AD Login */}
          <button
            type="button"
            onClick={handleAzureLogin}
            disabled={loading}
            className="w-full flex items-center justify-center gap-2 py-2.5 bg-[#0078d4] hover:bg-[#106ebe] disabled:opacity-50 text-white text-sm font-medium rounded-lg transition-colors"
          >
            <svg width="18" height="18" viewBox="0 0 21 21" xmlns="http://www.w3.org/2000/svg" fill="currentColor">
              <rect x="1" y="1" width="9" height="9" />
              <rect x="11" y="1" width="9" height="9" />
              <rect x="1" y="11" width="9" height="9" />
              <rect x="11" y="11" width="9" height="9" />
            </svg>
            {loading ? "Signing in..." : "Sign in with Microsoft"}
          </button>

          <div className="relative">
            <div className="absolute inset-0 flex items-center">
              <div className="w-full border-t border-zinc-200 dark:border-zinc-700" />
            </div>
            <div className="relative flex justify-center text-xs">
              <span className="bg-white dark:bg-zinc-900 px-2 text-zinc-500">or</span>
            </div>
          </div>

          {/* Email/Password Login */}
          <form onSubmit={handleSubmit} className="space-y-4">
            <div>
              <label htmlFor="email" className="block text-sm font-medium text-zinc-700 dark:text-zinc-300 mb-1">
                Email
              </label>
              <input
                id="email"
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
                className="w-full px-3 py-2 border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800 text-zinc-900 dark:text-zinc-100 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                placeholder="admin@knowledge.local"
              />
            </div>

            <div>
              <label htmlFor="password" className="block text-sm font-medium text-zinc-700 dark:text-zinc-300 mb-1">
                Password
              </label>
              <input
                id="password"
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
                className="w-full px-3 py-2 border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800 text-zinc-900 dark:text-zinc-100 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                placeholder="••••••••"
              />
            </div>

            <button
              type="submit"
              disabled={loading}
              className="w-full py-2.5 bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white text-sm font-medium rounded-lg transition-colors"
            >
              {loading ? "Signing in..." : "Sign in"}
            </button>
          </form>
        </div>

        {/* <p className="text-center text-xs text-zinc-500 mt-4">
          Don&apos;t have an account?{" "}
          <Link to="/register" className="text-blue-600 hover:underline">
            Register
          </Link>
        </p> */}
      </div>
    </div>
  );
}
