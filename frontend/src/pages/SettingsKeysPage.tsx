import { useState, useEffect } from "react";
import { Key, Plus, Trash2, Copy, Check, AlertTriangle } from "lucide-react";
import { useApi } from "../hooks/useApi";
import { toast } from "sonner";
import type { ApiKey } from "../types/api";

export default function SettingsKeysPage() {
  const { fetchWithAuth } = useApi();
  const [keys, setKeys] = useState<ApiKey[]>([]);
  const [loading, setLoading] = useState(true);
  const [showCreate, setShowCreate] = useState(false);
  const [newKeyName, setNewKeyName] = useState("");
  const [expiresInDays, setExpiresInDays] = useState(90);
  const [createdKey, setCreatedKey] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);
  const [error, setError] = useState("");

  const loadKeys = async () => {
    const res = await fetchWithAuth("/api/keys");
    if (res.ok) {
      const data = await res.json();
      setKeys(data);
    }
    setLoading(false);
  };

  useEffect(() => { loadKeys(); }, []);

  const handleCreate = async () => {
    if (!newKeyName.trim()) return;
    setError("");

    const res = await fetchWithAuth("/api/keys", {
      method: "POST",
      body: JSON.stringify({ name: newKeyName.trim(), expiresInDays }),
    });

    if (res.ok) {
      const data = await res.json();
      setCreatedKey(data.key);
      setNewKeyName("");
      setShowCreate(false);
      loadKeys();
    } else {
      const data = await res.json();
      setError(data.error || "Failed to create key");
    }
  };

  const handleDelete = async (id: string) => {
    const res = await fetchWithAuth(`/api/keys?id=${id}`, { method: "DELETE" });
    if (res.ok) {
      setKeys(keys.filter((k) => k.id !== id));
      toast.success("API key deleted");
    } else {
      toast.error("Failed to delete API key");
    }
  };

  const handleCopy = () => {
    if (createdKey) {
      navigator.clipboard.writeText(createdKey);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    }
  };

  if (loading) return <div className="text-center py-12 text-zinc-500">Loading...</div>;

  return (
    <div className="max-w-3xl mx-auto">
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-xl font-bold text-zinc-900 dark:text-zinc-100">API Keys</h1>
          <p className="text-sm text-zinc-500 mt-1">Manage API keys for external integrations</p>
        </div>
        <button onClick={() => setShowCreate(true)} className="flex items-center gap-2 px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium rounded-lg">
          <Plus size={16} />
          New Key
        </button>
      </div>

      {createdKey && (
        <div className="mb-6 p-4 bg-amber-50 dark:bg-amber-950 border border-amber-200 dark:border-amber-800 rounded-xl">
          <div className="flex items-start gap-2">
            <AlertTriangle size={16} className="text-amber-600 mt-0.5 shrink-0" />
            <div className="flex-1">
              <p className="text-sm font-medium text-amber-800 dark:text-amber-200">
                Copy your API key now — it won&apos;t be shown again
              </p>
              <div className="flex items-center gap-2 mt-2">
                <code className="text-xs bg-amber-100 dark:bg-amber-900 px-2 py-1 rounded font-mono break-all">{createdKey}</code>
                <button onClick={handleCopy} className="shrink-0 text-amber-700 hover:text-amber-900">
                  {copied ? <Check size={14} /> : <Copy size={14} />}
                </button>
              </div>
            </div>
            <button onClick={() => setCreatedKey(null)} className="text-amber-500 hover:text-amber-700 text-sm">Dismiss</button>
          </div>
        </div>
      )}

      {showCreate && (
        <div className="mb-6 p-4 border border-zinc-200 dark:border-zinc-800 rounded-xl">
          <h3 className="text-sm font-medium text-zinc-700 dark:text-zinc-300 mb-3">Create API Key</h3>
          <div className="flex items-end gap-3 flex-wrap">
            <div className="flex-1 min-w-[200px]">
              <label className="text-xs text-zinc-500 mb-1 block">Name</label>
              <input type="text" value={newKeyName} onChange={(e) => setNewKeyName(e.target.value)} placeholder="e.g., CI/CD Pipeline" className="w-full px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800" />
            </div>
            <div className="w-32">
              <label className="text-xs text-zinc-500 mb-1 block">Expires in (days)</label>
              <input type="number" value={expiresInDays} onChange={(e) => setExpiresInDays(parseInt(e.target.value) || 90)} className="w-full px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800" />
            </div>
            <button onClick={handleCreate} className="px-4 py-1.5 bg-blue-600 hover:bg-blue-700 text-white text-sm rounded-lg">Create</button>
            <button onClick={() => setShowCreate(false)} className="px-4 py-1.5 text-sm text-zinc-500 hover:text-zinc-700">Cancel</button>
          </div>
          {error && <p className="text-sm text-red-500 mt-2">{error}</p>}
        </div>
      )}

      {keys.length === 0 ? (
        <div className="text-center py-8 border border-dashed border-zinc-300 dark:border-zinc-700 rounded-xl">
          <Key size={24} className="mx-auto text-zinc-300 mb-2" />
          <p className="text-zinc-500">No API keys yet</p>
          <p className="text-xs text-zinc-400 mt-1">Create a key to authenticate external API requests</p>
        </div>
      ) : (
        <div className="space-y-3">
          {keys.map((key) => (
            <div key={key.id} className="flex items-center justify-between p-4 border border-zinc-200 dark:border-zinc-800 rounded-xl">
              <div>
                <p className="text-sm font-medium text-zinc-900 dark:text-zinc-100">{key.name}</p>
                <div className="flex items-center gap-3 mt-1 text-xs text-zinc-400">
                  <span>Created {new Date(key.createdAt).toLocaleDateString()}</span>
                  {key.lastUsedAt && <span>Last used {new Date(key.lastUsedAt).toLocaleDateString()}</span>}
                  {key.expiresAt && (
                    <span className={new Date(key.expiresAt) < new Date() ? "text-red-500" : ""}>
                      {new Date(key.expiresAt) < new Date() ? "Expired" : `Expires ${new Date(key.expiresAt).toLocaleDateString()}`}
                    </span>
                  )}
                </div>
              </div>
              <button onClick={() => handleDelete(key.id)} className="p-2 text-zinc-400 hover:text-red-500 rounded-lg hover:bg-red-50 dark:hover:bg-red-950">
                <Trash2 size={16} />
              </button>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
