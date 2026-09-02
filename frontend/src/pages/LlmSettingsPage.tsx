import { useCallback, useEffect, useState } from "react";
import { Bot, Save } from "lucide-react";
import { toast } from "sonner";
import { DropdownSelector } from "../components/ui/dropdown-selector";
import { useApi } from "../hooks/useApi";
import type { LlmModelSettings } from "../types/api";

export default function LlmSettingsPage() {
  const { fetchWithAuth } = useApi();
  const [settings, setSettings] = useState<LlmModelSettings>();
  const [model, setModel] = useState("");
  const [saving, setSaving] = useState(false);

  const load = useCallback(async () => {
    const response = await fetchWithAuth("/api/admin/llm-settings");
    if (!response.ok) return;
    const data = await response.json() as LlmModelSettings;
    setSettings(data); setModel(data.defaultModel);
  }, [fetchWithAuth]);
  useEffect(() => { void load(); }, [load]);

  async function save() {
    setSaving(true);
    try {
      const response = await fetchWithAuth("/api/admin/llm-settings", {
        method: "PUT", body: JSON.stringify({ model }), noRetry: true,
      });
      const data = await response.json();
      if (!response.ok) return toast.error(data.error || "Varsayılan model kaydedilemedi");
      setSettings(data); setModel(data.defaultModel);
      toast.success("Varsayılan LLM modeli güncellendi");
    } finally { setSaving(false); }
  }

  return (
    <div className="max-w-2xl mx-auto py-8 px-4">
      <div className="flex items-center gap-3 mb-2"><Bot className="text-blue-600" /><h1 className="text-2xl font-bold">LLM Ayarları</h1></div>
      <p className="text-sm text-zinc-500 mb-8">Modeller Ollama sunucusundan otomatik keşfedilir. Bu model yeni kullanıcı seçimlerinin başlangıç değeri ve sistem işlerinin varsayılanıdır.</p>
      {!settings ? <p className="text-sm text-zinc-500">Modeller yükleniyor...</p> : <div className="rounded-xl border border-zinc-200 dark:border-zinc-800 p-5">
        {settings.catalogWarning && <p className="mb-4 rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-800 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-200">{settings.catalogWarning}</p>}
        <label className="block text-sm font-medium mb-1">Varsayılan model</label>
        <DropdownSelector label="Varsayılan model" options={settings.models.map(item => ({ value: item.id, label: `${item.label} (${item.id})`, searchText: item.id }))} selected={model ? [model] : []} onChange={values => setModel(values[0] ?? "")} searchable={settings.models.length > 10} />
        <p className="mt-2 text-xs text-zinc-500">Değişiklik yeni Assistant isteklerinde hemen uygulanır. Tarayıcıda model seçmiş kullanıcıların yerel tercihleri korunur.</p>
        <button onClick={() => void save()} disabled={saving || model === settings.defaultModel} className="mt-4 inline-flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 rounded-lg disabled:opacity-50">
          <Save size={16} /> {saving ? "Kaydediliyor..." : "Varsayılanı Kaydet"}
        </button>
      </div>}
    </div>
  );
}
