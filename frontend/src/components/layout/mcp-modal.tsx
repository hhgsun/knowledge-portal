import { useState, useEffect } from "react";
import { X, Copy, Check, Cpu, Key, Shield, ChevronDown, ChevronUp, ExternalLink } from "lucide-react";
import { useAuth } from "../../contexts/AuthContext";
import { useApi } from "../../hooks/useApi";
import { toast } from "sonner";
import type { ApiKey } from "../../types/api";

interface McpModalProps {
  open: boolean;
  onClose: () => void;
}

type AuthTab = "bearer" | "apikey";
type ClientTab = "vscode" | "copilot" | "claude" | "claude-code" | "cursor" | "windsurf";
type LangTab = "dotnet" | "java" | "python";

interface McpToolInfo {
  name: string;
  description?: string;
  inputSchema?: {
    properties?: Record<string, unknown>;
    required?: string[];
  };
}

interface ToolSummary {
  name: string;
  desc: string;
  params: string;
}

const LANGS: { id: LangTab; label: string }[] = [
  { id: "dotnet", label: ".NET" },
  { id: "java", label: "Java" },
  { id: "python", label: "Python" },
];

// Config dosyalarındaki server kaydının adı (backend: McpConstants.ServerName)
const MCP_SERVER_NAME = "knowledge-portal";

const CLIENTS: { id: ClientTab; label: string; file: string }[] = [
  { id: "vscode", label: "VS Code", file: ".vscode/mcp.json" },
  { id: "copilot", label: "GitHub Copilot", file: "~/.copilot/mcp-config.json" },
  { id: "claude", label: "Claude Desktop", file: "claude_desktop_config.json" },
  { id: "claude-code", label: "Claude Code", file: "Terminal komutu" },
  { id: "cursor", label: "Cursor", file: "~/.cursor/mcp.json" },
  { id: "windsurf", label: "Windsurf", file: "~/.codeium/windsurf/mcp_config.json" },
];

const TOOL_DESCRIPTIONS: Record<string, string> = {
  search_articles: "Doküman arama (fulltext / semantic / hybrid)",
  ask_knowledge: "Portal kaynaklarından kanıtlı AI-RAG yanıtı",
  get_article: "Makale detayı",
  list_articles: "Makale listesi",
  list_tags: "Etiket listesi",
  get_portal_info: "Portal istatistikleri",
  get_project_context: "Proje bağlamı",
  get_integration_guidance: "Entegrasyon rehberi",
  find_authoritative_content: "Yetkili içerik bulma",
  compare_sources: "Kaynak karşılaştırma",
  get_recent_changes: "Son değişiklikler",
};

const DEFAULT_TOOLS: ToolSummary[] = [
  { name: "search_articles", desc: TOOL_DESCRIPTIONS.search_articles, params: "query*, type, page, limit, scope.tags, scope.contentTypes, scope.facets, authors, include_content, include_attachments, only_own_content" },
  { name: "ask_knowledge", desc: TOOL_DESCRIPTIONS.ask_knowledge, params: "question*, scope, authors, only_own_content" },
  { name: "get_article", desc: "Makale detayı", params: "id_or_slug*" },
  { name: "list_articles", desc: "Makale listesi", params: "page, limit, scope.tags, scope.contentTypes, scope.facets, sort" },
  { name: "list_tags", desc: "Etiket listesi", params: "—" },
  { name: "get_portal_info", desc: "Portal istatistikleri", params: "—" },
  { name: "get_project_context", desc: "Proje bağlamı", params: "project_tag*, limit, include_content, include_attachments" },
  { name: "get_integration_guidance", desc: "Entegrasyon rehberi", params: "integration_query*, project_tag, limit, include_attachments" },
  { name: "find_authoritative_content", desc: "Yetkili içerik bulma", params: "query*, project_tag, limit" },
  { name: "compare_sources", desc: "Kaynak karşılaştırma", params: "article_ids*" },
  { name: "get_recent_changes", desc: "Son değişiklikler", params: "project_tag, days, limit" },
];

const summarizeTools = (tools: McpToolInfo[]): ToolSummary[] => tools.map((tool) => {
  const required = new Set(tool.inputSchema?.required ?? []);
  const params = Object.keys(tool.inputSchema?.properties ?? {})
    .map((name) => `${name}${required.has(name) ? "*" : ""}`)
    .join(", ");

  return {
    name: tool.name,
    desc: TOOL_DESCRIPTIONS[tool.name] ?? tool.description ?? "—",
    params: params || "—",
  };
});

type CopyHandler = (text: string, label: string) => void;

