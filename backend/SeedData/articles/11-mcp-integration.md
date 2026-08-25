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

MCP (Model Context Protocol), yapay zekâ istemcilerinin harici bilgi kaynaklarını ve araçları standart bir protokol üzerinden keşfedip çağırmasını sağlayan bir entegrasyon protokolüdür. Knowledge Portal, MCP desteği sunar. Cursor, VS Code Copilot ve özel header gönderebilen diğer MCP istemcileri Knowledge Portal'daki makaleleri ve bilgileri doğrudan sorgulayabilir. Claude remote connector için aşağıdaki kimlik doğrulama sınırlamasına bakın.

MCP araçlarına resmî `ModelContextProtocol.AspNetCore` v2 Streamable HTTP sunucusu üzerinden erişim sağlanır. Endpoint: `POST /mcp`. JSON-RPC ayrıştırma, protokol müzakeresi, discovery ve JSON/SSE yanıt çerçevelemesi SDK tarafından yapılır; portal yalnızca araç kataloğunu ve iş mantığını sağlar.

Bu makaledeki `{site-url}`, tarayıcıda açtığınız mevcut Knowledge Portal adresini ifade eder (ör. `https://knowledge.example.com`). Değeri protokol (`https://`) dahil ve sonunda `/` olmadan kullanın.

Tercih edilen protokol versiyonu `2026-07-28`'dir. Modern istemciler `server/discover` ve istek başına `_meta` zarfını kullanır. Initialize tabanlı eski sürümlerin müzakeresi resmî SDK'nın desteklediği sürümlerle uyumludur.

## Kimlik Doğrulama

MCP endpoint'i, REST API ile aynı kimlik doğrulama mekanizmalarını kullanır. Her istekte kimlik bilgisi gönderilmesi zorunludur. OAuth kullanılmaz.

### API Key ile (Önerilen)

Otomasyon ve AI entegrasyonları için API key kullanımı önerilir. Her rol kendi key'ini profil sayfasındaki **API Keys** sekmesinden oluşturabilir.

```bash
curl -X POST "{site-url}/mcp" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "X-API-Key: kp_your_api_key_here" \
  -H "MCP-Protocol-Version: 2026-07-28" \
  -H "Mcp-Method: tools/call" \
  -H "Mcp-Name: search_articles" \
  -d '{
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/call",
    "params": {
      "name": "search_articles",
      "arguments": {"query": "deployment"},
      "_meta": {
        "io.modelcontextprotocol/protocolVersion": "2026-07-28",
        "io.modelcontextprotocol/clientInfo": {"name": "curl", "version": "1.0.0"},
        "io.modelcontextprotocol/clientCapabilities": {}
      }
    }
  }'
```

### JWT Token ile

```bash
curl -X POST "{site-url}/mcp" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "Authorization: Bearer eyJ..." \
  -H "MCP-Protocol-Version: 2026-07-28" \
  -H "Mcp-Method: tools/call" \
  -H "Mcp-Name: search_articles" \
  -d '{
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/call",
    "params": {
      "name": "search_articles",
      "arguments": {"query": "deployment"},
      "_meta": {
        "io.modelcontextprotocol/protocolVersion": "2026-07-28",
        "io.modelcontextprotocol/clientInfo": {"name": "curl", "version": "1.0.0"},
        "io.modelcontextprotocol/clientCapabilities": {}
      }
    }
  }'
```

## MCP İletişim Akışı

Modern (`2026-07-28`) istemciler:

1. İsteğe bağlı `server/discover` ile desteklenen sürüm ve yetenekleri öğrenir.
2. Her istekte `MCP-Protocol-Version`, `Mcp-Method` ve `tools/call` için `Mcp-Name` header'larını gönderir.
3. `params._meta` içinde protokol sürümünü, istemci bilgisini ve yeteneklerini taşır.
4. `tools/list` ve `tools/call` çağrılarını doğrudan yapar.

Legacy 2025-era istemciler aşağıdaki sırayı kullanır:

1. `initialize` — Sunucu yeteneklerini ve protokol versiyonunu öğrenir
2. `notifications/initialized` — İstemci hazır olduğunu bildirir
3. `tools/list` — Kullanılabilir araçları ve parametrelerini keşfeder
4. `tools/call` — Bir aracı çalıştırır ve sonuç alır

