import { useCallback, useEffect, useState } from "react";
import { Plus, Trash2, ToggleLeft, ToggleRight, Palette, Layers } from "lucide-react";
import { useApi } from "../hooks/useApi";
import { useLookups } from "../hooks/useLookups";
import { toast } from "sonner";
import { getColorClasses, getIconComponent } from "../lib/lookup-utils";
import { ColorPicker, IconPicker } from "../components/lookup-pickers";
import { LookupsListSkeleton } from "../components/ui/skeleton";
import type { LookupCategory, LookupValue } from "../types/api";

export default function LookupsPage() {
  const { fetchWithAuth } = useApi();
  const { invalidateCache } = useLookups();
  const [lookups, setLookups] = useState<LookupValue[]>([]);
  const [categories, setCategories] = useState<LookupCategory[]>([]);
  const [loading, setLoading] = useState(true);
  const [showAdd, setShowAdd] = useState(false);
  const [showAddCategory, setShowAddCategory] = useState(false);
  const [newCategory, setNewCategory] = useState("content_type");
  const [newValue, setNewValue] = useState("");
  const [newLabel, setNewLabel] = useState("");
  const [newColor, setNewColor] = useState("blue");
  const [newIcon, setNewIcon] = useState("file-text");
  const [newAuthorityWeight, setNewAuthorityWeight] = useState(50);
  const [newCategoryKey, setNewCategoryKey] = useState("");
  const [newCategoryLabel, setNewCategoryLabel] = useState("");
  const [newCardinality, setNewCardinality] = useState<"single" | "multiple">("single");
  const [newRagBehavior, setNewRagBehavior] = useState<"none" | "filter" | "boost">("filter");

  const loadLookups = useCallback(async () => {
    try {
      const [valuesResponse, categoriesResponse] = await Promise.all([
        fetchWithAuth("/api/lookups"), fetchWithAuth("/api/lookups/categories"),
      ]);
      if (valuesResponse.ok) setLookups(await valuesResponse.json());
      if (categoriesResponse.ok) setCategories(await categoriesResponse.json());
    } catch {
      // useApi displays the shared network error.
    } finally {
      setLoading(false);
    }
  }, [fetchWithAuth]);

  useEffect(() => { void loadLookups(); }, [loadLookups]);

  const handleAdd = async () => {
    if (!newValue.trim() || !newLabel.trim()) {
      toast.error("Değer ve etiket zorunludur");
      return;
    }
    const res = await fetchWithAuth("/api/lookups", {
      method: "POST",
      body: JSON.stringify({ category: newCategory, value: newValue.trim().toLowerCase(), label: newLabel.trim(), color: newColor, icon: newIcon,
        authorityWeight: newCategory === "content_type" ? newAuthorityWeight : undefined }),
    });
    if (res.ok) {
      toast.success("Tanım değeri eklendi");
      setNewValue("");
      setNewLabel("");
      setNewColor("blue");
      setNewIcon("file-text");
      setNewAuthorityWeight(50);
      setShowAdd(false);
      invalidateCache();
      loadLookups();
    } else {
      const err = await res.json();
      toast.error(err.error || "Ekleme başarısız");
    }
  };

  const handleAddCategory = async () => {
    if (!newCategoryKey.trim() || !newCategoryLabel.trim()) {
      toast.error("Kategori anahtarı ve etiketi zorunludur");
      return;
    }
    const res = await fetchWithAuth("/api/lookups/categories", {
      method: "POST",
      body: JSON.stringify({ key: newCategoryKey, label: newCategoryLabel,
        cardinality: newCardinality, ragBehavior: newRagBehavior }),
    });
    if (!res.ok) {
      const error = await res.json();
      toast.error(error.error || "Kategori eklenemedi");
      return;
    }
    const created = await res.json() as LookupCategory;
    setNewCategory(created.key);
    setNewCategoryKey("");
    setNewCategoryLabel("");
    setShowAddCategory(false);
    invalidateCache();
    await loadLookups();
    toast.success("Sınıflandırma kategorisi eklendi");
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
      toast.error(err.error || "Güncelleme başarısız");
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
      toast.error(err.error || "Silme başarısız");
    }
  };

  if (loading) return <LookupsListSkeleton />;

  return (
    <div className="max-w-3xl mx-auto">
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-bold text-zinc-900 dark:text-zinc-100">Tanım Değerleri</h1>
          <p className="text-sm text-zinc-500 mt-1">İçerik türü, departman, sistem ve diğer kontrollü sınıflandırmaları yönetin</p>
        </div>
        <div className="flex gap-2">
          <button onClick={() => setShowAddCategory(!showAddCategory)} className="flex items-center gap-2 rounded-lg border border-zinc-300 px-3 py-2 text-sm dark:border-zinc-700">
            <Layers size={16} /> Kategori Ekle
          </button>
          <button onClick={() => setShowAdd(!showAdd)} className="flex items-center gap-2 px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium rounded-lg">
            <Plus size={16} /> Değer Ekle
          </button>
        </div>
      </div>

      {showAddCategory && (
        <div className="mb-6 grid gap-3 rounded-xl border border-zinc-200 bg-zinc-50 p-4 sm:grid-cols-2 dark:border-zinc-800 dark:bg-zinc-900">
          <input value={newCategoryKey} onChange={(event) => setNewCategoryKey(event.target.value)} placeholder="Anahtar (örn. department)" className="rounded-lg border border-zinc-300 bg-white px-3 py-2 text-sm dark:border-zinc-700 dark:bg-zinc-800" />
          <input value={newCategoryLabel} onChange={(event) => setNewCategoryLabel(event.target.value)} placeholder="Etiket (örn. Departman)" className="rounded-lg border border-zinc-300 bg-white px-3 py-2 text-sm dark:border-zinc-700 dark:bg-zinc-800" />
          <select value={newCardinality} onChange={(event) => setNewCardinality(event.target.value as "single" | "multiple")} className="rounded-lg border border-zinc-300 bg-white px-3 py-2 text-sm dark:border-zinc-700 dark:bg-zinc-800">
            <option value="single">Tek değer</option><option value="multiple">Birden çok değer</option>
          </select>
          <select value={newRagBehavior} onChange={(event) => setNewRagBehavior(event.target.value as "none" | "filter" | "boost")} className="rounded-lg border border-zinc-300 bg-white px-3 py-2 text-sm dark:border-zinc-700 dark:bg-zinc-800">
            <option value="filter">AI filtresi</option><option value="boost">AI boost/filtresi</option><option value="none">AI dışında</option>
          </select>
          <button onClick={handleAddCategory} className="rounded-lg bg-green-600 px-4 py-2 text-sm text-white sm:col-span-2">Kategori Ekle</button>
        </div>
      )}

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
                {categories.filter((category) => category.isActive).map((category) => (
                  <option key={category.id} value={category.key}>{category.label}</option>
                ))}
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
          </div>
          <div className="flex flex-wrap gap-3 items-end mt-3">
            <div>
              <label className="text-xs font-medium text-zinc-500 block mb-1">Color</label>
              <ColorPicker value={newColor} onChange={setNewColor} />
            </div>
            <div>
              <label className="text-xs font-medium text-zinc-500 block mb-1">Icon</label>
              <IconPicker value={newIcon} onChange={setNewIcon} />
            </div>
            {newCategory === "content_type" && <div>
              <label className="text-xs font-medium text-zinc-500 block mb-1">Authority (0-100)</label>
              <input type="number" min={0} max={100} value={newAuthorityWeight} onChange={(e) => setNewAuthorityWeight(Number(e.target.value))} className="w-24 px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800" />
            </div>}
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
        {categories.map((category) => (
          <LookupSection key={category.id} category={category}
            items={lookups.filter((lookup) => lookup.category === category.key)}
            onToggle={handleToggle} onDelete={handleDelete} onReload={loadLookups} />
        ))}
      </div>
    </div>
  );
}

