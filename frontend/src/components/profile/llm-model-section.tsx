import { useCallback, useEffect, useState } from "react";
import { Bot, Save } from "lucide-react";
import { toast } from "sonner";
import { useApi } from "../../hooks/useApi";
import { useAuth } from "../../contexts/AuthContext";
import type { LlmModelSettings } from "../../types/api";

export function LlmModelSection() {
  const { fetchWithAuth } = useApi();
  const { refreshUser } = useAuth();
  const [settings, setSettings] = useState<LlmModelSettings>();
  const [value, setValue] = useState("");
  const [saving, setSaving] = useState(false);

  const load = useCallback(async () => {
    const response = await fetchWithAuth("/api/llm-models");
    if (!response.ok) return;
    const data = await response.json() as LlmModelSettings;
    setSettings(data);
    setValue(data.preferredModel ?? "");
  }, [fetchWithAuth]);

  useEffect(() => { void load(); }, [load]);

  async function save() {
    setSaving(true);
    try {
      const response = await fetchWithAuth("/api/auth/profile", {
        method: "PUT",
        body: JSON.stringify(value
          ? { preferredLlmModel: value }
          : { clearPreferredLlmModel: true }),
      });
      const data = await response.json();
      if (!response.ok) return toast.error(data.error || "Model tercihi kaydedilemedi");
      await Promise.all([load(), refreshUser()]);
      toast.success("LLM model tercihi kaydedildi");
    } finally {
      setSaving(false);
    }
  }

  if (!settings) return <p className="text-sm text-zinc-500">Modeller yükleniyor...</p>;

  return (
    <section>
      <div className="flex items-center gap-2 mb-2">
        <Bot size={20} className="text-blue-600" />
        <h2 className="text-lg font-semibold text-zinc-900 dark:text-zinc-100">AI Modeli</h2>
      </div>
      <p className="text-sm text-zinc-500 mb-5">
        Bilgi Asistanı için kullanmak istediğiniz modeli seçin. Modeller Ollama sunucusundan otomatik alınır; “Sistem varsayılanı” seçilirse admin tercihi uygulanır.
      </p>
      {settings.catalogWarning && <p className="mb-4 rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-800 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-200">{settings.catalogWarning}</p>}
      <label className="block text-sm font-medium text-zinc-700 dark:text-zinc-300 mb-1">LLM modeli</label>
      <select value={value} onChange={event => setValue(event.target.value)}
        className="w-full px-3 py-2 text-sm border border-zinc-300 dark:border-zinc-700 rounded-lg bg-white dark:bg-zinc-800 text-zinc-900 dark:text-zinc-100">
        <option value="">Sistem varsayılanı — {settings.defaultModel}</option>
        {settings.models.map(model => <option key={model.id} value={model.id}>{model.label} ({model.id})</option>)}
      </select>
      <p className="mt-2 text-xs text-zinc-500">Etkin model: {value || settings.defaultModel}</p>
      <button type="button" onClick={() => void save()} disabled={saving}
        className="mt-4 inline-flex items-center gap-2 px-4 py-2 text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 rounded-lg disabled:opacity-50">
        <Save size={16} /> {saving ? "Kaydediliyor..." : "Model Tercihini Kaydet"}
      </button>
    </section>
  );
}
