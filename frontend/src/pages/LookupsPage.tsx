import { useEffect, useState } from "react";
import { Plus, Trash2, ToggleLeft, ToggleRight } from "lucide-react";
import { useApi } from "../hooks/useApi";
import { useLookups } from "../hooks/useLookups";
import { toast } from "sonner";
import type { LookupValue } from "../types/api";

export default function LookupsPage() {
  const { fetchWithAuth } = useApi();
  const { invalidateCache } = useLookups();
  const [lookups, setLookups] = useState<LookupValue[]>([]);
  const [loading, setLoading] = useState(true);
  const [showAdd, setShowAdd] = useState(false);
  const [newCategory, setNewCategory] = useState("content_type");
  const [newValue, setNewValue] = useState("");
  const [newLabel, setNewLabel] = useState("");

  const loadLookups = async () => {
    const res = await fetchWithAuth("/api/lookups");
    if (res.ok) {
      setLookups(await res.json());
    }
    setLoading(false);
  };

  useEffect(() => { loadLookups(); }, [fetchWithAuth]);

  const handleAdd = async () => {
    if (!newValue.trim() || !newLabel.trim()) {
      toast.error("Value and label are required");
      return;
    }
    const res = await fetchWithAuth("/api/lookups", {
      method: "POST",
      body: JSON.stringify({ category: newCategory, value: newValue.trim().toLowerCase(), label: newLabel.trim() }),
    });
    if (res.ok) {
      toast.success("Lookup value added");
      setNewValue("");
      setNewLabel("");
      setShowAdd(false);
      invalidateCache();
      loadLookups();
    } else {
      const err = await res.json();
      toast.error(err.error || "Failed to add");
    }
  };

  const handleToggle = async (item: LookupValue) => {
    const res = await fetchWithAuth("/api/lookups", {
      method: "PUT",
      body: JSON.stringify({ id: item.id, isActive: !item.isActive }),
    });
    if (res.ok) {
      toast.success(item.isActive ? "Deactivated" : "Activated");
      invalidateCache();
      loadLookups();
    } else {
      const err = await res.json();
      toast.error(err.error || "Failed to update");
    }
  };

  const handleDelete = async (item: LookupValue) => {
    if (!confirm(`Delete "${item.label}"? This cannot be undone.`)) return;
    const res = await fetchWithAuth(`/api/lookups?id=${item.id}`, { method: "DELETE" });
    if (res.ok) {
      toast.success("Deleted");
      invalidateCache();
      loadLookups();
    } else {
      const err = await res.json();
      toast.error(err.error || "Failed to delete");
    }
  };

  const contentTypes = lookups.filter((l) => l.category === "content_type");

  if (loading) return <div className="text-center py-12 text-zinc-500">Loading...</div>;

  return (
    <div className="max-w-3xl mx-auto">
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-bold text-zinc-900 dark:text-zinc-100">Lookup Values</h1>
          <p className="text-sm text-zinc-500 mt-1">Manage article content types</p>
        </div>
        <button
          onClick={() => setShowAdd(!showAdd)}
          className="flex items-center gap-2 px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium rounded-lg"
        >
          <Plus size={16} />
          Add Value
        </button>
      </div>

      {showAdd && (
        <div className="mb-6 p-4 border border-zinc-200 dark:border-zinc-800 rounded-xl bg-zinc-50 dark:bg-zinc-900">
          <div className="flex flex-wrap gap-3 items-end">
            <div>
              <label className="text-xs font-medium text-zinc-500 block mb-1">Category</label>
              <select
                value={newCategory}
                onChange={(e) => setNewCategory(e.target.value)}
                className="px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800"
              >
                <option value="content_type">Content Type</option>
              </select>
            </div>
            <div>
              <label className="text-xs font-medium text-zinc-500 block mb-1">Value (slug)</label>
              <input
                value={newValue}
                onChange={(e) => setNewValue(e.target.value)}
                placeholder="e.g. tutorial"
                className="px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800"
              />
            </div>
            <div>
              <label className="text-xs font-medium text-zinc-500 block mb-1">Label (display)</label>
              <input
                value={newLabel}
                onChange={(e) => setNewLabel(e.target.value)}
                placeholder="e.g. Tutorial"
                className="px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800"
              />
            </div>
            <button
              onClick={handleAdd}
              className="px-4 py-1.5 text-sm bg-green-600 hover:bg-green-700 text-white rounded-lg"
            >
              Add
            </button>
          </div>
        </div>
      )}

      <div className="space-y-6">
        <LookupSection title="Content Types" items={contentTypes} onToggle={handleToggle} onDelete={handleDelete} />
      </div>
    </div>
  );
}

function LookupSection({ title, items, onToggle, onDelete }: {
  title: string;
  items: LookupValue[];
  onToggle: (item: LookupValue) => void;
  onDelete: (item: LookupValue) => void;
}) {
  return (
    <div>
      <h2 className="text-sm font-medium text-zinc-700 dark:text-zinc-300 mb-3">{title}</h2>
      <div className="border border-zinc-200 dark:border-zinc-800 rounded-xl overflow-hidden">
        {items.length === 0 ? (
          <p className="p-4 text-sm text-zinc-500">No values defined</p>
        ) : (
          items.map((item) => (
            <div
              key={item.id}
              className={`flex items-center justify-between px-4 py-3 border-b last:border-b-0 border-zinc-100 dark:border-zinc-800 ${
                !item.isActive ? "opacity-50" : ""
              }`}
            >
              <div>
                <span className="text-sm font-medium text-zinc-900 dark:text-zinc-100">{item.label}</span>
                <span className="text-xs text-zinc-400 ml-2">({item.value})</span>
                {!item.isActive && <span className="ml-2 text-xs text-amber-600 dark:text-amber-400">Inactive</span>}
              </div>
              <div className="flex items-center gap-2">
                <button
                  onClick={() => onToggle(item)}
                  className="p-1.5 rounded-lg hover:bg-zinc-100 dark:hover:bg-zinc-800 text-zinc-500"
                  title={item.isActive ? "Deactivate" : "Activate"}
                >
                  {item.isActive ? <ToggleRight size={18} className="text-green-600" /> : <ToggleLeft size={18} />}
                </button>
                <button
                  onClick={() => onDelete(item)}
                  className="p-1.5 rounded-lg hover:bg-red-50 dark:hover:bg-red-950 text-zinc-400 hover:text-red-600"
                  title="Delete"
                >
                  <Trash2 size={16} />
                </button>
              </div>
            </div>
          ))
        )}
      </div>
    </div>
  );
}
