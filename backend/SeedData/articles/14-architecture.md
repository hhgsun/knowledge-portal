---
{
  "title": "Mimari ve Teknoloji Yığını",
  "contentType": "reference",
  "tags": [
    "project-knowledge-portal",
    "best-practices",
    "api"
  ],
  "excerpt": "Knowledge Portal'ın güncel frontend, backend, PostgreSQL, arama/RAG ve işletim mimarisi.",
  "status": "published"
}
---

## Genel Mimari

Knowledge Portal split monorepo olarak düzenlenmiştir: React tabanlı SPA frontend, ASP.NET Core Web API backend ve PostgreSQL/pgvector veri katmanı. Geliştirme ortamında Vite, `/api/*` isteklerini `VITE_API_BASE_URL` ile yapılandırılan backend adresine yönlendirir; yayın ortamında frontend ve API reverse proxy üzerinden aynı site adresinde sunulur.

```text
Kullanıcı / entegrasyon
        │
        ├── React SPA ── REST /api/* ──┐
        └── MCP istemcisi ── /mcp ─────┤
                                       ▼
                           ASP.NET Core Web API
                              │             │
                              ▼             ▼
                     PostgreSQL/pgvector   Ollama
                     veri + FTS + kuyruk   embedding + chat
```

Desteklenen production topolojisi tek backend instance'ıdır. TLS şirket reverse proxy'sinde sonlanır; backend gerçek scheme/IP bilgisini Forwarded Headers üzerinden alır.

## Backend

- **.NET 10 / ASP.NET Core:** Controller tabanlı REST API ve stateless JSON-RPC MCP endpoint'i.
- **Entity Framework Core + Npgsql:** PostgreSQL migration, veri erişimi ve snake_case kolon eşlemeleri.
- **PostgreSQL FTS:** Türkçe `tsvector`/`tsquery`, GIN indeks ve sıralamalı lexical arama.
- **pgvector:** 1024 boyutlu embedding'ler, cosine distance ve HNSW indeks.
- **Ollama:** `bge-m3` embedding ve `qwen2.5vl:7b` chat modeli.
- **Kimlik doğrulama:** JWT Bearer, `kp_` prefix'li API key ve Azure AD giriş akışı.
- **RBAC:** Permission sabitleri, sahiplik kontrolleri, API key yetki tavanı ve session-only destructive endpoint'ler.
- **Arka plan işleri:** PostgreSQL-backed dayanıklı indeks kuyruğu ve RAG kalite değerlendirme worker'ı.
- **Gözlemlenebilirlik:** Serilog, OpenTelemetry trace/metric, Prometheus `/metrics`, RAG dashboard ve alarm kuralları.

Controller'lar routing, auth scope ve response shaping ile sınırlı tutulur. `ArticleMutationService` normal, toplu ve kaynak içe aktarma yazma kurallarını; `ContentTypeService` aktif içerik türü invariant'ını; `SearchExecutionService` REST ve MCP arama akışını ortaklaştırır. Veriye EF Core `AppDbContext` üzerinden erişilir. Service hataları standart `{ "error": "..." }` biçimine çevrilir.

## Middleware Sırası

İstek hattı şu sırayı izler: Forwarded Headers → production HSTS → güvenlik header'ları → global exception handling → CORS → API Key middleware → Authentication → Usage Tracking → Rate Limiter → Authorization → Controllers.

Rate limiter authentication'dan sonra çalıştığı için isteği API key kimliği, kullanıcı kimliği veya IP adresine göre partition edebilir.

## Frontend

- **React 19 + TypeScript strict:** Bileşen tabanlı SPA.
- **Vite 8:** Geliştirme/build aracı ve Azure popup callback için multi-page entry.
- **React Router 7:** `ProtectedRoute` ve `RoleRoute` ile istemci yönlendirmesi.
- **Tailwind CSS 4:** Utility-first stil sistemi.
- **Milkdown Crepe 7:** ProseMirror tabanlı CommonMark/GFM editörü.
- **MSAL.js 5:** Azure AD redirect-bridge popup ve PKCE akışı.
- **React Context:** Auth ve tema state'i; merkezi store kütüphanesi kullanılmaz.
- **Sonner + lucide-react:** Bildirim ve ikon altyapısı.