HTTP POST istekleri `Content-Type: application/json` kullanmalı ve `Accept` içinde hem `application/json` hem `text/event-stream` bildirmelidir. Legacy akışta istemci, `initialize` sonrasındaki isteklerde müzakere edilen sürümü `MCP-Protocol-Version` başlığında gönderir. SDK, sürüme göre JSON veya SSE çerçeveli yanıt döndürebilir. Sunucu stateless çalışır; bağımsız server-initiated GET stream sunmadığı için `GET /mcp` çağrısı `405 Method Not Allowed` döner.

## Kullanılabilir Araçlar (Tools)

MCP sunucusu genel veri erişim araçlarına ek olarak görev odaklı araçlar sunar. Tüm araçlar yalnızca yayınlanmış (published) makaleleri döndürür.

Görev odaklı araçlar:

- `get_project_context` — zorunlu tag/content-type kapsamıyla yönetişim bilgili özet (araç adı geriye uyumludur; tag'lere proje anlamı dayatmaz)
- `get_integration_guidance` — entegrasyon hedefi için hybrid kaynak bulma
- `find_authoritative_content` — karar konusu için güvenilirlik sıralamalı kaynaklar
- `compare_sources` — 2-10 kaynağı canonical içerik ve governance ile yan yana karşılaştırma
- `get_recent_changes` — isteğe bağlı tag/content-type kapsamıyla yakın dönem değişiklikleri

### Ortak kapsam modeli

Birden fazla makale getiren arama, liste ve görev araçları aynı `scope` nesnesini kullanır:

```json
{
  "scope": {
    "tags": ["a", "x", "y"],
    "contentTypes": ["how-to", "adr"]
  }
}
```

Etiketler serbest anlamlıdır; `project-`, `team-` veya benzeri bir ön ek zorunlu değildir. `tags` içindeki bütün değerler makalede bulunmalıdır (AND). `contentTypes` içindeki değerlerden herhangi birinin eşleşmesi yeterlidir (OR). İki boyut birlikte verilirse ikisi de uygulanır. `scope` verilmezse genel arama yapılır. Bilinmeyen bir değer kapsamı kaldırmaz veya genişletmez; sonuç kümesi boş kalır. Eski istemciler için düz `tags`, `content_type` ve `project_tag` alanları kabul edilmeye devam eder ve aynı kapsama birleştirilir. `get_project_context` etkili kapsamın boş olmamasını ister; diğer araçlarda kapsam isteğe bağlıdır.

### search_articles

Knowledge Portal'ın REST aramasıyla aynı full-text, semantic, hybrid ve RAG akışlarını kullanır. Yayınlanmış makaleler arasında arama yaparak başlık, özet, yazar, etiket ve istenirse içerik/ek bilgilerini döner. `@yazar`, `#etiket` ve `##içerik-türü` inline filtreleri desteklenir.

Parametreler:

- `query` (string, zorunlu) — Arama metni
- `type` (string) — `fulltext`, `semantic`, `hybrid` veya `rag` (varsayılan `fulltext`)
- `limit` (integer) — Maksimum sonuç sayısı (1-50, varsayılan 20)
- `scope` (object) — Ortak kapsam: `tags[]` (AND) ve `contentTypes[]` (OR)
- `tags` (string) — Geriye uyumlu düz kapsam alanı; virgülle ayrılmış etiket slug'ları
- `authors` (string) — Yazar slug'larına göre filtrele, virgülle ayrılmış (OR mantığı)
- `content_type` (string) — Geriye uyumlu düz kapsam alanı; virgülle ayrılmış içerik türleri
- `include_content` (boolean) — Kanonik Markdown string'ini `contentMarkdown` alanında sonuçlara dahil et (varsayılan false)
- `include_attachments` (boolean) — Ek dosya metadatasını sonuçlara dahil et (varsayılan false)
- `only_own_content` (boolean) — API key ile çağrıldığında yalnızca o anahtarla oluşturulan içerikleri döndürür

### get_article

ID veya slug ile belirli bir makalenin tüm detaylarını getirir. Kanonik içerik `contentMarkdown`, türetilmiş düz metin `contentText`, ekler metadata olarak döner.

Parametreler:

- `id_or_slug` (string, zorunlu) — Makale ID'si veya URL slug'ı

### list_articles

Yayınlanmış makaleleri sayfalayarak listeler.

Parametreler:

- `page` (integer) — Sayfa numarası, 1-tabanlı (varsayılan 1)
- `limit` (integer) — Sayfa başına öğe sayısı (1-50, varsayılan 20)
- `scope` (object) — Ortak kapsam: `tags[]` (AND) ve `contentTypes[]` (OR)
- `content_type` (string) — Geriye uyumlu düz içerik türü kapsamı
- `tags` (string) — Geriye uyumlu düz etiket kapsamı
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

Karar desteği için her sonuçta `governance` bilgisi bulunur. İçerik türleri dinamik olduğundan otorite değeri içerik türü adına göre kodlanmaz; Tanım Değerleri ekranındaki 0-100 `authorityWeight` ayarından alınır. Onay akışından geçen içeriklerde onaylayan ve zaman kaydedilir. Doğrudan yayınlanan veya içe aktarılan içerik gizlenmez ve onaylanmış varsayılmaz; `approvalState: not_recorded` uyarısıyla sunulur. İnceleme süresi, güncellik ve onay bilgisi birlikte bir reliability score üretir; arama yanıtındaki `decisionSupport` alanı dikkat gerektiren sonuçları özetler.

MCP içeriği güvenilmeyen kaynak verisi olarak işler. Sonuçlardaki `securityAssessment`, talimat geçersiz kılma, sistem prompt'u isteme, credential gönderme, komut/araç çalıştırma ve rol değiştirme sinyallerini açıklar. Yaygın API key, bearer token, JWT ve secret biçimleri `[REDACTED_SECRET]` ile maskelenir. `allowAutomaticExecution` her zaman false'dur; makale içindeki URL, komut veya araç talimatları otomatik çalıştırılmamalıdır.

Her araç çağrısı `X-Trace-Id` döndürür ve yapılandırılmış audit kaydı oluşturur. Audit kaydı araç, sonuç, kimlik kaynağı, süre ve çıktı boyutunu içerir; sorgu, içerik veya credential değerlerini kaydetmez. Argümanlar yalnızca alan adı, tür ve uzunluk/adet olarak özetlenir. Operasyon metrikleri `/metrics` üzerinden tool, outcome ve auth source boyutlarıyla yayınlanır.

Dayanıklılık katmanı araç türüne göre timeout, AI işlemleri için eşzamanlılık sınırı ve Ollama circuit breaker uygular. Büyük istekler 413 ile, büyük tool sonuçları `output_too_large` ile reddedilir. Geçici hatalar `retryable` ve `retryAfterSeconds` alanlarını içerir; istemci yalnızca bu alanlara göre kontrollü retry yapmalıdır. İstemci bağlantıyı kapatırsa çalışma iptal edilir ve audit sonucu `cancelled` olur.

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

## Claude Desktop / Claude.ai

Claude remote custom connector bağlantıları Anthropic altyapısından gelir; bu nedenle sunucunun bu altyapıdan HTTPS ile erişilebilir olması ve Claude'un belgelenmiş OAuth bağlantı akışını desteklemesi gerekir. Knowledge Portal MCP endpoint'i yalnızca `X-API-Key` veya statik Bearer token kabul ettiği için Claude remote connector'a doğrudan eklenemez. `claude_desktop_config.json` içindeki remote `url + headers` biçimi de güncel Claude Desktop tarafından desteklenen kurulum yolu değildir.

Claude entegrasyonu gerekiyorsa şirket ağı içinde çalışan, API key'i sunucu tarafında ekleyen güvenilir bir local stdio köprüsü kullanılmalı veya Knowledge Portal önüne standart MCP OAuth katmanı eklenmelidir. API key'i URL query parametresine koymayın.

## VS Code Copilot Yapılandırması

Workspace kök dizininde `.vscode/mcp.json` dosyası oluşturun. Anahtarı dosyaya düz metin yazmak yerine güvenli input değişkeni kullanın:

```json
{
  "inputs": [
    {
      "type": "promptString",
      "id": "knowledge-portal-key",
      "description": "Knowledge Portal API key",
      "password": true
    }
  ],
  "servers": {
    "knowledge-portal": {
      "type": "http",
      "url": "{site-url}/mcp",
      "headers": {
        "X-API-Key": "${input:knowledge-portal-key}"
      }
    }
  }
}
```

## Cursor Yapılandırması

Workspace kökünde `.cursor/mcp.json` oluşturun. `KNOWLEDGE_PORTAL_API_KEY` ortam değişkenini Cursor başlamadan önce tanımlayın:

```json
{
  "mcpServers": {
    "knowledge-portal": {
      "url": "{site-url}/mcp",
      "headers": {
        "X-API-Key": "${env:KNOWLEDGE_PORTAL_API_KEY}"
      }
    }
  }
}
```

## API Key Oluşturma

MCP için API key oluşturmak üzere REST API kullanın (admin veya api_keys:manage iznine sahip kullanıcı gerektirir):

```bash
# Önce JWT token al
curl -X POST "{site-url}/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"email": "kullanici@ornek.com", "password": "sifreniz"}'

# API key oluştur
curl -X POST "{site-url}/api/keys" \
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
            'Accept': 'application/json, text/event-stream',
            'X-API-Key': api_key
        }
        self._id = 0

    def _call(self, method, params=None, name=None):
        self._id += 1
        call_params = dict(params or {})
        call_params['_meta'] = {
            'io.modelcontextprotocol/protocolVersion': '2026-07-28',
            'io.modelcontextprotocol/clientInfo': {'name': 'knowledge-portal-python', 'version': '1.0.0'},
            'io.modelcontextprotocol/clientCapabilities': {}
        }
        payload = {
            'jsonrpc': '2.0',
            'id': self._id,
            'method': method,
            'params': call_params
        }
        headers = dict(self.headers)
        headers['MCP-Protocol-Version'] = '2026-07-28'
        headers['Mcp-Method'] = method
        if name:
            headers['Mcp-Name'] = name
        r = requests.post(f'{self.base_url}/mcp', json=payload, headers=headers)
        r.raise_for_status()
        data = r.json()
        if 'error' in data:
            raise Exception(data['error']['message'])
        return data['result']

    def call_tool(self, tool_name, **arguments):
        result = self._call('tools/call', {'name': tool_name, 'arguments': arguments}, tool_name)
        if result.get('isError'):
            raise Exception(result['content'][0]['text'])
        return result.get('structuredContent') or json.loads(result['content'][0]['text'])

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
mcp = KnowledgePortalMCP('{site-url}', 'kp_your_api_key_here')
results = mcp.search('deployment', limit=5)
for article in results['results']:
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

  private async call(method: string, params: Record<string, any> = {}, name?: string) {
    const requestParams = {
      ...params,
      _meta: {
        'io.modelcontextprotocol/protocolVersion': '2026-07-28',
        'io.modelcontextprotocol/clientInfo': { name: 'knowledge-portal-typescript', version: '1.0.0' },
        'io.modelcontextprotocol/clientCapabilities': {}
      }
    };
    const res = await fetch(`${this.baseUrl}/mcp`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Accept': 'application/json, text/event-stream',
        'X-API-Key': this.apiKey,
        'MCP-Protocol-Version': '2026-07-28',
        'Mcp-Method': method,
        ...(name ? { 'Mcp-Name': name } : {})
      },
      body: JSON.stringify({
        jsonrpc: '2.0',
        id: ++this.id,
        method,
        params: requestParams
      })
    });
    if (!res.ok) throw new Error(`MCP HTTP ${res.status}: ${await res.text()}`);
    const data = await res.json();
    if (data.error) throw new Error(data.error.message);
    return data.result;
  }

  async callTool(toolName: string, args: Record<string, any> = {}) {
    const result = await this.call('tools/call', { name: toolName, arguments: args }, toolName);
    if (result.isError) throw new Error(result.content[0].text);
    return result.structuredContent ?? JSON.parse(result.content[0].text);
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
const mcp = new KnowledgePortalMCP('{site-url}', 'kp_your_api_key_here');
const results = await mcp.search('api deployment', 5);
console.log(results);
```

### PowerShell

```powershell
$BaseUrl = '{site-url}'
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
            _meta = @{
                'io.modelcontextprotocol/protocolVersion' = '2026-07-28'
                'io.modelcontextprotocol/clientInfo' = @{ name = 'knowledge-portal-powershell'; version = '1.0.0' }
                'io.modelcontextprotocol/clientCapabilities' = @{}
            }
        }
    } | ConvertTo-Json -Depth 10

    $response = Invoke-RestMethod -Uri "$BaseUrl/mcp" -Method Post `
        -Headers @{
            'X-API-Key' = $ApiKey
            'Content-Type' = 'application/json'
            'Accept' = 'application/json, text/event-stream'
            'MCP-Protocol-Version' = '2026-07-28'
            'Mcp-Method' = 'tools/call'
            'Mcp-Name' = $ToolName
        } `
        -Body $payload
    if ($response.result.isError) { throw $response.result.content[0].text }
    if ($response.result.structuredContent) { return $response.result.structuredContent }
    return ($response.result.content[0].text | ConvertFrom-Json)
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
- Transport: Resmî `ModelContextProtocol.AspNetCore` v2 ile stateless Streamable HTTP. Modern ve initialize tabanlı çağrılar aynı `POST /mcp` endpoint'ini kullanır; SDK yanıtı sürüme göre JSON veya SSE olarak çerçeveleyebilir, bağımsız server-initiated GET stream yoktur.
