import { useState, useEffect } from "react";
import { X, Plus } from "lucide-react";
import { useApi } from "../../hooks/useApi";

interface Tag {
  id: string;
  name: string;
  slug: string;
}

interface TagSelectorProps {
  selectedTags: string[];
  onChange: (tagIds: string[]) => void;
}

export function TagSelector({ selectedTags, onChange }: TagSelectorProps) {
  const { fetchWithAuth } = useApi();
  const [allTags, setAllTags] = useState<Tag[]>([]);
  const [newTagName, setNewTagName] = useState("");
  const [showInput, setShowInput] = useState(false);

  useEffect(() => {
    fetchWithAuth("/api/tags")
      .then((res) => res.json())
      .then((data) => setAllTags(Array.isArray(data) ? data : []))
      .catch(() => {});
  }, [fetchWithAuth]);

  const handleAdd = (tagId: string) => {
    if (!selectedTags.includes(tagId)) {
      onChange([...selectedTags, tagId]);
    }
  };

  const handleRemove = (tagId: string) => {
    onChange(selectedTags.filter((id) => id !== tagId));
  };

  const handleCreateTag = async () => {
    if (!newTagName.trim()) return;

    const duplicate = allTags.find(
      (t) => t.name.toLowerCase() === newTagName.trim().toLowerCase()
    );
    if (duplicate) {
      handleAdd(duplicate.id);
      setNewTagName("");
      setShowInput(false);
      return;
    }

    const res = await fetchWithAuth("/api/tags", {
      method: "POST",
      body: JSON.stringify({ name: newTagName.trim() }),
    });
    if (res.ok) {
      const tag = await res.json();
      setAllTags((prev) =>
        prev.some((t) => t.id === tag.id) ? prev : [...prev, tag]
      );
      handleAdd(tag.id);
      setNewTagName("");
      setShowInput(false);
    }
  };

  const selectedTagObjects = allTags.filter((t) => selectedTags.includes(t.id));
  const availableTags = allTags.filter((t) => !selectedTags.includes(t.id));

  return (
    <div className="space-y-2">
      {/* Selected Tags */}
      <div className="flex flex-wrap gap-1.5">
        {selectedTagObjects.map((tag) => (
          <span
            key={tag.id}
            className="inline-flex items-center gap-1 px-2 py-0.5 bg-blue-100 dark:bg-blue-900 text-blue-700 dark:text-blue-300 text-xs rounded-full"
          >
            {tag.name}
            <button onClick={() => handleRemove(tag.id)} className="hover:text-red-500">
              <X size={12} />
            </button>
          </span>
        ))}
      </div>

      {/* Add Tag */}
      <div className="flex items-center gap-2 flex-wrap">
        {availableTags.length > 0 && (
          <select
            onChange={(e) => { if (e.target.value) handleAdd(e.target.value); e.target.value = ""; }}
            className="px-2 py-1 text-xs border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800"
            defaultValue=""
          >
            <option value="" disabled>Add tag...</option>
            {availableTags.map((tag) => (
              <option key={tag.id} value={tag.id}>{tag.name}</option>
            ))}
          </select>
        )}

        {showInput ? (
          <div className="flex items-center gap-1">
            <input
              type="text"
              value={newTagName}
              onChange={(e) => setNewTagName(e.target.value)}
              onKeyDown={(e) => e.key === "Enter" && handleCreateTag()}
              placeholder="New tag..."
              className="px-2 py-1 text-xs border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800 w-28"
              autoFocus
            />
            <button onClick={handleCreateTag} className="text-blue-600 hover:text-blue-700 text-xs font-medium">
              Add
            </button>
            <button onClick={() => { setShowInput(false); setNewTagName(""); }} className="text-zinc-400 hover:text-zinc-600 text-xs">
              Cancel
            </button>
          </div>
        ) : (
          <button
            onClick={() => setShowInput(true)}
            className="flex items-center gap-1 text-xs text-zinc-500 hover:text-blue-600"
          >
            <Plus size={12} />
            New tag
          </button>
        )}
      </div>
    </div>
  );
}
