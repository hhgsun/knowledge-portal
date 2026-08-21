---
{
  "title": "Deployment ve Yapılandırma Rehberi",
  "contentType": "runbook",
  "tags": [
    "project-knowledge-portal",
    "deployment",
    "monitoring"
  ],
  "excerpt": "Knowledge Portal'ın PostgreSQL/pgvector tabanlı kurulumu, yapılandırması, Docker akışı ve sağlık kontrolleri.",
  "status": "published"
}
---

## Sistem Gereksinimleri

- .NET 10 SDK
- Node.js 20+
- PostgreSQL ve `pgvector` eklentisi
- Ollama veya uyumlu servis (semantic, hybrid'in semantic bacağı ve RAG için)

## Yerel Geliştirme

Önce `backend/appsettings.json` içindeki `ConnectionStrings:DefaultConnection` değerinin erişilebilir bir PostgreSQL sunucusunu gösterdiğini doğrulayın. Ardından:

```bash
cd backend
dotnet ef database update
dotnet run
# API: http://localhost:5174
# Swagger (Development): http://localhost:5174/swagger

# Ayrı terminal
cd frontend
npm install
npm run dev
# UI: http://localhost:5173
# /api/* proxy: http://localhost:5174
```

Backend ilişkisel sağlayıcıyla açıldığında migration'ları uygular ve seed veriyi yükler. Testlerin varsayılan paketi EF Core InMemory kullandığı için Docker gerektirmez.

## Docker ile Çalıştırma

Repository geliştirme ve test compose dosyaları içerir:

```bash
docker compose -f docker-compose.dev.yml up --build
docker compose -f docker-compose.test.yml up --build
```

Bu compose tanımları backend ve frontend container'larını çalıştırır; PostgreSQL ve Ollama bağlantıları deployment ortamının ağ/yapılandırmasıyla sağlanmalıdır.

## Temel Yapılandırma

### JWT ve Rate Limiting

JWT issuer, audience ve süre ayarları `Jwt` bölümündedir. Rate limit varsayılanları auth için 10/dakika, search için 30/dakika ve MCP için 60/dakikadır. Partition anahtarı API key kimliği, kullanıcı kimliği veya istemci IP'sidir.

```json
{
  "RateLimiting": {
    "AuthLimit": 10,
    "SearchLimit": 30,
    "McpLimit": 60
  }
}
```

### Ollama

```json
{
  "Ollama": {
    "Enabled": true,
    "EmbeddingModel": "bge-m3",
    "ChatModel": "qwen2.5vl:7b",
    "EmbeddingDimensions": 1024,
    "MinSimilarityScore": 0.5,
    "RagMinSimilarityScore": 0.3
  }
}
```

Embedding boyutu veritabanındaki `vector(1024)` kolonuyla eşleşmelidir. Ollama devre dışı veya erişilemez olduğunda fulltext arama çalışmaya devam eder; AI bağımlı yollar kontrollü hata/fallback davranışı uygular.

### İndeksleme Worker'ları

`Indexing` bölümü worker sayısı, claim batch boyutu, polling aralığı, `ReconciliationIntervalSeconds` ile eksik iş uzlaştırma aralığı, lease süresi ve exponential retry sınırlarını yönetir. Kuyruk PostgreSQL `index_jobs` tablosunda dayanıklıdır; uygulama yeniden başlasa da bekleyen işler kaybolmaz. Uzlaştırma, başlangıç anındaki geçici PostgreSQL kesintisinden sonra kuyruk satırı oluşmamış kirli makaleleri de kendiliğinden geri kazanır.

### Dosya Depolama

```json
{
  "FileStorage": {
    "BasePath": "../data/uploads",
    "MaxFileSizeMB": 20,
    "MaxAttachmentsPerArticle": 20
  }
}
```

Uploads ve log dizinleri kalıcı diskte tutulmalıdır. Dosya yazımı aynı volume üzerinde geçici dosya, flush, SHA-256 ve atomik rename akışıyla yapılır; silmeler kurtarılabilir `.trash` alanına taşınır.

## Veritabanı

PostgreSQL bağlantısının migration oluşturma/uygulama ve `vector` extension kullanma yetkisi olmalıdır.

```bash
cd backend
dotnet ef database update
dotnet ef migrations add MigrationName
```

Gerçek PostgreSQL/pgvector davranışını doğrulayan fidelity testleri için `RAG_FIDELITY_CONNECTION_STRING` tanımlanır ve `backend/Tests.Postgres` projesi çalıştırılır.

## Sağlık ve Gözlemlenebilirlik

- `GET /api/health/live`: süreç liveness kontrolü, her zaman 200.
- `GET /api/health`: PostgreSQL readiness ve timeout/cached Ollama kontrolü. DB yoksa 503 `unhealthy`; yalnız Ollama sorunu varsa 200 `degraded`.
- `GET /metrics`: Prometheus metrikleri; nginx üzerinden public olarak yayınlanmaz.
- Yönetici endpoint'leri: `/api/search/diagnostics`, `/api/search/embedding-status`, `/api/search/storage-status`, `/api/search/rag-observability`.

RAG Prometheus alarm kuralları `ops/prometheus/rag-alerts.yml`, Grafana dashboard'u `ops/grafana/rag-overview.json` altındadır.

## Production Kontrol Listesi

- TLS'yi şirket reverse proxy'sinde sonlandırın; `KnownProxies`/`KnownNetworks` ayarlarını doğru yapılandırın.
- PostgreSQL yedekleme ve `pgvector` extension sürüm yönetimini işletim planına dahil edin.
- Upload ve log dizinlerini kalıcı diske bağlayın; kapasite ve bütünlük durumunu izleyin.
- Rate limit, indeks worker ve RAG bütçelerini gerçek trafik ölçümlerine göre ayarlayın.
- `/metrics` endpoint'ini yalnız iç ağdan erişilebilir tutun.
- Frontend build çıktısını nginx veya kurumun statik içerik katmanından sunun.
