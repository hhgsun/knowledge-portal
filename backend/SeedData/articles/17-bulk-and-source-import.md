---
{
  "title": "Toplu Aktarım ve Kaynak Dosyadan Makale Oluşturma",
  "contentType": "how-to",
  "tags": [
    "project-knowledge-portal",
    "tutorial",
    "api"
  ],
  "excerpt": "Markdown, JSONL, CSV ve ofis dosyalarından tutarlı, doğrulanmış makale içe aktarma ve dışa aktarma akışları.",
  "status": "published"
}
---

## İki Farklı İçe Aktarma Akışı

Knowledge Portal iki tamamlayıcı aktarım yolu sunar:

- **Toplu Aktarım** (`/settings/bulk-transfer`): Yapılandırılmış Markdown, Markdown ZIP, JSONL veya CSV kayıtlarını en çok 5.000 satırlık işler hâlinde doğrular ve içe aktarır. Aynı ekran görünür makaleleri JSONL, CSV veya Markdown ZIP olarak dışa aktarır.
- **Kaynak İçe Aktarma** (`/articles/import`): TXT, Markdown, CSV/TSV, JSON/YAML, PDF, DOCX, XLSX ve PPTX dosyalarını düzenlenebilir Markdown önizlemelerine dönüştürür. Kullanıcı başlık, özet, durum, içerik türü ve etiketleri kontrol ettikten sonra makaleleri oluşturur; isterse özgün dosyayı ek olarak korur.

Her iki akış da normal makale endpoint'iyle aynı `ArticleMutationService` kurallarını kullanır. Böylece başlık uzunluğu, yaşam döngüsü durumu, arşivleme yetkisi, aktif içerik türü, etiket oluşturma yetkisi, sürüm kaydı ve indeksleme davranışı giriş kanalına göre değişmez.

## Toplu Aktarım

Admin ve editor kullanıcıları **Ayarlar → Toplu Aktarım** ekranını kullanabilir. API endpoint'leri:

```text
GET  /api/bulk/templates/{format}  # md, jsonl veya csv şablonu
GET  /api/bulk/import-schema       # limitler ve aktif içerik türleri
POST /api/bulk/import              # multipart file, dryRun, conflictPolicy
GET  /api/bulk/export              # jsonl, csv veya Markdown ZIP
```

`conflictPolicy` seçenekleri:

- `skip`: Eşleşen kayıt değiştirilmez.
- `update`: Eşleşen ve kullanıcının düzenleyebildiği makale güncellenir.
- `duplicate`: Yeni makale oluşturulur; çakışan externalId yeni kayda taşınmaz.

Önce `dryRun=true` ile doğrulama yapılması önerilir. Eşleştirme, varsa en çok 200 karakterlik `externalId`; yoksa başlıktan türetilen slug üzerinden yapılır. `contentMarkdown` her formatta kanonik CommonMark/GFM string'idir. Toplu dışa aktarıma dosya ekleri dahil edilmez.

## Kaynak Dosyadan İçe Aktarma

Kaynak akışı iki aşamalıdır:

1. `POST /api/source-imports/analyze` dosyaları ayrıştırır ve Markdown taslakları döndürür. Ayrıştırılamayan veya kullanılabilir metin içermeyen dosya makale gövdesi yapılmaz; özgün ek olarak tutulması önerilir.
2. `POST /api/source-imports/commit` onaylanan manifesti ve özgün dosyaları gönderir. Her taslak kendi transaction'ında oluşturulur; bir satırın başarısız olması diğer başarılı taslakları geri almaz.

PDF sayfaları, çalışma kitabı sheet'leri ve sunu kaynakları Markdown'da başlık/provenance sınırlarıyla korunur. Özgün dosya ek olarak tutulduğunda normal attachment boyut ve uzantı kuralları uygulanır.

Birden fazla kaynak seçildiğinde ayrıştırılamayan dosya diğer taslakların analizini durdurmaz. İnceleme ekranı sorunlu her dosyanın adını ve neden dönüştürülemediğini toplu olarak gösterir, ilk sorunlu dosyayı seçer ve taslak listesinde uyarıyla işaretler. Commit aşamasındaki kısmi hatalar da dosya adı ve hata nedeni ile gösterilir; daha önce başarıyla oluşturulan makaleler geri alınmaz ve yalnızca başarısız taslaklar düzeltme/yeniden deneme için ekranda kalır.

## İçerik Türü ve Yetki Kuralları

İçerik türü verilmezse aktif `reference` değeri kullanılır. `reference` pasif veya eksikse istek varsayılanı sessizce yazmaz; doğrulama hatası verir. Açıkça gönderilen tüm değerler de yalnızca aktif `lookup_values(category = "content_type")` kayıtlarından seçilebilir.

Tüm roller makale oluşturabilir ve yayınlayabilir. `archived` durumu yalnızca admin/editor için geçerlidir. Makale silme aktarım akışının parçası değildir ve yalnızca admin JWT oturumuna açıktır.

## İndeksleme ve Atomiklik

Başarılı create/update önce sürüm ve makale verisini kaydeder, sonra dayanıklı `index_jobs` kaydını oluşturur ve PostgreSQL FTS'yi best-effort günceller. Semantic embedding arka planda tamamlanır. İçe aktarma transaction'ında FTS hatası savepoint ile yalıtılır; veri commit'i ve dayanıklı iş kaydı korunur.
