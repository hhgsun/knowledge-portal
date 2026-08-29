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

Her iki akış da normal makale endpoint'iyle aynı `ArticleMutationService` kurallarını kullanır. Böylece başlık uzunluğu, yaşam döngüsü durumu, arşivleme yetkisi, aktif içerik türü, makale mutasyonu sırasında yeni etiketi atomik oluşturup bağlama davranışı, sürüm kaydı ve indeksleme giriş kanalına göre değişmez. Bu bağlamsal etiket oluşturma yetkisi bağımsız Etiket Yönetimi işlemlerine erişim vermez.

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

`/articles/import` ekranında kaynak dosyalar dosya seçiciyle veya sürükleyip bırakılarak topluca eklenebilir. Yeni seçilen ya da bırakılan dosyalar mevcut listeye eklenir; desteklenmeyen uzantılar analiz kuyruğuna alınmadan kullanıcıya bildirilir.

Kaynak akışı iki aşamalıdır:

1. `POST /api/source-imports/analyze` dosyaları ayrıştırır ve Markdown taslakları döndürür. Desteklenmeyen ancak geçerli bir dosya veya kullanılabilir metin içermeyen kaynak `warning` ile ek olarak korunabilir; bozuk ya da okunamayan kaynak ise `analysisError` ile başarısız olarak işaretlenir.
2. `POST /api/source-imports/commit` onaylanan manifesti ve özgün dosyaları gönderir. Her taslak kendi transaction'ında oluşturulur; bir satırın başarısız olması diğer başarılı taslakları geri almaz.

PDF sayfaları, çalışma kitabı sheet'leri ve sunu kaynakları Markdown'da başlık/provenance sınırlarıyla korunur. Özgün dosya ek olarak tutulduğunda normal attachment boyut ve uzantı kuralları uygulanır.

Birden fazla kaynak seçildiğinde her dosya bağımsız analiz edilir; bir isteğin veya ayrıştırmanın başarısız olması diğer taslakları gizlemez. İnceleme ekranı sorunlu her dosyanın adını ve nedenini toplu hata alanında, kırmızı taslak satırında ve seçili makalenin hata panelinde gösterir. Başarısız analizlerde de Markdown editörü ve özgün dosyayı attachment olarak saklama seçeneği gösterilir. Her taslakta özgün dosya için **Orijinal dosyayı indekse dahil et** seçeneği, ayrıca o makaleye özel **Additional attachments** alanındaki her dosyada **İndekse dahil et** seçeneği bulunur. Başarıyla Markdown gövdesine dönüştürülen özgün dosyada seçenek tekrar üretmemesi için başlangıçta kapalı, dönüştürülemeyen özgün dosya ve ek destek dosyalarında açık gelir; kullanıcı inceleme sırasında her birini değiştirebilir. Seçim `includeInIndex` olarak saklanır. Özgün dosya ve ek attachment'lar birlikte yapılandırılmış dosya boyutu, uzantı ve makale başına adet sınırına tabidir; herhangi biri başarısız olursa ilgili makale ve o makale için yazılan dosyalar birlikte geri alınır.

`analysisError` taşıyan taslakta içerik boş olduğu sürece içe aktarma düğmesi kapalıdır; kullanıcı içeriği manuel girdiğinde taslak hazır duruma geçer ve özgün dosya seçiliyse makaleye eklenir. Alternatif olarak hatalı makale kaldırıldığında, başka çözümlenmemiş analiz hatası yoksa düğme hemen etkinleşir. Uyarı seviyesindeki ve ek olarak korunabilen kaynaklar içe aktarmayı engellemez. Commit aşamasındaki kısmi hatalar da dosya adı ve hata nedeni ile gösterilir; daha önce başarıyla oluşturulan makaleler geri alınmaz ve yalnızca başarısız taslaklar düzeltme/yeniden deneme için ekranda kalır.

## İçerik Türü ve Yetki Kuralları

İçerik türü verilmezse aktif `content_type` kategorisinin yönetici tarafından belirlenen varsayılanı kullanılır; `reference` yalnız başlangıç seed varsayılanıdır. Açıkça gönderilen değerler kategori aktifken yalnızca aktif `lookup_values(category = "content_type")` kayıtlarından seçilebilir. Kategori devre dışı bırakılır veya kaldırılırsa legacy alan geriye uyumluluk için korunur ve başlangıçta yeniden seed edilmez.

Generic sınıflandırmalar JSONL ve Markdown front matter içinde `classifications` nesnesiyle taşınır. CSV'de aynı nesne JSON metni olarak `classifications` sütununda bulunur. Export kategori/değer atamalarını korur; export ekranı aktif kategorileri `sortOrder` sırasıyla dinamik filtre olarak gösterir ve repeatable `facet=category:value` kullanır. Import canonical değer, aktiflik, tekli/çoklu ve zorunlu/varsayılan kurallarını normal makale yazma akışıyla aynı biçimde doğrular.

Tüm roller makale oluşturabilir ve yayınlayabilir. `archived` durumu yalnızca admin/editor için geçerlidir. Makale silme aktarım akışının parçası değildir ve yalnızca admin JWT oturumuna açıktır.

## İndeksleme ve Atomiklik

Başarılı create/update önce sürüm ve makale verisini kaydeder, sonra dayanıklı `index_jobs` kaydını oluşturur ve PostgreSQL FTS'yi best-effort günceller. Semantic embedding arka planda tamamlanır. İçe aktarma transaction'ında FTS hatası savepoint ile yalıtılır; veri commit'i ve dayanıklı iş kaydı korunur.
