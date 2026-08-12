---
{
  "title": "Mimari ve Teknoloji Yığını",
  "contentType": "reference",
  "tags": [
    "project-knowledge-portal",
    "best-practices",
    "api"
  ],
  "excerpt": "Knowledge Portal'ın genel mimarisi, backend ve frontend teknoloji yığını, veri modeli ve tasarım kararları.",
  "status": "published"
}
---

## Genel Mimari

Knowledge Portal, klasik bir istemci-sunucu mimarisi kullanır: React tabanlı SPA frontend, ASP.NET Core Web API backend ve SQLite veritabanı. Geliştirme ortamında frontend, /api/* isteklerini Vite proxy üzerinden backend'e yönlendirir.

## Backend Teknolojileri

- **.NET 10 / ASP.NET Core:** REST API, controller tabanlı mimari.
- **Entity Framework Core + SQLite:** Migration'lar startup'ta otomatik uygulanır. WAL modu ve busy timeout etkindir.
- **SQLite FTS5:** BM25 sıralamalı fulltext arama için sanal tablo.
- **Ollama (opsiyonel):** nomic-embed-text ile embedding üretimi (semantic arama) ve llama3.2 ile RAG yanıtları.
- **JWT + API Key:** Çift kimlik doğrulama mekanizması. RBAC ile yetki kontrolü.
- **Background service:** Arama ve embedding indekslemesi arka planda 5 saniyelik periyotlarla batch olarak çalışır.

## Frontend Teknolojileri

- **React 19 + TypeScript:** Bileşen tabanlı SPA.
- **Vite 8:** Geliştirme sunucusu ve build aracı. auth-popup-callback.html multi-page entry olarak build edilir.
- **Tailwind CSS 4:** Utility-first stil altyapısı.
- **Milkdown 3:** ProseMirror tabanlı zengin metin editörü. Tablolar, görev listeleri, kod blokları (lowlight ile sözdizimi vurgulama), görsel yapıştırma desteklenir.
- **React Router 7:** İstemci tarafı yönlendirme.
- **MSAL.js 5:** Azure AD kimlik doğrulaması (popup + PKCE akışı).
- **Lucide Icons + Sonner:** İkon seti ve toast bildirimleri.

## Veri Modeli

Temel varlıklar ve ilişkileri:

- **User:** Kullanıcılar — rol (admin/editor/viewer), yerel şifre ve/veya Azure Object ID.
- **Article:** Makaleler — Milkdown tarafından düzenlenen Markdown içerik, durum, içerik türü, slug, okuma süresi.
- **ArticleVersion:** İçerik değişikliklerinde otomatik oluşturulan versiyonlar.
- **Tag / ArticleTag:** Çoktan-çoğa etiket ilişkisi.
- **Attachment:** Makale ekleri — fiziksel dosyalar data/uploads/{articleId}/ altında tutulur.
- **ArticleVote / Comment:** Geri bildirim kayıtları.
- **ArticleView / SearchLog:** Analitik için görüntülenme ve arama kayıtları.
- **ApiKey:** kp_ prefix'li anahtarlar — yalnızca hash'i saklanır.
- **FeaturedLink:** Kenar çubuğu öne çıkan bağlantıları.
- **LookupValue:** İçerik türleri gibi yönetilebilir sabit listeler (etiket, renk, ikon, sıralama).

## Önemli Tasarım Kararları

- **İçerik formatı olarak Markdown:** Milkdown editörünün ürettiği CommonMark/GFM metni kanonik olarak saklanır. Arama indeksi için biçim işaretleri ayıklanarak düz metin türetilir.
- **SQLite tercihi:** Sıfır kurulum maliyeti ve FTS5 desteği. Yüksek eşzamanlılık gereken büyük kurulumlarda PostgreSQL'e geçiş önerilir.
- **Opsiyonel AI katmanı:** Ollama kapalıyken sistem tamamen çalışır durumda kalır — semantic/hybrid/RAG aramalar devre dışı kalır, fulltext her zaman çalışır.
- **Deferred upload:** Dosya ekleri makale kaydedilene kadar sunucuya gönderilmez; silmeler de kaydetme anında uygulanır. Bu, yarım kalmış düzenlemelerin artık dosya bırakmasını önler.
- **Seed data:** İlk açılışta backend/SeedData/articles altındaki Markdown dosyaları otomatik yüklenir. Portal, kendi dokümantasyonunu içeren makalelerle hazır gelir.

## Proje Yapısı

```text
know/
├── backend/            # ASP.NET Core Web API
│   ├── Controllers/    # REST endpoint'leri
│   ├── Services/       # İş mantığı (arama, embedding, istatistik...)
│   ├── Auth/           # JWT, API key, RBAC
│   ├── Models/         # Entity'ler ve DTO'lar
│   ├── Data/           # DbContext, migration, seed
│   └── SeedData/       # Başlangıç makaleleri (Markdown)
├── frontend/           # React + Vite SPA
│   └── src/
│       ├── pages/      # Sayfa bileşenleri
│       ├── components/ # Paylaşılan bileşenler
│       └── lib/        # API istemcisi, yardımcılar
└── data/               # SQLite veritabanı ve uploads (runtime)
```