function LookupSection({ category, items, onToggle, onDelete, onReload }: {
  category: LookupCategory;
  items: LookupValue[];
  onToggle: (item: LookupValue) => void;
  onDelete: (item: LookupValue) => void;
  onReload: () => void;
}) {
  const { fetchWithAuth } = useApi();
  const { invalidateCache } = useLookups();
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editColor, setEditColor] = useState("");
  const [editIcon, setEditIcon] = useState("");
  const [editAuthorityWeight, setEditAuthorityWeight] = useState(50);

  const updateCategory = async (patch: Partial<LookupCategory>) => {
    const response = await fetchWithAuth("/api/lookups/categories", {
      method: "PUT", body: JSON.stringify({ id: category.id, ...patch }),
    });
    if (!response.ok) {
      const error = await response.json(); toast.error(error.error || "Kategori güncellenemedi"); return;
    }
    invalidateCache(); onReload(); toast.success("Kategori güncellendi");
  };

  const deleteCategory = async () => {
    if (!confirm(`Delete category "${category.label}"?`)) return;
    const response = await fetchWithAuth(`/api/lookups/categories?id=${category.id}`, { method: "DELETE" });
    if (!response.ok) {
      const error = await response.json(); toast.error(error.error || "Kategori silinemedi"); return;
    }
    invalidateCache(); onReload(); toast.success("Kategori silindi");
  };

  const handleEdit = (item: LookupValue) => {
    setEditingId(item.id);
    setEditColor(item.color || "blue");
    setEditIcon(item.icon || "file-text");
    setEditAuthorityWeight(item.authorityWeight);
  };

  const handleSaveEdit = async (item: LookupValue) => {
    const res = await fetchWithAuth("/api/lookups", {
      method: "PUT",
      body: JSON.stringify({ id: item.id, color: editColor, icon: editIcon,
        authorityWeight: item.category === "content_type" ? editAuthorityWeight : undefined }),
    });
    if (res.ok) {
      toast.success("Updated");
      invalidateCache();
      setEditingId(null);
      onReload();
    } else {
      const err = await res.json();
      toast.error(err.error || "Güncelleme başarısız");
    }
  };

  return (
    <div>
      <div className="mb-3 flex flex-wrap items-end gap-2">
        <h2 className="mr-auto text-sm font-medium text-zinc-700 dark:text-zinc-300">{category.label} <span className="text-zinc-400">({category.key})</span></h2>
        {category.key !== "content_type" && <>
          <select value={category.cardinality} onChange={event => void updateCategory({ cardinality: event.target.value as "single" | "multiple" })} className="rounded border border-zinc-300 bg-white px-2 py-1 text-xs dark:border-zinc-700 dark:bg-zinc-900">
            <option value="single">Tekli</option><option value="multiple">Çoklu</option>
          </select>
          <select value={category.ragBehavior} onChange={event => void updateCategory({ ragBehavior: event.target.value as "none" | "filter" | "boost" })} className="rounded border border-zinc-300 bg-white px-2 py-1 text-xs dark:border-zinc-700 dark:bg-zinc-900">
            <option value="filter">AI filter</option><option value="boost">AI boost</option><option value="none">AI none</option>
          </select>
          <select value={category.defaultValueId ?? ""} onChange={event => { if (event.target.value) void updateCategory({ defaultValueId: event.target.value }); }} className="rounded border border-zinc-300 bg-white px-2 py-1 text-xs dark:border-zinc-700 dark:bg-zinc-900">
            <option value="">Varsayılan yok</option>{items.filter(item => item.isActive).map(item => <option key={item.id} value={item.id}>{item.label}</option>)}
          </select>
          <label className="flex items-center gap-1 text-xs text-zinc-500"><input type="checkbox" checked={category.isRequired} onChange={event => void updateCategory({ isRequired: event.target.checked })}/> Zorunlu</label>
          <button onClick={() => void updateCategory({ isActive: !category.isActive })} className="rounded border border-zinc-300 px-2 py-1 text-xs dark:border-zinc-700">{category.isActive ? "Pasifleştir" : "Aktifleştir"}</button>
          <button onClick={() => void deleteCategory()} className="rounded px-2 py-1 text-xs text-red-600">Kategoriyi sil</button>
        </>}
      </div>
      <div className="border border-zinc-200 dark:border-zinc-800 rounded-xl">
        {items.length === 0 ? (
          <p className="p-4 text-sm text-zinc-500">Tanımlı değer yok</p>
        ) : (
          items.map((item) => {
            const colorClasses = getColorClasses(item.color);
            const IconComp = getIconComponent(item.icon);
            return (
              <div
                key={item.id}
                className={`border-b last:border-b-0 border-zinc-100 dark:border-zinc-800 ${
                  !item.isActive ? "opacity-50" : ""
                }`}
              >
                <div className="flex items-center justify-between px-4 py-3">
                  <div className="flex items-center gap-3">
                    <span className={`inline-flex items-center justify-center w-8 h-8 rounded-lg ${colorClasses.bg}`}>
                      <IconComp size={16} className={colorClasses.text} />
                    </span>
                    <div>
                      <span className="text-sm font-medium text-zinc-900 dark:text-zinc-100">{item.label}</span>
                      <span className="text-xs text-zinc-400 ml-2">({item.value})</span>
                      {item.category === "content_type" && <span className="text-xs text-zinc-400 ml-2">Authority: {item.authorityWeight}</span>}
                      {!item.isActive && <span className="ml-2 text-xs text-amber-600 dark:text-amber-400">Etkin değil</span>}
                    </div>
                  </div>
                  <div className="flex items-center gap-2">
                    <button
                      onClick={() => handleEdit(item)}
                      className="p-1.5 rounded-lg hover:bg-zinc-100 dark:hover:bg-zinc-800 text-zinc-500"
                      title="Renk ve simgeyi düzenle"
                    >
                      <Palette size={16} />
                    </button>
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
                {editingId === item.id && (
                  <div className="px-4 pb-3 pt-1 border-t border-zinc-100 dark:border-zinc-800 bg-zinc-50 dark:bg-zinc-900/50">
                    <div className="flex flex-wrap gap-3 items-center">
                      <div>
                        <label className="text-xs font-medium text-zinc-500 block mb-1">Color</label>
                        <ColorPicker value={editColor} onChange={setEditColor} />
                      </div>
                      <div>
                        <label className="text-xs font-medium text-zinc-500 block mb-1">Icon</label>
                        <IconPicker value={editIcon} onChange={setEditIcon} />
                      </div>
                      {item.category === "content_type" && <div>
                        <label className="text-xs font-medium text-zinc-500 block mb-1">Authority (0-100)</label>
                        <input type="number" min={0} max={100} value={editAuthorityWeight} onChange={(e) => setEditAuthorityWeight(Number(e.target.value))} className="w-24 px-3 py-1.5 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800" />
                      </div>}
                      <div className="flex gap-2 ml-auto">
                        <button
                          onClick={() => handleSaveEdit(item)}
                          className="px-3 py-1.5 text-xs bg-blue-600 hover:bg-blue-700 text-white rounded-lg"
                        >
                          Save
                        </button>
                        <button
                          onClick={() => setEditingId(null)}
                          className="px-3 py-1.5 text-xs bg-zinc-200 dark:bg-zinc-700 hover:bg-zinc-300 dark:hover:bg-zinc-600 text-zinc-700 dark:text-zinc-300 rounded-lg"
                        >
                          İptal
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
