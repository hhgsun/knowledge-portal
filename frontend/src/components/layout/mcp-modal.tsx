import { useState } from "react";
import { X, Copy, Check, Cpu, Key, Shield, ChevronDown, ChevronUp, ExternalLink } from "lucide-react";
import { useAuth } from "../../contexts/AuthContext";
import { toast } from "sonner";

interface McpModalProps {
  open: boolean;
  onClose: () => void;
}

type AuthTab = "bearer" | "apikey";
type ClientTab = "claude" | "vscode" | "cursor";

const CLIENTS: { id: ClientTab; label: string; file: string }[] = [
  { id: "claude", label: "Claude Desktop", file: "claude_desktop_config.json" },
  { id: "vscode", label: "VS Code", file: ".vscode/mcp.json" },
  { id: "cursor", label: "Cursor", file: "~/.cursor/mcp.json" },
];

const TOOLS = [
  { name: "search_articles", desc: "Full-text arama", params: "query*, limit, tags, authors, content_type, include_content" },
  { name: "get_article", desc: "Makale detayı", params: "id_or_slug*" },
  { name: "list_articles", desc: "Makale listesi", params: "page, limit, content_type, tags, sort" },
  { name: "list_tags", desc: "Etiket listesi", params: "—" },
  { name: "get_portal_info", desc: "İstatistikler", params: "—" },
];