function CopyButton({ text, label, copied, onCopy, small = false }: {
  text: string;
  label: string;
  copied: string | null;
  onCopy: CopyHandler;
  small?: boolean;
}) {
  return (
    <button
      onClick={() => onCopy(text, label)}
      className={`inline-flex items-center gap-1 rounded bg-zinc-100 dark:bg-zinc-700 hover:bg-zinc-200 dark:hover:bg-zinc-600 transition-colors shrink-0 ${small ? "px-1.5 py-0.5 text-[11px]" : "px-2 py-1 text-xs"}`}
    >
      {copied === label ? <Check size={11} className="text-green-500" /> : <Copy size={11} />}
      <span>{copied === label ? "Kopyalandı" : "Kopyala"}</span>
    </button>
  );
}

function SectionLabel({ children }: { children: React.ReactNode }) {
  return (
    <span className="text-[11px] font-semibold text-zinc-400 dark:text-zinc-500 uppercase tracking-wide">{children}</span>
  );
}

function CodeBlock({ content, copyKey, copied, onCopy, full }: {
  content: string;
  copyKey: string;
  copied: string | null;
  onCopy: CopyHandler;
  full?: string;
}) {
  return (
    <div className="relative group">
      <pre className="text-xs bg-zinc-50 dark:bg-zinc-950 border border-zinc-200 dark:border-zinc-700 rounded-lg px-3 py-2.5 overflow-x-auto leading-relaxed">
        <code className="text-zinc-800 dark:text-zinc-300">{content}</code>
      </pre>
      <div className="absolute top-2 right-2 opacity-0 group-hover:opacity-100 transition-opacity">
        <CopyButton text={full || content} label={copyKey} copied={copied} onCopy={onCopy} small />
      </div>
    </div>
  );
}

