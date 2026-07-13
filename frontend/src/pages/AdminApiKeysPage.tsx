import { useState, useEffect } from "react";
import { Key, Search, Pencil, Trash2, ChevronLeft, ChevronRight, Plus, X, Copy, Check, AlertTriangle } from "lucide-react";
import { useApi } from "../hooks/useApi";
import { KeysListSkeleton } from "../components/ui/skeleton";
import type { AdminApiKey, AdminUser } from "../types/api";

interface Pagination {
  page: number;
  limit: number;
  total: number;
  pages: number;
}

export default function AdminApiKeysPage() {
  const { fetchWithAuth } = useApi();
  const [keys, setKeys] = useState<AdminApiKey[]>([]);
  const [pagination, setPagination] = useState<Pagination>({ page: 1, limit: 50, total: 0, pages: 0 });
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState("");
  const [showAddForm, setShowAddForm] = useState(false);
  const [editingKey, setEditingKey] = useState<AdminApiKey | null>(null);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  const [users, setUsers] = useState<AdminUser[]>([]);
  const [addUserId, setAddUserId] = useState("");
  const [addName, setAddName] = useState("");
  const [addExpiresInDays, setAddExpiresInDays] = useState(90);

  const [editName, setEditName] = useState("");
  const [editExpiresInDays, setEditExpiresInDays] = useState("");

  const [createdKey, setCreatedKey] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);

  const loadKeys = async (page = 1, q = "") => {
    setLoading(true);
    const params = new URLSearchParams({ page: String(page), limit: "50" });
    if (q) params.set("q", q);

    const res = await fetchWithAuth(`/api/admin/keys?${params}`);
    if (res.ok) {
      const data = await res.json();
      setKeys(data.keys);
      setPagination({ page, limit: 50, total: data.total, pages: Math.ceil(data.total / 50) });
    } else if (res.status === 403) {
      setError("You don't have permission to manage API keys");
    }
    setLoading(false);
  };

  const loadUsers = async () => {
    const res = await fetchWithAuth("/api/admin/users?limit=100");
    if (res.ok) {
      const data = await res.json();
      setUsers(data.users);
    }
  };

  useEffect(() => { loadKeys(); loadUsers(); }, []);

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault();
    loadKeys(1, search);
  };

  const handleAddKey = async () => {
    if (!addUserId || !addName.trim()) {
      setError("User and key name are required");
      return;
    }
    setError("");

    const res = await fetchWithAuth("/api/admin/keys", {
      method: "POST",
      body: JSON.stringify({ userId: addUserId, name: addName.trim(), expiresInDays: addExpiresInDays }),
    });

    if (res.ok) {
      const created = await res.json();
      setCreatedKey(created.key);
      setShowAddForm(false);
      setAddUserId(""); setAddName(""); setAddExpiresInDays(90);
      setSuccess(`Created key "${created.name}" for ${created.userName}`);
      setTimeout(() => setSuccess(""), 3000);
      loadKeys(pagination.page, search);
    } else {
      const data = await res.json();
      setError(data.error || "Failed to create API key");
    }
  };

  const handleEditStart = (key: AdminApiKey) => {
    setEditingKey(key);
    setEditName(key.name);
    setEditExpiresInDays("");
    setError(""); setSuccess("");
    setShowAddForm(false);
  };

  const handleEditSave = async () => {
    if (!editingKey) return;
    setError("");

    const payload: Record<string, string | number | undefined> = { id: editingKey.id };
    if (editName !== editingKey.name) payload.name = editName;
    if (editExpiresInDays) payload.expiresInDays = parseInt(editExpiresInDays);

    const res = await fetchWithAuth("/api/admin/keys", {
      method: "PUT",
      body: JSON.stringify(payload),
    });

    if (res.ok) {
      const updated = await res.json();
      setKeys(keys.map((k) => (k.id === updated.id ? { ...k, ...updated } : k)));
      setEditingKey(null);
      setSuccess(`Updated key "${updated.name}"`);
      setTimeout(() => setSuccess(""), 3000);
    } else {
      const data = await res.json();
      setError(data.error || "Failed to update API key");
    }
  };

  const handleDelete = async (key: AdminApiKey) => {
    if (!confirm(`Are you sure you want to delete the key "${key.name}" (${key.userName})? This action cannot be undone.`)) return;

    const res = await fetchWithAuth(`/api/admin/keys?id=${key.id}`, { method: "DELETE" });
    if (res.ok) {
      setKeys(keys.filter((k) => k.id !== key.id));
      setPagination((p) => ({ ...p, total: p.total - 1 }));
      setSuccess(`Deleted key "${key.name}"`);
      setTimeout(() => setSuccess(""), 3000);
    } else {
      const data = await res.json();
      setError(data.error || "Failed to delete API key");
    }
  };

  const handleCopy = () => {
    if (createdKey) {
      navigator.clipboard.writeText(createdKey);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    }
  };

  const isExpired = (key: AdminApiKey) => key.expiresAt != null && new Date(key.expiresAt) < new Date();

  if (loading && keys.length === 0) {
    return <KeysListSkeleton />;
  }

  return (
    <div className="max-w-4xl mx-auto">
      <div className="flex items-center justify-between mb-6">
        <div className="flex items-center gap-3">
          <Key size={24} className="text-zinc-400" />
          <div>
            <h1 className="text-xl font-bold text-zinc-900 dark:text-zinc-100">API Key Management</h1>
            <p className="text-sm text-zinc-500">{pagination.total} keys total — all users</p>
          </div>
        </div>
        <button
          onClick={() => { setShowAddForm(true); setEditingKey(null); }}
          className="flex items-center gap-2 px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium rounded-lg"
        >
          <Plus size={16} />
          Add Key
        </button>
      </div>

      {error && (
        <div className="mb-4 p-3 bg-red-50 dark:bg-red-950 border border-red-200 dark:border-red-800 rounded-lg text-sm text-red-600 dark:text-red-400">{error}</div>
      )}
      {success && (
        <div className="mb-4 p-3 bg-green-50 dark:bg-green-950 border border-green-200 dark:border-green-800 rounded-lg text-sm text-green-600 dark:text-green-400">{success}</div>
      )}

      {createdKey && (
        <div className="mb-6 p-4 bg-amber-50 dark:bg-amber-950 border border-amber-200 dark:border-amber-800 rounded-xl">
          <div className="flex items-start gap-2">
            <AlertTriangle size={16} className="text-amber-600 mt-0.5 shrink-0" />
            <div className="flex-1">
              <p className="text-sm font-medium text-amber-800 dark:text-amber-200">
                Copy this API key now — it won&apos;t be shown again
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

      <form onSubmit={handleSearch} className="mb-6">
        <div className="relative">
          <Search size={16} className="absolute left-3 top-1/2 -translate-y-1/2 text-zinc-400" />
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search by key name, user name, or email..."
            className="w-full pl-9 pr-4 py-2 text-sm bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>
      </form>

      {showAddForm && (
        <div className="mb-6 p-5 border border-green-200 dark:border-green-800 bg-green-50 dark:bg-green-950 rounded-xl">
          <div className="flex items-center justify-between mb-4">
            <h3 className="text-sm font-medium text-green-900 dark:text-green-100">Add New API Key</h3>
            <button onClick={() => setShowAddForm(false)} className="text-green-500 hover:text-green-700"><X size={16} /></button>
          </div>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
            <div>
              <label className="text-xs text-zinc-500 mb-1 block">User *</label>
              <select value={addUserId} onChange={(e) => setAddUserId(e.target.value)} className="w-full px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800">
                <option value="">Select user...</option>
                {users.map((u) => (
                  <option key={u.id} value={u.id}>{u.name} ({u.email})</option>
                ))}
              </select>
            </div>
            <div>
              <label className="text-xs text-zinc-500 mb-1 block">Name *</label>
              <input type="text" value={addName} onChange={(e) => setAddName(e.target.value)} placeholder="e.g., CI/CD Pipeline" className="w-full px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800" />
            </div>
            <div>
              <label className="text-xs text-zinc-500 mb-1 block">Expires in (days)</label>
              <input type="number" value={addExpiresInDays} onChange={(e) => setAddExpiresInDays(parseInt(e.target.value) || 90)} className="w-full px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800" />
            </div>
          </div>
          <div className="flex gap-2 mt-4">
            <button onClick={handleAddKey} className="px-4 py-1.5 bg-green-600 hover:bg-green-700 text-white text-sm rounded-lg">Create Key</button>
            <button onClick={() => setShowAddForm(false)} className="px-4 py-1.5 text-sm text-zinc-500 hover:text-zinc-700">Cancel</button>
          </div>
        </div>
      )}

      {editingKey && (
        <div className="mb-6 p-5 border border-blue-200 dark:border-blue-800 bg-blue-50 dark:bg-blue-950 rounded-xl">
          <div className="flex items-center justify-between mb-4">
            <h3 className="text-sm font-medium text-blue-900 dark:text-blue-100">Edit Key: {editingKey.name} ({editingKey.userName})</h3>
            <button onClick={() => setEditingKey(null)} className="text-blue-500 hover:text-blue-700"><X size={16} /></button>
          </div>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
            <div>
              <label className="text-xs text-zinc-500 mb-1 block">Name</label>
              <input type="text" value={editName} onChange={(e) => setEditName(e.target.value)} className="w-full px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800" />
            </div>
            <div>
              <label className="text-xs text-zinc-500 mb-1 block">Extend expiry (days from now, leave empty to keep current)</label>
              <input type="number" value={editExpiresInDays} onChange={(e) => setEditExpiresInDays(e.target.value)} placeholder="e.g., 90" className="w-full px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800" />
            </div>
          </div>
          <div className="flex gap-2 mt-4">
            <button onClick={handleEditSave} className="px-4 py-1.5 bg-blue-600 hover:bg-blue-700 text-white text-sm rounded-lg">Save Changes</button>
            <button onClick={() => setEditingKey(null)} className="px-4 py-1.5 text-sm text-zinc-500 hover:text-zinc-700">Cancel</button>
          </div>
        </div>
      )}

      {keys.length === 0 ? (
        <div className="text-center py-8 border border-dashed border-zinc-300 dark:border-zinc-700 rounded-xl">
          <Key size={24} className="mx-auto text-zinc-300 mb-2" />
          <p className="text-zinc-500">No API keys found</p>
        </div>
      ) : (
        <div className="border border-zinc-200 dark:border-zinc-800 rounded-xl overflow-hidden">
          <table className="w-full text-sm">
            <thead>
              <tr className="bg-zinc-50 dark:bg-zinc-900 border-b border-zinc-200 dark:border-zinc-800">
                <th className="text-left px-4 py-3 font-medium text-zinc-500">Key</th>
                <th className="text-left px-4 py-3 font-medium text-zinc-500">User</th>
                <th className="text-left px-4 py-3 font-medium text-zinc-500">Last Used</th>
                <th className="text-left px-4 py-3 font-medium text-zinc-500">Expires</th>
                <th className="text-right px-4 py-3 font-medium text-zinc-500">Actions</th>
              </tr>
            </thead>
            <tbody>
              {keys.map((key) => (
                <tr key={key.id} className="border-b border-zinc-100 dark:border-zinc-800 last:border-0">
                  <td className="px-4 py-3">
                    <p className="font-medium text-zinc-900 dark:text-zinc-100">{key.name}</p>
                    <p className="text-xs text-zinc-400 font-mono">kp_{key.keyPrefix}...</p>
                  </td>
                  <td className="px-4 py-3">
                    <p className="text-zinc-900 dark:text-zinc-100">{key.userName}</p>
                    <p className="text-xs text-zinc-400">{key.userEmail}</p>
                  </td>
                  <td className="px-4 py-3 text-zinc-500">
                    {key.lastUsedAt ? new Date(key.lastUsedAt).toLocaleDateString() : "Never"}
                  </td>
                  <td className="px-4 py-3">
                    {key.expiresAt ? (
                      <span className={isExpired(key) ? "text-red-500" : "text-zinc-500"}>
                        {isExpired(key) ? "Expired" : new Date(key.expiresAt).toLocaleDateString()}
                      </span>
                    ) : (
                      <span className="text-zinc-400">—</span>
                    )}
                  </td>
                  <td className="px-4 py-3">
                    <div className="flex items-center justify-end gap-1">
                      <button onClick={() => handleEditStart(key)} className="p-1.5 rounded-lg hover:bg-zinc-100 dark:hover:bg-zinc-800 text-zinc-400 hover:text-blue-600" title="Edit key">
                        <Pencil size={14} />
                      </button>
                      <button onClick={() => handleDelete(key)} className="p-1.5 rounded-lg hover:bg-red-50 dark:hover:bg-red-950 text-zinc-400 hover:text-red-600" title="Delete key">
                        <Trash2 size={14} />
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {pagination.pages > 1 && (
        <div className="flex items-center justify-between mt-4">
          <p className="text-xs text-zinc-500">Page {pagination.page} of {pagination.pages}</p>
          <div className="flex items-center gap-1">
            <button onClick={() => loadKeys(pagination.page - 1, search)} disabled={pagination.page <= 1} className="p-1.5 rounded-lg border border-zinc-300 dark:border-zinc-700 disabled:opacity-30 hover:bg-zinc-50 dark:hover:bg-zinc-800">
              <ChevronLeft size={14} />
            </button>
            <button onClick={() => loadKeys(pagination.page + 1, search)} disabled={pagination.page >= pagination.pages} className="p-1.5 rounded-lg border border-zinc-300 dark:border-zinc-700 disabled:opacity-30 hover:bg-zinc-50 dark:hover:bg-zinc-800">
              <ChevronRight size={14} />
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