export function McpModal({ open, onClose }: McpModalProps) {
  const { token } = useAuth();
  const [copied, setCopied] = useState<string | null>(null);
  const [authTab, setAuthTab] = useState<AuthTab>("bearer");
  const [clientTab, setClientTab] = useState<ClientTab>("claude");
  const [toolsOpen, setToolsOpen] = useState(false);

  if (!open) return null;

  const baseUrl = window.location.origin;
  const mcpEndpoint = `${baseUrl}/mcp`;

  const authHeader =
    authTab === "bearer"
      ? { Authorization: `Bearer ${token || "YOUR_JWT_TOKEN"}` }
      : { "X-API-Key": "kp_YOUR_API_KEY" };

  // Client-specific config shapes
  const buildConfig = (client: ClientTab) => {
    const entry = {
      url: mcpEndpoint,
      headers: authHeader,
    };
    if (client === "vscode") return { servers: { "knowledge-portal": entry } };
    return { mcpServers: { "knowledge-portal": entry } };
  };

  const configJson = JSON.stringify(buildConfig(clientTab), null, 2);

  const authHeaderStr =
    authTab === "bearer"
      ? `Authorization: Bearer ${token ? token.slice(0, 20) + "..." : "YOUR_TOKEN"}`
      : "X-API-Key: kp_YOUR_API_KEY";

  const authHeaderFull =
    authTab === "bearer"
      ? `Authorization: Bearer ${token || "YOUR_JWT_TOKEN"}`
      : "X-API-Key: kp_YOUR_API_KEY";

  const curlCmd = `curl -X POST ${mcpEndpoint} \\
  -H "Content-Type: application/json" \\
  -H "${authHeaderStr}" \\
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'`;

  const curlFull = `curl -X POST ${mcpEndpoint} -H "Content-Type: application/json" -H "${authHeaderFull}" -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'`;

  const pythonSnippet = `import requests, json

mcp_url = "${mcpEndpoint}"
headers = {
    "Content-Type": "application/json",
    ${authTab === "bearer"
      ? `"Authorization": "Bearer ${token || "YOUR_JWT_TOKEN"}"`
      : `"X-API-Key": "kp_YOUR_API_KEY"`}
}

def call_tool(name, **args):
    r = requests.post(mcp_url, json={
        "jsonrpc": "2.0", "id": 1,
        "method": "tools/call",
        "params": {"name": name, "arguments": args}
    }, headers=headers)
    return json.loads(r.json()["result"]["content"][0]["text"])

# Örnekler
results = call_tool("search_articles", query="deployment", limit=5)
article = call_tool("get_article", id_or_slug="mcp-entegrasyonu")
tags    = call_tool("list_tags")`;

  const copyToClipboard = (text: string, label: string) => {
    navigator.clipboard.writeText(text);
    setCopied(label);
    toast.success(`${label} kopyalandı`);
    setTimeout(() => setCopied(null), 2000);
  };

  const CopyBtn = ({ text, label, small = false }: { text: string; label: string; small?: boolean }) => (
    <button
      onClick={() => copyToClipboard(text, label)}
      className={`inline-flex items-center gap-1 rounded bg-zinc-100 dark:bg-zinc-700 hover:bg-zinc-200 dark:hover:bg-zinc-600 transition-colors shrink-0 ${small ? "px-1.5 py-0.5 text-[11px]" : "px-2 py-1 text-xs"}`}
    >
      {copied === label ? <Check size={11} className="text-green-500" /> : <Copy size={11} />}
      <span>{copied === label ? "Kopyalandı" : "Kopyala"}</span>
    </button>
  );

  const SectionLabel = ({ children }: { children: React.ReactNode }) => (
    <span className="text-[11px] font-semibold text-zinc-400 dark:text-zinc-500 uppercase tracking-wide">{children}</span>
  );

  const CodeBlock = ({ content, copyKey, full }: { content: string; copyKey: string; full?: string }) => (
    <div className="relative group">
      <pre className="text-xs bg-zinc-50 dark:bg-zinc-950 border border-zinc-200 dark:border-zinc-700 rounded-lg px-3 py-2.5 overflow-x-auto leading-relaxed">
        <code className="text-zinc-800 dark:text-zinc-300">{content}</code>
      </pre>
      <div className="absolute top-2 right-2 opacity-0 group-hover:opacity-100 transition-opacity">
        <CopyBtn text={full || content} label={copyKey} small />
      </div>
    </div>
  );

  const currentClient = CLIENTS.find((c) => c.id === clientTab)!;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-black/50" onClick={onClose} />
      <div className="relative bg-white dark:bg-zinc-900 rounded-xl shadow-2xl border border-zinc-200 dark:border-zinc-700 w-full max-w-2xl max-h-[90vh] flex flex-col">

        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 border-b border-zinc-200 dark:border-zinc-700 shrink-0">
          <div className="flex items-center gap-2">
            <Cpu size={20} className="text-blue-500" />
            <h2 className="text-lg font-semibold text-zinc-900 dark:text-zinc-100">MCP Bağlantı Bilgileri</h2>
            <span className="text-xs px-2 py-0.5 rounded-full bg-zinc-100 dark:bg-zinc-800 text-zinc-500">2024-11-05</span>
          </div>
          <button onClick={onClose} className="p-1 rounded hover:bg-zinc-100 dark:hover:bg-zinc-800 transition-colors">
            <X size={18} className="text-zinc-500" />
          </button>
        </div>

        {/* Scrollable body */}
        <div className="overflow-y-auto flex-1 px-5 py-4 space-y-5">

          {/* Endpoint */}
          <div>
            <SectionLabel>Endpoint</SectionLabel>
            <div className="mt-1.5 flex items-center gap-2">
              <code className="flex-1 text-sm bg-zinc-100 dark:bg-zinc-800 px-3 py-2 rounded-lg truncate font-mono">
                {mcpEndpoint}
              </code>
              <CopyBtn text={mcpEndpoint} label="Endpoint" />
            </div>
          </div>

          {/* Auth Tabs */}
          <div>
            <SectionLabel>Kimlik Doğrulama</SectionLabel>
            <div className="mt-1.5 flex rounded-lg bg-zinc-100 dark:bg-zinc-800 p-1">
              <button
                onClick={() => setAuthTab("bearer")}
                className={`flex-1 flex items-center justify-center gap-1.5 px-3 py-2 text-sm rounded-md transition-colors ${authTab === "bearer"
                    ? "bg-white dark:bg-zinc-700 text-zinc-900 dark:text-zinc-100 shadow-sm font-medium"
                    : "text-zinc-500 hover:text-zinc-700 dark:hover:text-zinc-300"
                  }`}
              >
                <Shield size={14} />
                Bearer Token
                <span className="text-[10px] text-zinc-400">(24 saat)</span>
              </button>
              <button
                onClick={() => setAuthTab("apikey")}
                className={`flex-1 flex items-center justify-center gap-1.5 px-3 py-2 text-sm rounded-md transition-colors ${authTab === "apikey"
                    ? "bg-white dark:bg-zinc-700 text-zinc-900 dark:text-zinc-100 shadow-sm font-medium"
                    : "text-zinc-500 hover:text-zinc-700 dark:hover:text-zinc-300"
                  }`}
              >
                <Key size={14} />
                API Key
                <span className="text-[10px] text-zinc-400">(Önerilen)</span>
              </button>
            </div>

            {/* Auth Value */}
            <div className="mt-2 flex items-center gap-2">
              <code className="flex-1 text-xs bg-zinc-100 dark:bg-zinc-800 px-3 py-2 rounded-lg truncate font-mono text-zinc-700 dark:text-zinc-300">
                {authTab === "bearer"
                  ? (token ? `Bearer ${token.slice(0, 28)}...` : "Oturum açılmamış")
                  : "kp_YOUR_API_KEY"}
              </code>
              {authTab === "bearer" && token && <CopyBtn text={`Bearer ${token}`} label="Bearer Value" />}
              {authTab === "apikey" && (
                <a
                  href="/settings/keys"
                  onClick={onClose}
                  className="inline-flex items-center gap-1 px-2 py-1 text-xs rounded bg-blue-100 dark:bg-blue-900/40 text-blue-700 dark:text-blue-300 hover:bg-blue-200 dark:hover:bg-blue-900/60 transition-colors shrink-0"
                >
                  <ExternalLink size={11} />
                  Oluştur
                </a>
              )}
            </div>
            {authTab === "bearer" && (
              <p className="mt-1 text-xs text-zinc-400">Token 24 saat geçerlidir. Uzun süreli otomasyon için API Key kullanın.</p>
            )}
          </div>

          {/* Client Tabs + Config */}
          <div>
            <SectionLabel>Yapılandırma</SectionLabel>
            <div className="mt-1.5 flex gap-1 flex-wrap">
              {CLIENTS.map((c) => (
                <button
                  key={c.id}
                  onClick={() => setClientTab(c.id)}
                  className={`flex items-center gap-1.5 px-3 py-1.5 text-xs rounded-lg border transition-colors ${clientTab === c.id
                      ? "border-blue-500 bg-blue-50 dark:bg-blue-950/40 text-blue-700 dark:text-blue-300 font-medium"
                      : "border-zinc-200 dark:border-zinc-700 text-zinc-500 hover:border-zinc-400 dark:hover:border-zinc-500"
                    }`}
                >
                  {c.label}
                </button>
              ))}
            </div>
            <p className="mt-1.5 text-xs text-zinc-500">
              Dosya: <code className="bg-zinc-100 dark:bg-zinc-800 px-1 rounded">{currentClient.file}</code>
            </p>
            <div className="mt-2">
              <CodeBlock content={configJson} copyKey="Config JSON" />
            </div>
          </div>

          {/* cURL */}
          <div>
            <SectionLabel>cURL Testi</SectionLabel>
            <div className="mt-1.5">
              <CodeBlock content={curlCmd} copyKey="cURL" full={curlFull} />
            </div>
          </div>

          {/* Python */}
          <div>
            <SectionLabel>Python</SectionLabel>
            <div className="mt-1.5">
              <CodeBlock content={pythonSnippet} copyKey="Python" />
            </div>
          </div>

          {/* Tools Reference (collapsible) */}
          <div>
            <button
              onClick={() => setToolsOpen((v) => !v)}
              className="flex items-center gap-2 w-full group"
            >
              <SectionLabel>Araçlar (5 tool)</SectionLabel>
              {toolsOpen ? (
                <ChevronUp size={14} className="text-zinc-400 ml-auto" />
              ) : (
                <ChevronDown size={14} className="text-zinc-400 ml-auto" />
              )}
            </button>
            {toolsOpen && (
              <div className="mt-2 rounded-lg border border-zinc-200 dark:border-zinc-700 overflow-hidden">
                <table className="w-full text-xs">
                  <thead className="bg-zinc-50 dark:bg-zinc-800">
                    <tr>
                      <th className="text-left px-3 py-2 font-medium text-zinc-600 dark:text-zinc-400">Araç</th>
                      <th className="text-left px-3 py-2 font-medium text-zinc-600 dark:text-zinc-400">Açıklama</th>
                      <th className="text-left px-3 py-2 font-medium text-zinc-600 dark:text-zinc-400 hidden sm:table-cell">Parametreler</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-zinc-100 dark:divide-zinc-800">
                    {TOOLS.map((t) => (
                      <tr key={t.name}>
                        <td className="px-3 py-2">
                          <code className="bg-zinc-100 dark:bg-zinc-800 px-1.5 py-0.5 rounded text-[11px]">{t.name}</code>
                        </td>
                        <td className="px-3 py-2 text-zinc-600 dark:text-zinc-400">{t.desc}</td>
                        <td className="px-3 py-2 text-zinc-400 dark:text-zinc-500 hidden sm:table-cell">{t.params}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>

          {/* Footer */}
          <p className="text-xs text-zinc-400 dark:text-zinc-500 pt-2 border-t border-zinc-100 dark:border-zinc-800">
            JSON-RPC 2.0 • Claude Desktop, VS Code Copilot, Cursor uyumlu • OAuth yok • Stateless
          </p>
        </div>
      </div>
    </div>
  );
}