export function McpModal({ open, onClose }: McpModalProps) {
  const { token } = useAuth();
  const { fetchWithAuth } = useApi();
  const [copied, setCopied] = useState<string | null>(null);
  const [authTab, setAuthTab] = useState<AuthTab>("bearer");
  const [clientTab, setClientTab] = useState<ClientTab>("vscode");
  const [langTab, setLangTab] = useState<LangTab>("dotnet");
  const [toolsOpen, setToolsOpen] = useState(false);
  const [keys, setKeys] = useState<ApiKey[] | null>(null);
  const [protocolVersion, setProtocolVersion] = useState<string | null>(null);
  const [tools, setTools] = useState<ToolSummary[]>(DEFAULT_TOOLS);

  useEffect(() => {
    if (!open) return;
    let cancelled = false;

    fetchWithAuth("/api/keys")
      .then(async (res) => {
        if (res.ok && !cancelled) setKeys(await res.json());
      })
      .catch(() => {});

    const callMcp = (id: string, method: string, params?: object) => fetchWithAuth("/mcp", {
      method: "POST",
      noRetry: true,
      body: JSON.stringify({ jsonrpc: "2.0", id, method, ...(params ? { params } : {}) }),
    });

    callMcp("modal-initialize", "initialize", {
      protocolVersion: "2025-11-25",
      capabilities: {},
      clientInfo: { name: "knowledge-portal-web", version: "1.0.0" },
    })
      .then(async (res) => {
        if (res.ok) {
          const info = await res.json();
          if (!cancelled && info?.result?.protocolVersion) setProtocolVersion(info.result.protocolVersion);
        }
      })
      .catch(() => {});

    callMcp("modal-tools", "tools/list")
      .then(async (res) => {
        if (!res.ok) return;
        const response = await res.json();
        if (!cancelled && Array.isArray(response?.result?.tools)) {
          setTools(summarizeTools(response.result.tools));
        }
      })
      .catch(() => {});

    return () => {
      cancelled = true;
    };
  }, [open, fetchWithAuth]);

  if (!open) return null;

  const baseUrl = window.location.origin;
  const mcpEndpoint = `${baseUrl}/mcp`;

  const authHeader =
    authTab === "bearer"
      ? { Authorization: `Bearer ${token || "YOUR_JWT_TOKEN"}` }
      : { "X-API-Key": "kp_YOUR_API_KEY" };

  const authHeaderStr =
    authTab === "bearer"
      ? `Authorization: Bearer ${token ? token.slice(0, 20) + "..." : "YOUR_TOKEN"}`
      : "X-API-Key: kp_YOUR_API_KEY";

  const authHeaderFull =
    authTab === "bearer"
      ? `Authorization: Bearer ${token || "YOUR_JWT_TOKEN"}`
      : "X-API-Key: kp_YOUR_API_KEY";

  // Client-specific config shapes
  const buildConfig = (client: ClientTab): string => {
    const entry = {
      url: mcpEndpoint,
      headers: authHeader,
    };
    switch (client) {
      case "vscode":
        return JSON.stringify(
          { servers: { [MCP_SERVER_NAME]: { type: "http", ...entry } } },
          null, 2,
        );
      case "copilot":
        return JSON.stringify(
          { mcpServers: { [MCP_SERVER_NAME]: { type: "http", ...entry, tools: ["*"] } } },
          null, 2,
        );
      case "claude-code":
        return `claude mcp add --transport http ${MCP_SERVER_NAME} ${mcpEndpoint} \\
  --header "${authHeaderFull}"`;
      case "windsurf":
        return JSON.stringify(
          { mcpServers: { [MCP_SERVER_NAME]: { serverUrl: mcpEndpoint, headers: authHeader } } },
          null, 2,
        );
      default:
        return JSON.stringify({ mcpServers: { [MCP_SERVER_NAME]: entry } }, null, 2);
    }
  };

  const configJson = buildConfig(clientTab);

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
answer  = call_tool("ask_knowledge", question="Deployment adımları nelerdir?")
article = call_tool("get_article", id_or_slug="mcp-entegrasyonu")
tags    = call_tool("list_tags")`;

  const dotnetSnippet = `using System.Net.Http.Json;
using System.Text.Json;

var mcpUrl = "${mcpEndpoint}";
var http = new HttpClient();
http.DefaultRequestHeaders.Add(${authTab === "bearer"
      ? `"Authorization", "Bearer ${token || "YOUR_JWT_TOKEN"}"`
      : `"X-API-Key", "kp_YOUR_API_KEY"`});

async Task<JsonElement> CallToolAsync(string name, object args)
{
    var res = await http.PostAsJsonAsync(mcpUrl, new
    {
        jsonrpc = "2.0", id = 1,
        method = "tools/call",
        @params = new { name, arguments = args }
    });
    var json = await res.Content.ReadFromJsonAsync<JsonElement>();
    var text = json.GetProperty("result").GetProperty("content")[0]
                   .GetProperty("text").GetString()!;
    return JsonSerializer.Deserialize<JsonElement>(text);
}

// Örnekler
var results = await CallToolAsync("search_articles", new { query = "deployment", limit = 5 });
var answer  = await CallToolAsync("ask_knowledge", new { question = "Deployment adımları nelerdir?" });
var article = await CallToolAsync("get_article", new { id_or_slug = "mcp-entegrasyonu" });
var tags    = await CallToolAsync("list_tags", new { });`;

  const javaSnippet = `import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;

HttpClient client = HttpClient.newHttpClient();
String mcpUrl = "${mcpEndpoint}";

String callTool(String name, String argsJson) throws Exception {
    String body = """
        {"jsonrpc":"2.0","id":1,"method":"tools/call",
         "params":{"name":"%s","arguments":%s}}""".formatted(name, argsJson);
    HttpRequest request = HttpRequest.newBuilder()
        .uri(URI.create(mcpUrl))
        .header("Content-Type", "application/json")
        .header(${authTab === "bearer"
      ? `"Authorization", "Bearer ${token || "YOUR_JWT_TOKEN"}"`
      : `"X-API-Key", "kp_YOUR_API_KEY"`})
        .POST(HttpRequest.BodyPublishers.ofString(body))
        .build();
    HttpResponse<String> res = client.send(request, HttpResponse.BodyHandlers.ofString());
    // result.content[0].text alanını Jackson/Gson ile parse edin
    return res.body();
}

// Örnekler
String results = callTool("search_articles", "{\\"query\\":\\"deployment\\",\\"limit\\":5}");
String answer  = callTool("ask_knowledge", "{\\"question\\":\\"Deployment adımları nelerdir?\\"}");
String article = callTool("get_article", "{\\"id_or_slug\\":\\"mcp-entegrasyonu\\"}");
String tags    = callTool("list_tags", "{}");`;

  const langSnippets: Record<LangTab, string> = {
    dotnet: dotnetSnippet,
    java: javaSnippet,
    python: pythonSnippet,
  };

  const copyToClipboard = (text: string, label: string) => {
    navigator.clipboard.writeText(text);
    setCopied(label);
    toast.success(`${label} kopyalandı`);
    setTimeout(() => setCopied(null), 2000);
  };

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
            {protocolVersion && (
              <span className="text-xs px-2 py-0.5 rounded-full bg-zinc-100 dark:bg-zinc-800 text-zinc-500">{protocolVersion}</span>
            )}
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
              <CopyButton text={mcpEndpoint} label="Endpoint" copied={copied} onCopy={copyToClipboard} />
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
              {authTab === "bearer" && token && <CopyButton text={`Bearer ${token}`} label="Bearer Value" copied={copied} onCopy={copyToClipboard} />}
              {authTab === "apikey" && (
                <a
                  href="/profile?tab=api-keys"
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
            {authTab === "apikey" && (
              <div className="mt-2">
                {keys === null ? (
                  <p className="text-xs text-zinc-400">API key'ler yükleniyor...</p>
                ) : keys.length > 0 ? (
                  <div className="space-y-1">
                    {keys.slice(0, 3).map((k) => (
                      <div key={k.id} className="flex items-center justify-between px-3 py-1.5 rounded-lg bg-zinc-50 dark:bg-zinc-800/50 text-xs">
                        <span className="font-medium text-zinc-700 dark:text-zinc-300">{k.name}</span>
                        <span className="text-zinc-400">
                          {k.expiresAt ? new Date(k.expiresAt).toLocaleDateString("tr-TR") : "Süresiz"}
                        </span>
                      </div>
                    ))}
                    {keys.length > 3 && (
                      <p className="text-[11px] text-zinc-400 text-center">+{keys.length - 3} daha</p>
                    )}
                    <a
                      href="/profile?tab=api-keys"
                      onClick={onClose}
                      className="inline-flex items-center gap-1 text-xs text-blue-600 dark:text-blue-400 hover:underline"
                    >
                      Yönet <ExternalLink size={11} />
                    </a>
                  </div>
                ) : (
                  <p className="text-xs text-zinc-400">Henüz API key oluşturmadınız.</p>
                )}
              </div>
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
              {clientTab === "claude-code" ? (
                <>Terminalde çalıştırın:</>
              ) : (
                <>Dosya: <code className="bg-zinc-100 dark:bg-zinc-800 px-1 rounded">{currentClient.file}</code></>
              )}
            </p>
            <div className="mt-2">
              <CodeBlock content={configJson} copyKey="Config JSON" copied={copied} onCopy={copyToClipboard} />
            </div>
          </div>

          {/* cURL */}
          <div>
            <SectionLabel>cURL Testi</SectionLabel>
            <div className="mt-1.5">
              <CodeBlock content={curlCmd} copyKey="cURL" copied={copied} onCopy={copyToClipboard} full={curlFull} />
            </div>
          </div>

          {/* Kod Örnekleri */}
          <div>
            <SectionLabel>Kod Örnekleri</SectionLabel>
            <div className="mt-1.5 flex gap-1 flex-wrap">
              {LANGS.map((l) => (
                <button
                  key={l.id}
                  onClick={() => setLangTab(l.id)}
                  className={`flex items-center gap-1.5 px-3 py-1.5 text-xs rounded-lg border transition-colors ${langTab === l.id
                      ? "border-blue-500 bg-blue-50 dark:bg-blue-950/40 text-blue-700 dark:text-blue-300 font-medium"
                      : "border-zinc-200 dark:border-zinc-700 text-zinc-500 hover:border-zinc-400 dark:hover:border-zinc-500"
                    }`}
                >
                  {l.label}
                </button>
              ))}
            </div>
            <div className="mt-2">
              <CodeBlock content={langSnippets[langTab]} copyKey={LANGS.find((l) => l.id === langTab)!.label} copied={copied} onCopy={copyToClipboard} />
            </div>
          </div>

          {/* Tools Reference (collapsible) */}
          <div>
            <button
              onClick={() => setToolsOpen((v) => !v)}
              className="flex items-center gap-2 w-full group"
            >
              <SectionLabel>Araçlar ({tools.length} araç)</SectionLabel>
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
                    {tools.map((t) => (
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

          {/* Notes */}
          <div className="rounded-lg border border-amber-200 dark:border-amber-800/50 bg-amber-50 dark:bg-amber-950/30 px-4 py-3">
            <h3 className="text-xs font-semibold text-amber-800 dark:text-amber-200 mb-1.5">Önemli Notlar</h3>
            <ul className="text-xs text-amber-700 dark:text-amber-300 space-y-1 list-disc pl-4">
              <li>Tüm araçlar yalnızca <strong>published</strong> makaleleri döndürür.</li>
              <li>OAuth kullanılmaz — sadece API Key veya Bearer Token.</li>
              <li>Her istek stateless'tır, session tutulmaz.</li>
              <li>Bearer token 24 saat geçerlidir. Otomasyon için API Key tercih edin.</li>
            </ul>
          </div>

          {/* Footer */}
          <p className="text-xs text-zinc-400 dark:text-zinc-500 pt-2 border-t border-zinc-100 dark:border-zinc-800">
            JSON-RPC 2.0 • {CLIENTS.map((c) => c.label).join(", ")} uyumlu
          </p>
        </div>
      </div>
    </div>
  );
}
