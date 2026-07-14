import { useEffect, useState } from "react";
import { Plus, Trash2, ToggleLeft, ToggleRight, ArrowUp, ArrowDown, Pencil, ExternalLink } from "lucide-react";
import { useApi } from "../hooks/useApi";
import { useLookups } from "../hooks/useLookups";
import { invalidateFeaturedLinksCache, resolveFeaturedLinkHref } from "../hooks/useFeaturedLinks";
import { toast } from "sonner";
import { getIconComponent } from "../lib/lookup-utils";
import { IconPicker } from "../components/lookup-pickers";
import type { FeaturedLink, TagWithCount } from "../types/api";

const LINK_TYPE_LABELS: Record<FeaturedLink["linkType"], string> = {
  content_type: "Content Type",
  tag: "Tag",
  custom: "Custom Link",
};

export default function FeaturedLinksPage() {
  const { fetchWithAuth } = useApi();
  const { contentTypes } = useLookups();
  const [links, setLinks] = useState<FeaturedLink[]>([]);
  const [tags, setTags] = useState<TagWithCount[]>([]);
  const [loading, setLoading] = useState(true);
  const [showAdd, setShowAdd] = useState(false);
  const [newLabel, setNewLabel] = useState("");
  const [newLinkType, setNewLinkType] = useState<FeaturedLink["linkType"]>("content_type");
  const [newTarget, setNewTarget] = useState("");
  const [newIcon, setNewIcon] = useState("star");
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editLabel, setEditLabel] = useState("");
  const [editIcon, setEditIcon] = useState("");

  const loadLinks = async () => {
    const res = await fetchWithAuth("/api/featured-links?includeInactive=true");
    if (res.ok) {
      setLinks(await res.json());
    }
    setLoading(false);
  };

  useEffect(() => {
    loadLinks();
    fetchWithAuth("/api/tags")
      .then((res) => res.json())
      .then((data) => {
        if (Array.isArray(data)) setTags(data);
      })
      .catch(() => {});
  }, [fetchWithAuth]);

  const afterMutation = () => {
    invalidateFeaturedLinksCache();
    loadLinks();
  };

  const handleAdd = async () => {
    if (!newLabel.trim() || !newTarget.trim()) {
      toast.error("Label and target are required");
      return;
    }
    const res = await fetchWithAuth("/api/featured-links", {
      method: "POST",
      body: JSON.stringify({ label: newLabel.trim(), linkType: newLinkType, target: newTarget.trim(), icon: newIcon }),
    });
    if (res.ok) {
      toast.success("Featured link added");
      setNewLabel("");
      setNewTarget("");
      setNewIcon("star");
      setShowAdd(false);
      afterMutation();
    } else {
      const err = await res.json();
      toast.error(err.error || "Failed to add");
    }
  };

  const handleUpdate = async (body: Record<string, unknown>, successMessage: string) => {
    const res = await fetchWithAuth("/api/featured-links", {
      method: "PUT",
      body: JSON.stringify(body),
    });
    if (res.ok) {
      toast.success(successMessage);
      afterMutation();
      return true;
    }
    const err = await res.json();
    toast.error(err.error || "Failed to update");
    return false;
  };

  const handleToggle = (link: FeaturedLink) =>
    handleUpdate({ id: link.id, isActive: !link.isActive }, link.isActive ? "Deactivated" : "Activated");

  const handleMove = async (index: number, direction: -1 | 1) => {
    const other = links[index + direction];
    const current = links[index];
    if (!other) return;
    await fetchWithAuth("/api/featured-links", {
      method: "PUT",
      body: JSON.stringify({ id: current.id, sortOrder: other.sortOrder }),
    });
    await fetchWithAuth("/api/featured-links", {
      method: "PUT",
      body: JSON.stringify({ id: other.id, sortOrder: current.sortOrder }),
    });
    afterMutation();
  };

  const handleDelete = async (link: FeaturedLink) => {
    if (!confirm(`Delete "${link.label}"? This cannot be undone.`)) return;
    const res = await fetchWithAuth(`/api/featured-links?id=${link.id}`, { method: "DELETE" });
    if (res.ok) {
      toast.success("Deleted");
      afterMutation();
    } else {
      const err = await res.json();
      toast.error(err.error || "Failed to delete");
    }
  };

  const handleEdit = (link: FeaturedLink) => {
    setEditingId(link.id);
    setEditLabel(link.label);
    setEditIcon(link.icon || "star");
  };

  const handleSaveEdit = async (link: FeaturedLink) => {
    const ok = await handleUpdate({ id: link.id, label: editLabel.trim(), icon: editIcon }, "Updated");
    if (ok) setEditingId(null);
  };

  const targetInput = () => {
    if (newLinkType === "content_type") {
      return (
        <select
          value={newTarget}
          onChange={(e) => setNewTarget(e.target.value)}
          className="px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800"
        >
          <option value="">Select content type…</option>
          {contentTypes.filter((ct) => ct.isActive).map((ct) => (
            <option key={ct.id} value={ct.value}>{ct.label}</option>
          ))}
        </select>
      );
    }
    if (newLinkType === "tag") {
      return (
        <select
          value={newTarget}
          onChange={(e) => setNewTarget(e.target.value)}
          className="px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800"
        >
          <option value="">Select tag…</option>
          {tags.map((t) => (
            <option key={t.id} value={t.slug}>{t.name}</option>
          ))}
        </select>
      );
    }
    return (
      <input
        value={newTarget}
        onChange={(e) => setNewTarget(e.target.value)}
        placeholder="/articles?status=draft or https://…"
        className="px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800 w-64"
      />
    );
  };

  if (loading) return <div className="max-w-3xl mx-auto text-sm text-zinc-500">Loading…</div>;

  return (
    <div className="max-w-3xl mx-auto">
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-bold text-zinc-900 dark:text-zinc-100">Featured Links</h1>
          <p className="text-sm text-zinc-500 mt-1">Manage the "Seçkinler" menu shown in the sidebar</p>
        </div>
        <button
          onClick={() => setShowAdd(!showAdd)}
          className="flex items-center gap-2 px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium rounded-lg"
        >
          <Plus size={16} />
          Add Link
        </button>
      </div>

      {showAdd && (
        <div className="mb-6 p-4 border border-zinc-200 dark:border-zinc-800 rounded-xl bg-zinc-50 dark:bg-zinc-900">
          <div className="flex flex-wrap gap-3 items-end">
            <div>
              <label className="text-xs font-medium text-zinc-500 block mb-1">Type</label>
              <select
                value={newLinkType}
                onChange={(e) => {
                  setNewLinkType(e.target.value as FeaturedLink["linkType"]);
                  setNewTarget("");
                }}
                className="px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800"
              >
                <option value="content_type">Content Type</option>
                <option value="tag">Tag</option>
                <option value="custom">Custom Link</option>
              </select>
            </div>
            <div>
              <label className="text-xs font-medium text-zinc-500 block mb-1">Target</label>
              {targetInput()}
            </div>
            <div>
              <label className="text-xs font-medium text-zinc-500 block mb-1">Label (display)</label>
              <input
                value={newLabel}
                onChange={(e) => setNewLabel(e.target.value)}
                placeholder="e.g. Tutorials"
                className="px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800"
              />
            </div>
          </div>
          <div className="flex flex-wrap gap-3 items-end mt-3">
            <div>
              <label className="text-xs font-medium text-zinc-500 block mb-1">Icon</label>
              <IconPicker value={newIcon} onChange={setNewIcon} />
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

      <div className="border border-zinc-200 dark:border-zinc-800 rounded-xl">
        {links.length === 0 ? (
          <p className="p-4 text-sm text-zinc-500">No featured links defined</p>
        ) : (
          links.map((link, index) => {
            const IconComp = getIconComponent(link.icon || "star");
            const { href, external } = resolveFeaturedLinkHref(link);
            return (
              <div
                key={link.id}
                className={`border-b last:border-b-0 border-zinc-100 dark:border-zinc-800 ${
                  !link.isActive ? "opacity-50" : ""
                }`}
              >
                <div className="flex items-center justify-between px-4 py-3">
                  <div className="flex items-center gap-3 min-w-0">
                    <span className="inline-flex items-center justify-center w-8 h-8 rounded-lg bg-zinc-100 dark:bg-zinc-800 shrink-0">
                      <IconComp size={16} className="text-zinc-600 dark:text-zinc-300" />
                    </span>
                    <div className="min-w-0">
                      <div className="flex items-center gap-2">
                        <span className="text-sm font-medium text-zinc-900 dark:text-zinc-100">{link.label}</span>
                        <span className="text-[10px] px-1.5 py-0.5 rounded bg-zinc-100 dark:bg-zinc-800 text-zinc-500 uppercase tracking-wide shrink-0">
                          {LINK_TYPE_LABELS[link.linkType]}
                        </span>
                        {external && <ExternalLink size={12} className="text-zinc-400 shrink-0" />}
                        {!link.isActive && <span className="text-xs text-amber-600 dark:text-amber-400 shrink-0">Inactive</span>}
                      </div>
                      <span className="text-xs text-zinc-400 truncate block">{href}</span>
                    </div>
                  </div>
                  <div className="flex items-center gap-1 shrink-0">
                    <button
                      onClick={() => handleMove(index, -1)}
                      disabled={index === 0}
                      className="p-1.5 rounded-lg hover:bg-zinc-100 dark:hover:bg-zinc-800 text-zinc-500 disabled:opacity-30 disabled:hover:bg-transparent"
                      title="Move up"
                    >
                      <ArrowUp size={16} />
                    </button>
                    <button
                      onClick={() => handleMove(index, 1)}
                      disabled={index === links.length - 1}
                      className="p-1.5 rounded-lg hover:bg-zinc-100 dark:hover:bg-zinc-800 text-zinc-500 disabled:opacity-30 disabled:hover:bg-transparent"
                      title="Move down"
                    >
                      <ArrowDown size={16} />
                    </button>
                    <button
                      onClick={() => handleEdit(link)}
                      className="p-1.5 rounded-lg hover:bg-zinc-100 dark:hover:bg-zinc-800 text-zinc-500"
                      title="Edit label & icon"
                    >
                      <Pencil size={16} />
                    </button>
                    <button
                      onClick={() => handleToggle(link)}
                      className="p-1.5 rounded-lg hover:bg-zinc-100 dark:hover:bg-zinc-800 text-zinc-500"
                      title={link.isActive ? "Deactivate" : "Activate"}
                    >
                      {link.isActive ? <ToggleRight size={18} className="text-green-600" /> : <ToggleLeft size={18} />}
                    </button>
                    <button
                      onClick={() => handleDelete(link)}
                      className="p-1.5 rounded-lg hover:bg-red-50 dark:hover:bg-red-950 text-zinc-400 hover:text-red-600"
                      title="Delete"
                    >
                      <Trash2 size={16} />
                    </button>
                  </div>
                </div>
                {editingId === link.id && (
                  <div className="px-4 pb-3 pt-1 border-t border-zinc-100 dark:border-zinc-800 bg-zinc-50 dark:bg-zinc-900/50">
                    <div className="flex flex-wrap gap-3 items-center">
                      <div>
                        <label className="text-xs font-medium text-zinc-500 block mb-1">Label</label>
                        <input
                          value={editLabel}
                          onChange={(e) => setEditLabel(e.target.value)}
                          className="px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800"
                        />
                      </div>
                      <div>
                        <label className="text-xs font-medium text-zinc-500 block mb-1">Icon</label>
                        <IconPicker value={editIcon} onChange={setEditIcon} />
                      </div>
                      <div className="flex gap-2 ml-auto">
                        <button
                          onClick={() => handleSaveEdit(link)}
                          className="px-3 py-1.5 text-xs bg-blue-600 hover:bg-blue-700 text-white rounded-lg"
                        >
                          Save
                        </button>
                        <button
                          onClick={() => setEditingId(null)}
                          className="px-3 py-1.5 text-xs bg-zinc-200 dark:bg-zinc-700 hover:bg-zinc-300 dark:hover:bg-zinc-600 text-zinc-700 dark:text-zinc-300 rounded-lg"
                        >
                          Cancel
                        </button>
                      </div>
                    </div>
                  </div>
                )}
              </div>
            );
          })
        )}
      </div>
    </div>
  );
}
