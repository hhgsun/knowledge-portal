---
{
  "title": "MCP (Model Context Protocol) Entegrasyonu",
  "contentType": "reference",
  "tags": [
    "project-knowledge-portal",
    "api",
    "tutorial"
  ],
  "excerpt": "Knowledge Portal'ın MCP sunucusuna bağlanma, araçların kullanımı ve AI asistanlarla entegrasyon rehberi.",
  "status": "published"
}
---

## Genel Bakış

Knowledge Portal, Model Context Protocol (MCP) desteği sunar. Bu sayede Claude Desktop, Cursor, VS Code Copilot ve diğer MCP uyumlu AI araçları, Knowledge Portal'daki makaleleri ve bilgileri doğrudan sorgulayabilir.

MCP araçlarına REST API üzerinden erişim sağlanır. Endpoint: `POST /mcp`

Varsayılan protokol versiyonu: 2025-11-25. Desteklenen sürümler: 2025-11-25, 2025-06-18, 2025-03-26 ve 2024-11-05 (JSON-RPC 2.0 uyumlu).

## Kimlik Doğrulama

MCP endpoint'i, REST API ile aynı kimlik doğrulama mekanizmalarını kullanır. Her istekte kimlik bilgisi gönderilmesi zorunludur. OAuth kullanılmaz.

### API Key ile (Önerilen)

Otomasyon ve AI entegrasyonları için API key kullanımı önerilir. API key oluşturmak için Admin paneli > API Keys bölümünü kullanın.

```bash
curl -X POST http://localhost:5174/mcp \
  -H "Content-Type: application/json" \
  -H "X-API-Key: kp_your_api_key_here" \
  -d '{
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/call",
    "params": {
      "name": "search_articles",
      "arguments": {"query": "deployment"}
    }
  }'
```

### JWT Token ile

```bash
curl -X POST http://localhost:5174/mcp \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer eyJ..." \
  -d '{
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/call",
    "params": {
      "name": "search_articles",
      "arguments": {"query": "deployment"}
    }
  }'
```

## MCP İletişim Akışı

MCP istemcileri aşağıdaki sırayla sunucu ile iletişim kurar:

1. `initialize` — Sunucu yeteneklerini ve protokol versiyonunu öğrenir
2. `notifications/initialized` — İstemci hazır olduğunu bildirir
3. `tools/list` — Kullanılabilir araçları ve parametrelerini keşfeder
4. `tools/call` — Bir aracı çalıştırır ve sonuç alır

HTTP POST istekleri `Content-Type: application/json` kullanmalıdır. İlk `initialize` çağrısından sonraki isteklerde istemci, müzakere edilen sürümü `MCP-Protocol-Version` başlığında gönderebilir. Sunucu stateless çalışır; SSE ve server-initiated mesaj sunmadığı için `GET /mcp` çağrısı `405 Method Not Allowed` döner.

## Kullanılabilir Araçlar (Tools)

MCP sunucusu 5 araç sunar. Tüm araçlar yalnızca yayınlanmış (published) makaleleri döndürür.

### search_articles

Knowledge Portal'ın REST aramasıyla aynı full-text, semantic, hybrid ve RAG akışlarını kullanır. Yayınlanmış makaleler arasında arama yaparak başlık, özet, yazar, etiket ve istenirse içerik/ek bilgilerini döner. `@yazar`, `#etiket` ve `##içerik-türü` inline filtreleri desteklenir.

Parametreler:

- `query` (string, zorunlu) — Arama metni
- `type` (string) — `fulltext`, `semantic`, `hybrid` veya `rag` (varsayılan `fulltext`)
- `limit` (integer) — Maksimum sonuç sayısı (1-50, varsayılan 20)
- `tags` (string) — Etiket slug'larına göre filtrele, virgülle ayrılmış (AND mantığı)
- `authors` (string) — Yazar slug'larına göre filtrele, virgülle ayrılmış (OR mantığı)
- `content_type` (string) — İçerik türüne göre filtrele, virgülle ayrılmış (OR mantığı)
- `include_content` (boolean) — Makale içeriğini düz metin olarak sonuçlara dahil et (varsayılan false)
- `include_attachments` (boolean) — Ek dosya metadatasını sonuçlara dahil et (varsayılan false)
- `only_own_content` (boolean) — API key ile çağrıldığında yalnızca o anahtarla oluşturulan içerikleri döndürür

