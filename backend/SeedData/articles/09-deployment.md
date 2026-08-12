---
{
  "title": "Deployment ve Yapılandırma Rehberi",
  "contentType": "runbook",
  "tags": [
    "project-knowledge-portal",
    "deployment",
    "monitoring"
  ],
  "excerpt": "Knowledge Portal'ın kurulumu, yapılandırması, Docker ile çalıştırılması ve ortam değişkenleri.",
  "status": "published"
}
---

## Sistem Gereksinimleri

- .NET 10 SDK (backend)
- Node.js 20+ (frontend)
- SQLite (veritabanı — otomatik oluşturulur)
- Ollama (opsiyonel — semantic search ve RAG için)

## Yerel Geliştirme

```bash
# Backend başlatma
cd backend
dotnet run
# → http://localhost:5174
# → Swagger: http://localhost:5174/swagger

# Frontend başlatma (ayrı terminal)
cd frontend
npm install
npm run dev
# → http://localhost:5173
# → API proxy: /api/* → localhost:5174
```

## Docker ile Çalıştırma

```bash
# Development ortamı
docker-compose -f docker-compose.dev.yml up

# Test ortamı
docker-compose -f docker-compose.test.yml up
```

## Yapılandırma (appsettings.json)

### JWT Ayarları

```json
{
  "Jwt": {
    "Key": "your-secret-key-min-32-chars",
    "Issuer": "KnowledgePortal",
    "Audience": "KnowledgePortal",
    "ExpiryHours": 24
  }
}
```

### Rate Limiting

```json
{
  "RateLimiting": {
    "AuthLimit": 10,
    "SearchLimit": 30
  }
}
```

### Ollama (Opsiyonel)

```json
{
  "Ollama": {
    "Enabled": true,
    "BaseUrl": "http://localhost:11434",
    "EmbeddingModel": "nomic-embed-text",
    "ChatModel": "llama3.2",
    "MinSimilarityScore": 0.3
  }
}
```

Ollama devre dışıyken semantic, hybrid ve RAG arama modları kullanılamaz. Fulltext arama her zaman çalışır.

### Dosya Depolama

```json
{
  "FileStorage": {
    "BasePath": "../data/uploads",
    "MaxFileSizeMB": 20,
    "MaxFilesPerArticle": 20
  }
}
```

## Veritabanı

SQLite veritabanı otomatik oluşturulur ve migration'lar startup'ta uygulanır. WAL modu ve busy timeout otomatik olarak etkinleştirilir.

```bash
# Manuel migration uygulama
cd backend && dotnet ef database update

# Yeni migration oluşturma
cd backend && dotnet ef migrations add MigrationName
```

## Sağlık Kontrolü (Health Check)

Sistemin çalıştığını doğrulamak için:

```bash
curl http://localhost:5174/api/health
# Yanıt: {"status": "healthy", ...}
```

## Production Önerileri

- JWT secret key'i güçlü ve uzun tutun (en az 32 karakter).
- SQLite yerine bir production veritabanı düşünün (büyük ölçekte).
- HTTPS zorunlu tutun — reverse proxy (nginx) arkasında çalıştırın.
- Uploads dizinini kalıcı bir disk'e mount edin (Docker volume).
- Rate limit değerlerini trafik paternine göre ayarlayın.
- Frontend build çıktısını nginx veya CDN ile sunun.
