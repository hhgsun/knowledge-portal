---
{
  "title": "API Key ile Entegrasyon Rehberi",
  "contentType": "how-to",
  "tags": [
    "project-knowledge-portal",
    "api",
    "security"
  ],
  "excerpt": "API key oluşturma, kullanma, döndürme ve otomasyon senaryolarında Knowledge Portal entegrasyonu.",
  "status": "published"
}
---

## API Key Nedir?

API key'ler, otomasyon, CI/CD pipeline'ları ve harici sistem entegrasyonları için tasarlanmış kimlik doğrulama mekanizmasıdır. İstek sırasında kullanıcı oturumu gerektirmez; yeni key'ler varsayılan olarak 90 gün geçerlidir ve oluşturulurken 1-365 gün arasında bir süre seçilebilir.

Bu makaledeki `{site-url}`, tarayıcıda açtığınız mevcut Knowledge Portal adresini ifade eder (ör. `https://knowledge.example.com`). Değeri protokol (`https://`) dahil ve sonunda `/` olmadan kullanın.

## API Key Oluşturma

Her rol kendi API key'lerini yönetebilir. Yönetim işlemleri API key ile değil, session-based kimlik doğrulama (JWT) ile yapılır.

1. Profil sayfasındaki **API Keys** sekmesine gidin.
2. Yeni key oluştur butonuna tıklayın ve bir isim verin.
3. Oluşturulan key sadece bir kez gösterilir — güvenli bir yere kaydedin.

```bash
# API üzerinden key oluşturma
curl -X POST "{site-url}/api/keys" \
  -H "Authorization: Bearer {jwt_token}" \
  -H "Content-Type: application/json" \
  -d '{"name": "CI/CD Pipeline", "expiresInDays": 90}'

# Yanıt: {"id": "...", "name": "CI/CD Pipeline", "key": "kp_abc123...", "expiresAt": "..."}
```

## API Key Kullanımı

API key'i X-API-Key header'ında gönderin:

```bash
# Makale oluşturma
curl -X POST "{site-url}/api/articles" \
  -H "X-API-Key: kp_your_key_here" \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Otomatik Oluşturulan Makale",
    "contentMarkdown": "## Otomatik Oluşturulan Makale\n\nİçerik **Markdown** formatındadır.",
    "tags": ["api", "automation"],
    "status": "draft"
  }'

# Arama
curl "{site-url}/api/search?q=deployment&type=fulltext" \
  -H "X-API-Key: kp_your_key_here"
```

## Özel Özellikler

### onlyOwnContent Filtresi

API key ile yapılan aramalarda ve makale listelemede onlyOwnContent=true parametresi kullanılabilir. Bu, sadece o API key ile oluşturulmuş makaleleri döndürür — çoklu entegrasyon senaryolarında her sistemin sadece kendi içeriklerini görmesini sağlar.

```bash
# Arama ile
GET /api/search?q=test&onlyOwnContent=true

# Makale listesi ile
GET /api/articles?onlyOwnContent=true
```

### Otomatik Etiket Oluşturma

API key ile makale oluşturulurken bilinmeyen etiketler otomatik olarak oluşturulur. Bu sayede otomasyon script'leri önceden etiket tanımlamak zorunda kalmaz.

### includeContent ve includeAttachments

Hem arama hem de makale listesi endpoint'inde kanonik Markdown string'ini (`contentMarkdown`) ve ek dosya bilgilerini almak için:

```bash
# Arama ile
GET /api/search?q=kurulum&includeContent=true&includeAttachments=true

# Makale listesi ile
GET /api/articles?includeContent=true&includeAttachments=true
```

## Key Döndürme (Rotate)

Güvenlik amacıyla key'leri periyodik olarak döndürmeniz önerilir. Döndürme işlemi eski key'i geçersiz kılar ve yeni bir key üretir:

```bash
curl -X POST "{site-url}/api/keys/{keyId}/rotate" \
  -H "Authorization: Bearer {jwt_token}"

# Yanıt: {"key": "kp_new_key_value..."}
```

## Kısıtlamalar

API key principal'ı, sahibi admin olsa bile en fazla editor yetkisi taşır. `articles:delete_any`, `users:manage`, `api_keys:manage_any` ve `featured_links:manage` gibi admin yetkilerini alamaz. Ayrıca hassas yönetim ve silme işlemleri session-only'dir. Başlıca örnekler:

- `/api/keys` ve `/api/admin/keys` — API key yönetimi
- `/api/admin/users`, `/api/admin/rag-evaluations` ve `/api/logs` — yönetim işlemleri
- `/api/analytics` — analitik verileri
- `/api/search/reindex`, `/api/search/repair-indexing`, `/api/search/diagnostics`, `/api/search/embedding-status`, `/api/search/storage-status`, `/api/search/rag-observability` ve `/api/search/rag-debug` — ayrıcalıklı arama operasyonları
- İçerik veya yapılandırma silen `DELETE` endpoint'leri. Kendi oyunu geri alan `DELETE /api/articles/{id}/vote` bu kuralın istisnasıdır.

## Güvenlik Önerileri

- API key'leri kaynak koda commit etmeyin, environment variable veya secret manager kullanın.
- Her entegrasyon için ayrı key oluşturun.
- Kullanılmayan key'leri silin.
- Key'leri düzenli aralıklarla döndürün (rotate).
