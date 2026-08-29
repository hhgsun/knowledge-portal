---
{
  "title": "API Kullanım Kılavuzu",
  "contentType": "reference",
  "tags": [
    "project-knowledge-portal",
    "api",
    "tutorial"
  ],
  "excerpt": "Knowledge Portal REST API'sinin kullanımı, kimlik doğrulama yöntemleri, endpoint'ler ve örnek istekler.",
  "status": "published"
}
---

## API Genel Bakış

Knowledge Portal, tüm işlemler için RESTful bir API sunar. Tüm endpoint'ler /api/ prefix'i altında bulunur. API, JSON formatında istek ve yanıt kullanır.

Bu makaledeki `{site-url}`, tarayıcıda açtığınız mevcut Knowledge Portal adresini ifade eder (ör. `https://knowledge.example.com`). Değeri protokol (`https://`) dahil ve sonunda `/` olmadan kullanın.

## Kimlik Doğrulama

API'ye erişmek için iki farklı kimlik doğrulama yöntemi desteklenir:

### 1. JWT Token (Session-Based)

Login endpoint'i ile JWT token alın ve sonraki isteklerde Authorization header'ında kullanın:

```bash
# Login
curl -X POST "{site-url}/api/auth/login" \
  -H "Content-Type: application/json" \
  -d '{"email": "kullanici@ornek.com", "password": "sifreniz"}'

# Yanıt: {"token": "eyJ...", "user": {...}}

# Token ile istek
curl "{site-url}/api/articles" \
  -H "Authorization: Bearer eyJ..."
```

### 2. API Key

Otomasyon ve entegrasyon senaryoları için API key kullanılabilir. API key'ler 'kp_' prefix'i ile başlar ve X-API-Key header'ında gönderilir:

```bash
curl "{site-url}/api/articles" \
  -H "X-API-Key: kp_your_api_key_here"
```

Not: API key ile erişimde bazı session-only endpoint'ler (admin users, analytics, API key yönetimi) kullanılamaz.

## Temel Endpoint'ler

### Makaleler

```bash
# Makale listesi
GET /api/articles?page=1&limit=20&status=published

# Makale listesi (içerik ve ekler dahil)
GET /api/articles?includeContent=true&includeAttachments=true

# Sadece kendi API key ile oluşturulan makaleler
GET /api/articles?onlyOwnContent=true

# Makale detayı (ID veya slug ile)
GET /api/articles/{idOrSlug}

# Yeni makale oluştur
POST /api/articles
{
  "title": "Yeni Makale",
  "contentMarkdown": "## Yeni Makale\n\nKanonik Markdown içerik.",
  "excerpt": "Kısa açıklama",
  "status": "draft",
  "contentType": "how-to",
  "tags": ["tutorial", "api"]
}

# Makale güncelle
PUT /api/articles/{id}
{
  "title": "Güncel Başlık",
  "contentMarkdown": "## Güncel Başlık\n\nGüncellenmiş Markdown içerik.",
  "status": "published",
  "changeSummary": "Başlık ve içerik güncellendi"
}

# Makale sil (yalnızca admin JWT oturumu)
DELETE /api/articles/{id}
```

### Arama

```bash
# Fulltext arama
GET /api/search?q=kubernetes deployment&type=fulltext

# Semantic arama (Ollama gerektirir)
GET /api/search?q=container orchestration&type=semantic

# Hybrid arama (fulltext + semantic birleşimi)
GET /api/search?q=docker compose&type=hybrid

# Kaynaklı AI-RAG yanıtı (Search'ten ayrı)
POST /api/assistant
Content-Type: application/json

{"message":"nasıl deploy edilir?"}

# Tag filtresi ile arama
GET /api/search?q=react #tutorial #best-practices

# Yazar filtresi ile arama
GET /api/search?q=deployment @admin

# İçerik türü filtresi
GET /api/search?q=kurulum +content_type:how-to
```

### Etiketler

```bash
# Etiket listesi
GET /api/tags

# Yeni etiket oluştur
POST /api/tags
{"name": "kubernetes"}

# Etiket güncelle
PUT /api/tags
{"id": "abc123", "name": "k8s"}

# Etiket sil (makale bağlantısı yoksa)
DELETE /api/tags?id=abc123
```

## Yanıt Formatları

Başarılı yanıtlar endpoint'e göre değişir:

- **Liste:** { articles: [...], total: 42 }
- **Oluşturma/Güncelleme:** { id: "...", slug: "...", title: "..." }
- **Hata:** { error: "Human-readable message" }

## Rate Limiting

API'nin aşırı kullanımını önlemek için rate limiting uygulanır:

- **Auth endpoint'leri:** Dakikada 10 istek (login, register)
- **Search endpoint'leri:** Dakikada 30 istek
- **Assistant endpoint'leri:** Dakikada 20 istek

Rate limit aşıldığında HTTP 429 (Too Many Requests) yanıtı döner.
