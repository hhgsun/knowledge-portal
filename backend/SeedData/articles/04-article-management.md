---
{
  "title": "Makale Yönetimi — Oluşturma, Düzenleme ve Versiyonlama",
  "contentType": "how-to",
  "tags": [
    "project-knowledge-portal",
    "tutorial",
    "getting-started"
  ],
  "excerpt": "Makale yaşam döngüsü, durum geçişleri, versiyon kontrolü ve içerik yönetimi rehberi.",
  "status": "published"
}
---

## Makale Yaşam Döngüsü

Her makale üç durumdan birinde bulunabilir. Onay, durumdan bağımsız bir güven sinyalidir.

### Durumlar

- **draft (Taslak):** Yeni oluşturulan makalelerin varsayılan durumu. Sadece sahibi görebilir.
- **published (Yayında):** Herkesin görebildiği aktif makaleler. Arama indeksine dahildir.
- **archived (Arşivlenmiş):** Güncelliğini yitirmiş ama silinmemiş makaleler.

### Durum Geçiş Kuralları

- draft → published: Tüm roller kendi makaleleri için yapabilir
- published → archived: articles:archive yetkisi gerekir (admin, editor)
- published → onaylı: Makalenin durumu değişmez; articles:approve yetkisi gerekir (admin, editor)

## Makale Oluşturma

Yeni makale oluştururken aşağıdaki alanlar kullanılabilir:

- **title (zorunlu):** 1-300 karakter. Slug otomatik oluşturulur.
- **contentMarkdown:** Milkdown tarafından düzenlenen CommonMark/GFM Markdown içerik.
- **excerpt:** İsteğe bağlı kısa özet.
- **contentType:** `content_type` kategorisindeki aktif tanım değerlerinden biri. Varsayılan seed değerleri reference, how-to, adr, runbook, faq, policy ve onboarding'dir; pasif veya bilinmeyen değerler REST, bulk ve kaynak içe aktarma akışlarının tümünde reddedilir.
- **classifications:** Kontrollü ve dinamik sınıflandırma nesnesidir; örneğin `{ "department": ["finance"], "system": ["erp"] }`. Kategoriler yönetim ekranından tekli/çoklu, zorunlu/opsiyonel ve varsayılanlı olarak tanımlanır. AI davranışı varsayılan olarak `filter`dır; yalnız metadata amaçlı kategoriler `none` seçilebilir. Yalnız aktif ve canonical değerler kabul edilir. Aynı kategorideki değerler OR, farklı kategoriler AND mantığıyla aranır. `contentType` geriye uyumluluk için korunur ve generic `content_type` atamasıyla otomatik eşlenir.
- **tags:** Etiket dizisi — ID, isim veya slug kabul eder.
- **status:** Başlangıç durumu (yetkilere göre kısıtlı).
- **reviewIntervalDays:** İçerik yönetişiminde kullanılacak gözden geçirme aralığı (1-3650 gün, varsayılan 90). Detay yanıtında okunur ve oluşturma/düzenleme formundan değiştirilebilir.

## Versiyon Kontrolü

Makale içeriği (`contentMarkdown` alanı) her değiştiğinde otomatik olarak yeni bir versiyon oluşturulur. Sadece başlık veya metadata değişikliklerinde versiyon oluşturulmaz.

### Versiyon İşlemleri

- **Listeleme:** GET /api/articles/{id}/versions — Tüm versiyonları kronolojik sırada görüntüleyin.
- **Detay:** GET /api/articles/{id}/versions/{versionId} — Belirli bir versiyonun içeriğini görüntüleyin.
- **Geri Yükleme:** POST /api/articles/{id}/versions/{versionId}/restore — Önceki bir versiyona geri dönün.
- **Diff:** Frontend'de iki versiyon arasındaki farkları satır bazında karşılaştırabilirsiniz.

### Geri Yükleme Davranışı

Bir versiyon geri yüklendiğinde:

1. Versiyonun içeriği ve başlığı makaleye kopyalanır.
2. 'Restored to version N' özeti ile yeni bir versiyon oluşturulur.
3. Okuma süresi yeniden hesaplanır.

## Slug Yönetimi

Makale başlığından otomatik olarak URL-dostu slug üretilir. Türkçe karakterler (ş→s, ç→c, ğ→g, ü→u, ö→o, ı→i) translitere edilir. Başlık değiştiğinde slug yeniden üretilir; ancak aynı slug başka bir makalede varsa mevcut slug korunur.

## Okuma Süresi

Makalenin tahmini okuma süresi, içerik metninden otomatik hesaplanır (~200 kelime/dakika). Oluşturma ve içerik değişikliğinde güncellenir.

## Görüntüleme Takibi

Makale görüntülendiğinde otomatik olarak sayılır. Aynı kullanıcının aynı makaleyi 15 dakika içinde tekrar görüntülemesi tek görüntüleme olarak sayılır (deduplication).