Tüm authenticated API çağrıları JWT ekleme ve 401'de logout davranışını merkezileştiren `useApi` hook'u üzerinden yapılır.

## Veri ve İçerik Modeli

Başlıca entity grupları:

- **Kimlik ve yetki:** User, ApiKey.
- **İçerik:** Article, ArticleVersion, Tag, ArticleTag, LookupValue, FeaturedLink.
- **Etkileşim ve analitik:** ArticleVote, ArticleComment, ArticleView, SearchQuery, UsageEvent.
- **Dosya ve arama:** ArticleAttachment, ArticleEmbedding, IndexJob.
- **RAG kalite yönetimi:** RagEvaluationDataset, RagEvaluationRun.

Makale gövdesi kanonik CommonMark/GFM Markdown olarak saklanır. `includeContent` yanıtı bu kanonik string'i `contentMarkdown` olarak döndürür. Arama, embedding, okuma süresi ve detay yanıtındaki `contentText` için ayrıca okunabilir düz metin türetilir; URL ve biçim sözdizimi indekse taşınmaz.

## Arama ve RAG

Lexical arama PostgreSQL FTS, semantic arama pgvector kullanır. Hybrid arama iki aday listesini RRF ile birleştirip yerel reranker uygular. RAG, provenance taşıyan makale/ek chunk'larını getirir; dar soruları tek geçişte, geniş soruları bounded-parallel map-reduce ile yanıtlar. Üretilen claim'ler kanıt ve atıf doğrulamasından geçmeden kullanıcı yanıtına alınmaz.

Ayrıntılı akış için **Arama Motoru — Fulltext, Semantic, Hybrid ve RAG** ile **RAG Mimarisi ve İşleyişi** makalelerine bakın.

## Önemli Tasarım Kararları

- **Markdown kanonik formattır:** Editör görünümü değil Markdown metni kalıcı sözleşmedir.
- **PostgreSQL ortak veri katmanıdır:** Uygulama verisi, FTS, pgvector embedding'leri ve dayanıklı iş kuyrukları aynı ilişkisel sağlayıcıdadır.
- **Şema adları:** Uygulamaya ait PostgreSQL tablo, kolon, indeks ve constraint adları `snake_case` kullanır. EF geçmiş tablosunun adı `__ef_migrations_history`'dir; bu tablodaki sağlayıcıya ait iki kolon EF'in standart adlarını korur. Migration geçmişi temiz başlangıç şemasına sıkıştırılmıştır; eski kurulumlar veritabanını yeniden oluşturur.
- **Opsiyonel AI katmanı:** Ollama sorunu fulltext aramayı veya temel içerik yönetimini durdurmaz.
- **Dayanıklı indeksleme:** Editler makale başına coalesce edilir; lease, bounded parallelism ve exponential retry ile işlenir.
- **Deferred upload:** Frontend, yeni ekleri ve silmeleri makale kaydına kadar erteler.
- **Seed dokümantasyonu:** `backend/SeedData/articles/` ürünle birlikte gelen proje dokümantasyonudur ve uygulama davranışıyla aynı değişiklikte güncellenir.

## Proje Yapısı

```text
know/
├── backend/
│   ├── Controllers/    # REST endpoint'leri
│   ├── Services/       # Domain, arama/RAG ve gözlemlenebilirlik
│   ├── Auth/           # JWT, API key, RBAC
│   ├── Models/         # Entity ve DTO'lar
│   ├── Data/           # DbContext, initializer, slug sorguları
│   ├── Migrations/     # PostgreSQL migration'ları
│   └── SeedData/       # Ürünle gelen proje makaleleri
├── frontend/           # React + Vite SPA
├── specs/              # AGENTS.md'ye bağlı ayrıntılı spesifikasyonlar
├── ops/                # Prometheus ve Grafana RAG varlıkları
└── data/               # Upload ve log gibi runtime dosyaları
```
