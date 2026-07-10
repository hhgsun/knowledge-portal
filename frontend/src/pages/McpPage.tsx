import { useState, useEffect } from "react";
import { Copy, Check, ExternalLink, Terminal, Cpu, Key, Zap, BookOpen } from "lucide-react";
import { useAuth } from "../contexts/AuthContext";
import { useApi } from "../hooks/useApi";
import { toast } from "sonner";
import type { ApiKey } from "../types/api";

export default function McpPage() {
  const { token } = useAuth();
  const { fetchWithAuth } = useApi();
  const [copied, setCopied] = useState<string | null>(null);
  const [keys, setKeys] = useState<ApiKey[]>([]);
  const [loadingKeys, setLoadingKeys] = useState(true);

  const baseUrl = window.location.origin;
  const mcpEndpoint = `${baseUrl}/mcp`;

  useEffect(() => {
    fetchWithAuth("/api/keys").then(async (res) => {
      if (res.ok) setKeys(await res.json());
      setLoadingKeys(false);
    });
  }, []);

  const copyToClipboard = (text: string, label: string) => {
    navigator.clipboard.writeText(text);
    setCopied(label);
    toast.success(`${label} kopyalandı`);
    setTimeout(() => setCopied(null), 2000);
  };

  const CopyButton = ({ text, label }: { text: string; label: string }) => (
    <button
      onClick={() => copyToClipboard(text, label)}
      className="inline-flex items-center gap-1 px-2 py-1 text-xs rounded bg-zinc-100 dark:bg-zinc-800 hover:bg-zinc-200 dark:hover:bg-zinc-700 transition-colors"
      title={`${label} kopyala`}
    >
      {copied === label ? <Check size={12} className="text-green-600" /> : <Copy size={12} />}
      <span>{copied === label ? "Kopyalandı" : "Kopyala"}</span>
    </button>
  );

  const claudeDesktopConfig = JSON.stringify({
    mcpServers: {
      "knowledge-portal": {
        url: mcpEndpoint,
        headers: {
          "X-API-Key": "kp_YOUR_API_KEY"
        }
      }
    }
  }, null, 2);

  const vscodeConfig = JSON.stringify({
    servers: {
      "knowledge-portal": {
        url: mcpEndpoint,
        headers: {
          "X-API-Key": "kp_YOUR_API_KEY"
        }
      }
    }
  }, null, 2);

  const curlExample = `curl -X POST ${mcpEndpoint} \\
  -H "Content-Type: application/json" \\
  -H "X-API-Key: kp_YOUR_API_KEY" \\
  -d '{
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/list"
  }'`;

  const pythonExample = `import requests, json

mcp_url = "${mcpEndpoint}"
headers = {"Content-Type": "application/json", "X-API-Key": "kp_YOUR_API_KEY"}

def call_tool(name, **args):
    r = requests.post(mcp_url, json={
        "jsonrpc": "2.0", "id": 1,
        "method": "tools/call",
        "params": {"name": name, "arguments": args}
    }, headers=headers)
    return json.loads(r.json()["result"]["content"][0]["text"])

# Arama
results = call_tool("search_articles", query="deployment", limit=5)
print(results)`;

  return (
    <div className="max-w-4xl mx-auto space-y-8">
      {/* Header */}
      <div>
        <h1 className="text-2xl font-bold text-zinc-900 dark:text-zinc-100 flex items-center gap-3">
          <Cpu size={28} />
          MCP Entegrasyonu
        </h1>
        <p className="mt-2 text-zinc-600 dark:text-zinc-400">
          Knowledge Portal'ı AI araçlarınızla (Claude Desktop, Cursor, VS Code Copilot) entegre edin.
        </p>
      </div>

      {/* Quick Info Cards */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        <div className="rounded-xl border border-zinc-200 dark:border-zinc-700 p-4 bg-white dark:bg-zinc-900">
          <div className="flex items-center gap-2 text-sm font-medium text-zinc-700 dark:text-zinc-300 mb-2">
            <Zap size={16} className="text-amber-500" />
            MCP Endpoint
          </div>
          <div className="flex items-center gap-2">
            <code className="text-sm bg-zinc-100 dark:bg-zinc-800 px-2 py-1 rounded flex-1 truncate">
              {mcpEndpoint}
            </code>
            <CopyButton text={mcpEndpoint} label="Endpoint" />
          </div>
        </div>

        <div className="rounded-xl border border-zinc-200 dark:border-zinc-700 p-4 bg-white dark:bg-zinc-900">
          <div className="flex items-center gap-2 text-sm font-medium text-zinc-700 dark:text-zinc-300 mb-2">
            <Key size={16} className="text-blue-500" />
            Bearer Token
          </div>
          <div className="flex items-center gap-2">
            <code className="text-sm bg-zinc-100 dark:bg-zinc-800 px-2 py-1 rounded flex-1 truncate">
              {token ? `${token.slice(0, 20)}...` : "—"}
            </code>
            {token && <CopyButton text={token} label="Bearer Token" />}
          </div>
        </div>

        <div className="rounded-xl border border-zinc-200 dark:border-zinc-700 p-4 bg-white dark:bg-zinc-900">
          <div className="flex items-center gap-2 text-sm font-medium text-zinc-700 dark:text-zinc-300 mb-2">
            <BookOpen size={16} className="text-green-500" />
            Protokol
          </div>
          <div className="text-sm text-zinc-600 dark:text-zinc-400">
            JSON-RPC 2.0 • MCP 2024-11-05
          </div>
        </div>
      </div>

      {/* API Keys */}
      <section className="rounded-xl border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-900 overflow-hidden">
        <div className="px-6 py-4 border-b border-zinc-200 dark:border-zinc-700 flex items-center justify-between">
          <h2 className="text-lg font-semibold text-zinc-900 dark:text-zinc-100 flex items-center gap-2">
            <Key size={18} />
            API Key'leriniz
          </h2>
          <a
            href="/settings/keys"
            className="text-sm text-blue-600 dark:text-blue-400 hover:underline flex items-center gap-1"
          >
            Yönet <ExternalLink size={12} />
          </a>
        </div>
        <div className="px-6 py-4">
          {loadingKeys ? (
            <p className="text-sm text-zinc-500">Yükleniyor...</p>
          ) : keys.length === 0 ? (
            <div className="text-center py-6">
              <p className="text-sm text-zinc-500 mb-3">Henüz API key oluşturmadınız.</p>
              <a
                href="/settings/keys"
                className="inline-flex items-center gap-2 px-4 py-2 bg-blue-600 text-white text-sm rounded-lg hover:bg-blue-700 transition-colors"
              >
                <Key size={14} />
                API Key Oluştur
              </a>
            </div>
          ) : (
            <div className="space-y-2">
              {keys.slice(0, 3).map((k) => (
                <div key={k.id} className="flex items-center justify-between py-2 px-3 rounded-lg bg-zinc-50 dark:bg-zinc-800/50">
                  <div>
                    <span className="text-sm font-medium text-zinc-800 dark:text-zinc-200">{k.name}</span>
                  </div>
                  <span className="text-xs text-zinc-500">
                    {k.expiresAt ? new Date(k.expiresAt).toLocaleDateString("tr-TR") : "Süresiz"}
                  </span>
                </div>
              ))}
              {keys.length > 3 && (
                <p className="text-xs text-zinc-500 text-center">+{keys.length - 3} daha</p>
              )}
            </div>
          )}
        </div>
      </section>

      {/* Quick Setup - Claude Desktop */}
      <section className="rounded-xl border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-900 overflow-hidden">
        <div className="px-6 py-4 border-b border-zinc-200 dark:border-zinc-700">
          <h2 className="text-lg font-semibold text-zinc-900 dark:text-zinc-100">
            🤖 Claude Desktop
          </h2>
          <p className="text-sm text-zinc-500 mt-1">
            <code className="text-xs">claude_desktop_config.json</code> dosyasına ekleyin
          </p>
        </div>
        <div className="relative">
          <pre className="px-6 py-4 text-sm overflow-x-auto bg-zinc-50 dark:bg-zinc-950 text-zinc-800 dark:text-zinc-200">
            <code>{claudeDesktopConfig}</code>
          </pre>
          <div className="absolute top-3 right-3">
            <CopyButton text={claudeDesktopConfig} label="Claude Config" />
          </div>
        </div>
      </section>

      {/* Quick Setup - VS Code / Cursor */}
      <section className="rounded-xl border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-900 overflow-hidden">
        <div className="px-6 py-4 border-b border-zinc-200 dark:border-zinc-700">
          <h2 className="text-lg font-semibold text-zinc-900 dark:text-zinc-100">
            💻 VS Code Copilot / Cursor
          </h2>
          <p className="text-sm text-zinc-500 mt-1">
            Workspace kök dizininde <code className="text-xs">.vscode/mcp.json</code> oluşturun
          </p>
        </div>
        <div className="relative">
          <pre className="px-6 py-4 text-sm overflow-x-auto bg-zinc-50 dark:bg-zinc-950 text-zinc-800 dark:text-zinc-200">
            <code>{vscodeConfig}</code>
          </pre>
          <div className="absolute top-3 right-3">
            <CopyButton text={vscodeConfig} label="VS Code Config" />
          </div>
        </div>
      </section>

      {/* cURL Example */}
      <section className="rounded-xl border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-900 overflow-hidden">
        <div className="px-6 py-4 border-b border-zinc-200 dark:border-zinc-700">
          <h2 className="text-lg font-semibold text-zinc-900 dark:text-zinc-100 flex items-center gap-2">
            <Terminal size={18} />
            cURL ile Test
          </h2>
        </div>
        <div className="relative">
          <pre className="px-6 py-4 text-sm overflow-x-auto bg-zinc-50 dark:bg-zinc-950 text-zinc-800 dark:text-zinc-200">
            <code>{curlExample}</code>
          </pre>
          <div className="absolute top-3 right-3">
            <CopyButton text={curlExample} label="cURL" />
          </div>
        </div>
      </section>

      {/* Python Example */}
      <section className="rounded-xl border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-900 overflow-hidden">
        <div className="px-6 py-4 border-b border-zinc-200 dark:border-zinc-700">
          <h2 className="text-lg font-semibold text-zinc-900 dark:text-zinc-100">
            🐍 Python
          </h2>
        </div>
        <div className="relative">
          <pre className="px-6 py-4 text-sm overflow-x-auto bg-zinc-50 dark:bg-zinc-950 text-zinc-800 dark:text-zinc-200">
            <code>{pythonExample}</code>
          </pre>
          <div className="absolute top-3 right-3">
            <CopyButton text={pythonExample} label="Python" />
          </div>
        </div>
      </section>

      {/* Available Tools Reference */}
      <section className="rounded-xl border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-900 overflow-hidden">
        <div className="px-6 py-4 border-b border-zinc-200 dark:border-zinc-700">
          <h2 className="text-lg font-semibold text-zinc-900 dark:text-zinc-100">
            🛠️ Kullanılabilir Araçlar
          </h2>
        </div>
        <div className="px-6 py-4">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-zinc-200 dark:border-zinc-700">
                  <th className="text-left py-2 pr-4 font-medium text-zinc-700 dark:text-zinc-300">Araç</th>
                  <th className="text-left py-2 pr-4 font-medium text-zinc-700 dark:text-zinc-300">Açıklama</th>
                  <th className="text-left py-2 font-medium text-zinc-700 dark:text-zinc-300">Parametreler</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-100 dark:divide-zinc-800">
                <tr>
                  <td className="py-3 pr-4"><code className="text-xs bg-zinc-100 dark:bg-zinc-800 px-1.5 py-0.5 rounded">search_articles</code></td>
                  <td className="py-3 pr-4 text-zinc-600 dark:text-zinc-400">Full-text arama</td>
                  <td className="py-3 text-xs text-zinc-500">query*, limit, tags, authors, content_type, include_content</td>
                </tr>
                <tr>
                  <td className="py-3 pr-4"><code className="text-xs bg-zinc-100 dark:bg-zinc-800 px-1.5 py-0.5 rounded">get_article</code></td>
                  <td className="py-3 pr-4 text-zinc-600 dark:text-zinc-400">Makale detayı</td>
                  <td className="py-3 text-xs text-zinc-500">id_or_slug*</td>
                </tr>
                <tr>
                  <td className="py-3 pr-4"><code className="text-xs bg-zinc-100 dark:bg-zinc-800 px-1.5 py-0.5 rounded">list_articles</code></td>
                  <td className="py-3 pr-4 text-zinc-600 dark:text-zinc-400">Makale listesi</td>
                  <td className="py-3 text-xs text-zinc-500">page, limit, content_type, tags, sort</td>
                </tr>
                <tr>
                  <td className="py-3 pr-4"><code className="text-xs bg-zinc-100 dark:bg-zinc-800 px-1.5 py-0.5 rounded">list_tags</code></td>
                  <td className="py-3 pr-4 text-zinc-600 dark:text-zinc-400">Etiket listesi</td>
                  <td className="py-3 text-xs text-zinc-500">—</td>
                </tr>
                <tr>
                  <td className="py-3 pr-4"><code className="text-xs bg-zinc-100 dark:bg-zinc-800 px-1.5 py-0.5 rounded">get_portal_info</code></td>
                  <td className="py-3 pr-4 text-zinc-600 dark:text-zinc-400">Portal istatistikleri</td>
                  <td className="py-3 text-xs text-zinc-500">—</td>
                </tr>
              </tbody>
            </table>
          </div>
          <p className="mt-3 text-xs text-zinc-500">* zorunlu parametre</p>
        </div>
      </section>

      {/* Notes */}
      <section className="rounded-xl border border-amber-200 dark:border-amber-800/50 bg-amber-50 dark:bg-amber-950/30 p-6">
        <h3 className="text-sm font-semibold text-amber-800 dark:text-amber-200 mb-2">Önemli Notlar</h3>
        <ul className="text-sm text-amber-700 dark:text-amber-300 space-y-1 list-disc pl-4">
          <li>Tüm araçlar yalnızca <strong>published</strong> makaleleri döndürür.</li>
          <li>OAuth kullanılmaz — sadece API Key veya Bearer Token.</li>
          <li>Her istek stateless'tır, session tutulmaz.</li>
          <li>Bearer token 24 saat geçerlidir. Otomasyon için API Key tercih edin.</li>
          <li>API Key oluşturmak için <a href="/settings/keys" className="underline">API Keys</a> sayfasını kullanın.</li>
        </ul>
      </section>
    </div>
  );
}