### get_article

ID veya slug ile belirli bir makalenin tüm detaylarını getirir. İçerik düz metin olarak, ekler metadata olarak döner.

Parametreler:

- `id_or_slug` (string, zorunlu) — Makale ID'si veya URL slug'ı

### list_articles

Yayınlanmış makaleleri sayfalayarak listeler.

Parametreler:

- `page` (integer) — Sayfa numarası, 1-tabanlı (varsayılan 1)
- `limit` (integer) — Sayfa başına öğe sayısı (1-50, varsayılan 20)
- `content_type` (string) — İçerik türüne göre filtrele
- `tags` (string) — Etiket slug'larına göre filtrele, virgülle ayrılmış
- `sort` (string) — Sıralama: newest, oldest, most_viewed (varsayılan newest)

### list_tags

Knowledge Portal'daki tüm etiketleri listeler. Her etiket için makale sayısını da döner. Parametre gerektirmez.

### get_portal_info

Portal istatistiklerini döner: toplam makale sayısı, yazar sayısı, etiket sayısı, içerik türü dağılımı ve en son yayınlanan 5 makale. Parametre gerektirmez.

## Yanıt Formatı

Tüm araçlar sonuç şemasını `outputSchema` ile ilan eder ve makine tarafından doğrudan okunabilen sonucu `structuredContent` alanında döndürür. Eski istemcilerle uyumluluk için aynı JSON `content` dizisinde de bulunur:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "content": [
      {
        "type": "text",
        "text": "{\"articles\": [...], \"total\": 5, \"query\": \"deployment\"}"
      }
    ]
  }
}
```

Arama sonuçlarında ayrıca `evidenceAvailable` ve `evidence[]` alanları bulunur. Kanıt kaydı makale ID/slug'ını, canonical API URL'ini, kaynak türünü, varsa eşleşen pasajı, güncellenme zamanını, eşleşme türünü ve skoru içerir. Yalnızca başlıkta eşleşme varsa sahte pasaj üretilmez; `evidenceAvailable` false olur.

Hata durumunda:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "content": [
      {
        "type": "text",
        "text": "Article not found or not published"
      }
    ],
    "isError": true
  }
}
```

## Claude Desktop Yapılandırması

Claude Desktop'ın claude_desktop_config.json dosyasına aşağıdaki yapılandırmayı ekleyin:

```json
{
  "mcpServers": {
    "knowledge-portal": {
      "url": "http://localhost:5174/mcp",
      "headers": {
        "X-API-Key": "kp_your_api_key_here"
      }
    }
  }
}
```

## VS Code Copilot / Cursor Yapılandırması

Workspace kök dizininde .vscode/mcp.json dosyası oluşturun:

```json
{
  "servers": {
    "knowledge-portal": {
      "url": "http://localhost:5174/mcp",
      "headers": {
        "X-API-Key": "kp_your_api_key_here"
      }
    }
  }
}
```

## API Key Oluşturma

MCP için API key oluşturmak üzere REST API kullanın (admin veya api_keys:manage iznine sahip kullanıcı gerektirir):

```bash
# Önce JWT token al
curl -X POST http://localhost:5174/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email": "kullanici@ornek.com", "password": "sifreniz"}'

# API key oluştur
curl -X POST http://localhost:5174/api/keys \
  -H "Authorization: Bearer eyJ..." \
  -H "Content-Type: application/json" \
  -d '{"name": "MCP Integration", "expiresInDays": 365}'

# Yanıt: {"id": "...", "key": "kp_...", "name": "MCP Integration", ...}
```

Dönen key değerini (kp_ ile başlayan) MCP yapılandırmanızda X-API-Key header'ı olarak kullanın. Bu değer yalnızca oluşturulduğunda bir kez görüntülenir.

## Programlama Dilleri ile Entegrasyon Örnekleri

### Python

