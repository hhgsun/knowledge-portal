import { useState, useEffect, useCallback } from "react";
import { Tag, Pencil, Trash2, Plus, X, Check } from "lucide-react";
import { useApi } from "../hooks/useApi";
import { toast } from "sonner";
import type { TagWithCount } from "../types/api";

export default function TagsPage() {
  const { fetchWithAuth } = useApi();
  const [tags, setTags] = useState<TagWithCount[]>([]);
  const [loading, setLoading] = useState(true);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editName, setEditName] = useState("");
  const [newTagName, setNewTagName] = useState("");
  const [showCreate, setShowCreate] = useState(false);

  const loadTags = useCallback(async () => {
    try {
      const res = await fetchWithAuth("/api/tags");
      const data = await res.json();
      setTags(Array.isArray(data) ? data : []);
    } catch {
      toast.error("Failed to load tags");
    } finally {
      setLoading(false);
    }
  }, [fetchWithAuth]);

  useEffect(() => {
    loadTags();
  }, [loadTags]);

  const handleCreate = async () => {
    if (!newTagName.trim()) return;
    const res = await fetchWithAuth("/api/tags", {
      method: "POST",
      body: JSON.stringify({ name: newTagName.trim() }),
    });
    if (res.ok) {
      toast.success("Tag created");
      setNewTagName("");
      setShowCreate(false);
      loadTags();
    } else {
      const data = await res.json();
      toast.error(data.error || "Failed to create tag");
    }
  };

  const handleUpdate = async (id: string) => {
    if (!editName.trim()) return;
    const res = await fetchWithAuth("/api/tags", {
      method: "PUT",
      body: JSON.stringify({ id, name: editName.trim() }),
    });
    if (res.ok) {
      toast.success("Tag updated");
      setEditingId(null);
      loadTags();
    } else {
      const data = await res.json();
      toast.error(data.error || "Failed to update tag");
    }
  };

  const handleDelete = async (tag: TagWithCount) => {
    if (!confirm(`Delete tag "${tag.name}"?`)) return;
    const res = await fetchWithAuth(`/api/tags?id=${tag.id}`, {
      method: "DELETE",
    });
    if (res.ok) {
      toast.success("Tag deleted");
      loadTags();
    } else {
      const data = await res.json();
      toast.error(data.error || "Failed to delete tag");
    }
  };

  const startEdit = (tag: TagWithCount) => {
    setEditingId(tag.id);
    setEditName(tag.name);
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center py-12 text-zinc-500">
        Loading...
      </div>
    );
  }

  return (
    <div className="max-w-3xl mx-auto">
      <div className="flex items-center justify-between mb-6">
        <div className="flex items-center gap-3">
          <Tag size={24} className="text-blue-600" />
          <h1 className="text-2xl font-bold text-zinc-900 dark:text-zinc-100">
            Tags
          </h1>
          <span className="text-sm text-zinc-500">({tags.length})</span>
        </div>
        <button
          onClick={() => setShowCreate(true)}
          className="flex items-center gap-2 px-3 py-2 text-sm font-medium text-white bg-blue-600 rounded-lg hover:bg-blue-700 transition-colors"
        >
          <Plus size={16} />
          New Tag
        </button>
      </div>

      {/* Create new tag */}
      {showCreate && (
        <div className="mb-4 flex items-center gap-2 p-3 bg-zinc-50 dark:bg-zinc-900 rounded-lg border border-zinc-200 dark:border-zinc-800">
          <input
            type="text"
            value={newTagName}
            onChange={(e) => setNewTagName(e.target.value)}
            onKeyDown={(e) => e.key === "Enter" && handleCreate()}
            placeholder="Tag name..."
            maxLength={50}
            autoFocus
            className="flex-1 px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
          <button
            onClick={handleCreate}
            className="p-1.5 text-green-600 hover:bg-green-50 dark:hover:bg-green-900/30 rounded-lg"
            title="Save"
          >
            <Check size={18} />
          </button>
          <button
            onClick={() => { setShowCreate(false); setNewTagName(""); }}
            className="p-1.5 text-zinc-500 hover:bg-zinc-100 dark:hover:bg-zinc-800 rounded-lg"
            title="Cancel"
          >
            <X size={18} />
          </button>
        </div>
      )}

      {/* Tags list */}
      <div className="border border-zinc-200 dark:border-zinc-800 rounded-lg overflow-hidden">
        <table className="w-full text-sm">
          <thead className="bg-zinc-50 dark:bg-zinc-900 border-b border-zinc-200 dark:border-zinc-800">
            <tr>
              <th className="text-left px-4 py-3 font-medium text-zinc-600 dark:text-zinc-400">Name</th>
              <th className="text-left px-4 py-3 font-medium text-zinc-600 dark:text-zinc-400">Slug</th>
              <th className="text-center px-4 py-3 font-medium text-zinc-600 dark:text-zinc-400">Articles</th>
              <th className="text-right px-4 py-3 font-medium text-zinc-600 dark:text-zinc-400">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-zinc-200 dark:divide-zinc-800">
            {tags.map((tag) => (
              <tr key={tag.id} className="hover:bg-zinc-50 dark:hover:bg-zinc-900/50">
                <td className="px-4 py-3">
                  {editingId === tag.id ? (
                    <input
                      type="text"
                      value={editName}
                      onChange={(e) => setEditName(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key === "Enter") handleUpdate(tag.id);
                        if (e.key === "Escape") setEditingId(null);
                      }}
                      maxLength={50}
                      autoFocus
                      className="px-2 py-1 text-sm border border-blue-400 rounded bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500 w-full"
                    />
                  ) : (
                    <span className="text-zinc-900 dark:text-zinc-100 font-medium">{tag.name}</span>
                  )}
                </td>
                <td className="px-4 py-3 text-zinc-500">{tag.slug}</td>
                <td className="px-4 py-3 text-center">
                  <span className={`inline-flex items-center justify-center min-w-[24px] px-1.5 py-0.5 rounded-full text-xs font-medium ${tag.articleCount > 0 ? "bg-blue-100 text-blue-700 dark:bg-blue-900 dark:text-blue-300" : "bg-zinc-100 text-zinc-500 dark:bg-zinc-800 dark:text-zinc-500"}`}>
                    {tag.articleCount}
                  </span>
                </td>
                <td className="px-4 py-3 text-right">
                  <div className="flex items-center justify-end gap-1">
                    {editingId === tag.id ? (
                      <>
                        <button
                          onClick={() => handleUpdate(tag.id)}
                          className="p-1.5 text-green-600 hover:bg-green-50 dark:hover:bg-green-900/30 rounded-lg"
                          title="Save"
                        >
                          <Check size={16} />
                        </button>
                        <button
                          onClick={() => setEditingId(null)}
                          className="p-1.5 text-zinc-500 hover:bg-zinc-100 dark:hover:bg-zinc-800 rounded-lg"
                          title="Cancel"
                        >
                          <X size={16} />
                        </button>
                      </>
                    ) : (
                      <>
                        <button
                          onClick={() => startEdit(tag)}
                          className="p-1.5 text-zinc-500 hover:text-blue-600 hover:bg-blue-50 dark:hover:bg-blue-900/30 rounded-lg"
                          title="Edit"
                        >
                          <Pencil size={16} />
                        </button>
                        <button
                          onClick={() => handleDelete(tag)}
                          disabled={tag.articleCount > 0}
                          className={`p-1.5 rounded-lg ${tag.articleCount > 0 ? "text-zinc-300 dark:text-zinc-700 cursor-not-allowed" : "text-zinc-500 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/30"}`}
                          title={tag.articleCount > 0 ? "Cannot delete: tag has articles" : "Delete"}
                        >
                          <Trash2 size={16} />
                        </button>
                      </>
                    )}
                  </div>
                </td>
              </tr>
            ))}
            {tags.length === 0 && (
              <tr>
                <td colSpan={4} className="px-4 py-8 text-center text-zinc-500">
                  No tags yet. Create one to get started.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
