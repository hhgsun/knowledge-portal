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

API key'ler, otomasyon, CI/CD pipeline'ları ve harici sistem entegrasyonları için tasarlanmış kimlik doğrulama mekanizmasıdır. JWT token'ın aksine süresiz geçerlidir ve kullanıcı oturumu gerektirmez.

## API Key Oluşturma

API key yönetimi için admin rolü ve session-based kimlik doğrulama (JWT) gereklidir.

1. Admin panelinden 'API Keys' bölümüne gidin.
2. Yeni key oluştur butonuna tıklayın ve bir isim verin.
3. Oluşturulan key sadece bir kez gösterilir — güvenli bir yere kaydedin.

```bash
# API üzerinden key oluşturma
curl -X POST http://localhost:5174/api/keys \
  -H "Authorization: Bearer {jwt_token}" \
  -H "Content-Type: application/json" \
  -d '{"name": "CI/CD Pipeline"}'

# Yanıt: {"id": "...", "name": "CI/CD Pipeline", "key": "kp_abc123..."}
```

## API Key Kullanımı

API key'i X-API-Key header'ında gönderin:

```bash
# Makale oluşturma
curl -X POST http://localhost:5174/api/articles \
  -H "X-API-Key: kp_your_key_here" \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Otomatik Oluşturulan Makale",
    "content": {"type": "doc", "content": [{"type": "paragraph", "content": [{"type": "text", "text": "İçerik"}]}]},
    "tags": ["api", "automation"],
    "status": "draft"
  }'

# Arama
curl "http://localhost:5174/api/search?q=deployment&type=fulltext" \
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

Hem arama hem de makale listesi endpoint'inde makale içeriğini (plain text olarak) ve ek dosya bilgilerini almak için:

```bash
# Arama ile
GET /api/search?q=kurulum&includeContent=true&includeAttachments=true

# Makale listesi ile
GET /api/articles?includeContent=true&includeAttachments=true
```

## Key Döndürme (Rotate)

Güvenlik amacıyla key'leri periyodik olarak döndürmeniz önerilir. Döndürme işlemi eski key'i geçersiz kılar ve yeni bir key üretir:

```bash
curl -X POST http://localhost:5174/api/keys/{keyId}/rotate \
  -H "Authorization: Bearer {jwt_token}"

# Yanıt: {"key": "kp_new_key_value..."}
```

## Kısıtlamalar

API key ile erişilemeyen (session-only) endpoint'ler:

- /api/admin/users — Kullanıcı yönetimi
- /api/analytics — Analitik verileri
- /api/keys — API key yönetimi
- /api/search/reindex — Arama indeksi yeniden oluşturma
- /api/search/embedding-status — Embedding durumu

## Güvenlik Önerileri

- API key'leri kaynak koda commit etmeyin, environment variable veya secret manager kullanın.
- Her entegrasyon için ayrı key oluşturun.
- Kullanılmayan key'leri silin.
- Key'leri düzenli aralıklarla döndürün (rotate).
