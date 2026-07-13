import { useState } from "react";
import { useSearchParams, useNavigate } from "react-router-dom";
import { useAuth } from "../contexts/AuthContext";
import { useApi } from "../hooks/useApi";

import { toast } from "sonner";
import { Lock, User, Key, LogOut } from "lucide-react";
import { cn } from "../lib/utils";
import { ApiKeysSection } from "../components/profile/api-keys-section";

type ProfileTab = "personal" | "password" | "api-keys";

const tabs: { id: ProfileTab; label: string; icon: React.ReactNode }[] = [
  { id: "personal", label: "Personal Info", icon: <User size={16} /> },
  { id: "password", label: "Password", icon: <Lock size={16} /> },
  { id: "api-keys", label: "API Keys", icon: <Key size={16} /> },
];

export default function ProfilePage() {
  const { user, refreshUser, logout } = useAuth();
  const { fetchWithAuth } = useApi();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();

  const tabParam = searchParams.get("tab");
  const activeTab: ProfileTab = tabParam === "password" || tabParam === "api-keys" ? tabParam : "personal";
  const setActiveTab = (tab: ProfileTab) => {
    setSearchParams(tab === "personal" ? {} : { tab }, { replace: true });
  };

  const isAzureUser = user?.isAzureUser ?? false;

  const [name, setName] = useState(user?.name ?? "");
  const [email, setEmail] = useState(user?.email ?? "");
  const [saving, setSaving] = useState(false);

  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [changingPassword, setChangingPassword] = useState(false);

  const handleProfileUpdate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim()) {
      toast.error("Name is required");
      return;
    }
    setSaving(true);
    try {
      const res = await fetchWithAuth("/api/auth/profile", {
        method: "PUT",
        body: JSON.stringify({ name: name.trim(), email: email.trim() }),
      });
      if (!res.ok) {
        const data = await res.json();
        toast.error(data.error || "Failed to update profile");
        return;
      }
      await refreshUser();
      toast.success("Profile updated successfully");
    } finally {
      setSaving(false);
    }
  };

  const handlePasswordChange = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!isAzureUser && !currentPassword) {
      toast.error("Current password is required");
      return;
    }
    if (newPassword.length < 8) {
      toast.error("New password must be at least 8 characters");
      return;
    }
    if (newPassword !== confirmPassword) {
      toast.error("Passwords do not match");
      return;
    }
    setChangingPassword(true);
    try {
      const body: Record<string, string> = { newPassword };
      if (!isAzureUser) {
        body.currentPassword = currentPassword;
      }
      const res = await fetchWithAuth("/api/auth/profile", {
        method: "PUT",
        body: JSON.stringify(body),
      });
      if (!res.ok) {
        const data = await res.json();
        toast.error(data.error || "Failed to change password");
        return;
      }
      await refreshUser();
      toast.success(isAzureUser ? "Password set successfully" : "Password changed successfully");
      setCurrentPassword("");
      setNewPassword("");
      setConfirmPassword("");
    } finally {
      setChangingPassword(false);
    }
  };

  return (
    <div className="max-w-2xl mx-auto py-8 px-4">
      <div className="flex items-center justify-between mb-6">
        <div className="flex items-center gap-3">
          <div className="w-10 h-10 rounded-full bg-blue-100 dark:bg-blue-900/30 flex items-center justify-center text-sm font-semibold text-blue-700 dark:text-blue-300">
            {user?.name ? user.name.split(' ').map(w => w[0]).join('').toUpperCase().slice(0, 2) : '?'}
          </div>
          <div>
            <h1 className="text-2xl font-bold text-zinc-900 dark:text-zinc-100">Profile Settings</h1>
            <p className="text-sm text-zinc-500">{user?.email}</p>
          </div>
        </div>
        <button
          onClick={() => { logout(); navigate("/login"); }}
          className="flex items-center gap-2 px-2 py-1 text-sm font-medium text-red-600 dark:text-red-400 border border-red-200 dark:border-red-900 rounded-lg hover:bg-red-50 dark:hover:bg-red-950 transition-colors"
        >
          <LogOut size={16} />
          Sign out
        </button>
      </div>

      {/* Tabs */}
      <div role="tablist" aria-label="Profile settings sections" className="flex items-center gap-1 border-b border-zinc-200 dark:border-zinc-800 mb-8">
        {tabs.map((tab) => (
          <button
            key={tab.id}
            role="tab"
            aria-selected={activeTab === tab.id}
            onClick={() => setActiveTab(tab.id)}
            className={cn(
              "flex items-center gap-2 px-4 py-2.5 text-sm font-medium border-b-2 -mb-px transition-colors",
              activeTab === tab.id
                ? "border-blue-600 text-blue-600 dark:text-blue-400"
                : "border-transparent text-zinc-500 hover:text-zinc-900 dark:hover:text-zinc-100 hover:border-zinc-300 dark:hover:border-zinc-700"
            )}
          >
            {tab.icon}
            {tab.label}
          </button>
        ))}
      </div>

      {/* Personal Info */}
      {activeTab === "personal" && (
        <form onSubmit={handleProfileUpdate}>
          <h2 className="text-lg font-semibold text-zinc-900 dark:text-zinc-100 mb-6">Personal Information</h2>

          {isAzureUser && (
            <div className="mb-4 p-3 bg-sky-50 dark:bg-sky-950 border border-sky-200 dark:border-sky-800 rounded-lg text-sm text-sky-700 dark:text-sky-300">
              Your name and email are managed by your Microsoft account and are re-synced on every sign-in, so they cannot be changed here.
            </div>
          )}

          <div className="space-y-4">
            <div>
              <label className="block text-sm font-medium text-zinc-700 dark:text-zinc-300 mb-1">Name</label>
              <input
                type="text"
                value={name}
                onChange={(e) => setName(e.target.value)}
                disabled={isAzureUser}
                className="w-full px-3 py-2 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800 text-zinc-900 dark:text-zinc-100 focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-60 disabled:cursor-not-allowed disabled:bg-zinc-100 dark:disabled:bg-zinc-900"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-zinc-700 dark:text-zinc-300 mb-1">Email</label>
              <input
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                disabled={isAzureUser}
                className="w-full px-3 py-2 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800 text-zinc-900 dark:text-zinc-100 focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-60 disabled:cursor-not-allowed disabled:bg-zinc-100 dark:disabled:bg-zinc-900"
              />
            </div>
          </div>

          {!isAzureUser && (
            <button
              type="submit"
              disabled={saving}
              className="mt-4 px-4 py-2 text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 rounded-lg disabled:opacity-50 disabled:cursor-not-allowed"
            >
              {saving ? "Saving..." : "Save Changes"}
            </button>
          )}
        </form>
      )}

      {/* Change Password */}
      {activeTab === "password" && (
        <form onSubmit={handlePasswordChange}>
          <h2 className="text-lg font-semibold text-zinc-900 dark:text-zinc-100 mb-6">
            {isAzureUser ? "Set Password" : "Change Password"}
          </h2>

          {isAzureUser && (
            <p className="text-sm text-zinc-500 dark:text-zinc-400 mb-4">
              You signed in with Microsoft. You can set a password to also sign in with email and password.
            </p>
          )}

          <div className="space-y-4">
            {!isAzureUser && (
              <div>
                <label className="block text-sm font-medium text-zinc-700 dark:text-zinc-300 mb-1">Current Password</label>
                <input
                  type="password"
                  value={currentPassword}
                  onChange={(e) => setCurrentPassword(e.target.value)}
                  className="w-full px-3 py-2 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800 text-zinc-900 dark:text-zinc-100 focus:outline-none focus:ring-2 focus:ring-blue-500"
                />
              </div>
            )}
            <div>
              <label className="block text-sm font-medium text-zinc-700 dark:text-zinc-300 mb-1">New Password</label>
              <input
                type="password"
                value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)}
                className="w-full px-3 py-2 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800 text-zinc-900 dark:text-zinc-100 focus:outline-none focus:ring-2 focus:ring-blue-500"
                placeholder="Minimum 8 characters"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-zinc-700 dark:text-zinc-300 mb-1">Confirm New Password</label>
              <input
                type="password"
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                className="w-full px-3 py-2 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800 text-zinc-900 dark:text-zinc-100 focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
            </div>
          </div>

          <button
            type="submit"
            disabled={changingPassword}
            className="mt-4 px-4 py-2 text-sm font-medium text-white bg-amber-600 hover:bg-amber-700 rounded-lg disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {changingPassword ? "Saving..." : isAzureUser ? "Set Password" : "Change Password"}
          </button>
        </form>
      )}

      {/* API Keys */}
      {activeTab === "api-keys" && <ApiKeysSection />}
    </div>
  );
}