```python
import requests
import json

class KnowledgePortalMCP:
    def __init__(self, base_url, api_key):
        self.base_url = base_url
        self.headers = {
            'Content-Type': 'application/json',
            'X-API-Key': api_key
        }
        self._id = 0

    def _call(self, method, params=None):
        self._id += 1
        payload = {
            'jsonrpc': '2.0',
            'id': self._id,
            'method': method,
            'params': params or {}
        }
        r = requests.post(f'{self.base_url}/mcp', json=payload, headers=self.headers)
        data = r.json()
        if 'error' in data:
            raise Exception(data['error']['message'])
        return data['result']

    def call_tool(self, tool_name, **arguments):
        result = self._call('tools/call', {'name': tool_name, 'arguments': arguments})
        content = result['content'][0]['text']
        return json.loads(content)

    def search(self, query, limit=20, **kwargs):
        return self.call_tool('search_articles', query=query, limit=limit, **kwargs)

    def get_article(self, id_or_slug):
        return self.call_tool('get_article', id_or_slug=id_or_slug)

    def list_articles(self, page=1, limit=20, **kwargs):
        return self.call_tool('list_articles', page=page, limit=limit, **kwargs)

    def list_tags(self):
        return self.call_tool('list_tags')

    def get_info(self):
        return self.call_tool('get_portal_info')

# Kullanım
mcp = KnowledgePortalMCP('http://localhost:5174', 'kp_your_api_key_here')
results = mcp.search('deployment', limit=5)
for article in results['articles']:
    print(f"- {article['title']} ({article['slug']})")
```

### TypeScript / Node.js

```typescript
class KnowledgePortalMCP {
  private id = 0;

  constructor(
    private baseUrl: string,
    private apiKey: string
  ) {}

  private async call(method: string, params?: Record<string, any>) {
    const res = await fetch(`${this.baseUrl}/mcp`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-API-Key': this.apiKey
      },
      body: JSON.stringify({
        jsonrpc: '2.0',
        id: ++this.id,
        method,
        params: params ?? {}
      })
    });
    const data = await res.json();
    if (data.error) throw new Error(data.error.message);
    return data.result;
  }

  async callTool(toolName: string, args: Record<string, any> = {}) {
    const result = await this.call('tools/call', { name: toolName, arguments: args });
    return JSON.parse(result.content[0].text);
  }

  search(query: string, limit = 20) {
    return this.callTool('search_articles', { query, limit });
  }

  getArticle(idOrSlug: string) {
    return this.callTool('get_article', { id_or_slug: idOrSlug });
  }

  listArticles(page = 1, limit = 20) {
    return this.callTool('list_articles', { page, limit });
  }

  listTags() {
    return this.callTool('list_tags');
  }

  getInfo() {
    return this.callTool('get_portal_info');
  }
}

// Kullanım
const mcp = new KnowledgePortalMCP('http://localhost:5174', 'kp_your_api_key_here');
const results = await mcp.search('api deployment', 5);
console.log(results);
```

### PowerShell

```powershell
$BaseUrl = 'http://localhost:5174'
$ApiKey = 'kp_your_api_key_here'

function Invoke-McpTool {
    param(
        [string]$ToolName,
        [hashtable]$Arguments = @{}
    )
    $payload = @{
        jsonrpc = '2.0'
        id = 1
        method = 'tools/call'
        params = @{
            name = $ToolName
            arguments = $Arguments
        }
    } | ConvertTo-Json -Depth 10

    $response = Invoke-RestMethod -Uri "$BaseUrl/mcp" -Method Post `
        -Headers @{ 'X-API-Key' = $ApiKey; 'Content-Type' = 'application/json' } `
        -Body $payload
    return $response.result.content[0].text | ConvertFrom-Json
}

# Arama
Invoke-McpTool -ToolName 'search_articles' -Arguments @{ query = 'deployment'; limit = 5 }

# Makale detayı
Invoke-McpTool -ToolName 'get_article' -Arguments @{ id_or_slug = 'mcp-entegrasyonu' }

# Etiketler
Invoke-McpTool -ToolName 'list_tags'
```

## Önemli Notlar

- MCP araçları yalnızca yayınlanmış (published) makaleleri döndürür.
- Kimlik doğrulama zorunludur: `X-API-Key` header'ı (önerilen) veya `Authorization: Bearer` header'ı.
- OAuth kullanılmaz. Yalnızca API Key veya JWT Bearer token desteklenir.
- Her istek bağımsızdır — stateless çalışır, session tutulmaz.
- Araç yanıtları MCP spec'e uygun `content[]` dizisi formatında döner.
- RBAC uygulanmaz — kimlik doğrulaması yeterlidir, ek izin kontrolü yapılmaz.
- Transport: HTTP POST (Streamable HTTP). SSE stream kullanılmaz.
