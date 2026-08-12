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

Her makale dört durumdan birinde bulunabilir. Durum geçişleri kullanıcı rolüne göre kısıtlanmıştır.

### Durumlar

- **draft (Taslak):** Yeni oluşturulan makalelerin varsayılan durumu. Sadece sahibi görebilir.
- **pending (Onay Bekliyor):** İnceleme için gönderilmiş makaleler. Editor veya admin onaylayabilir.
- **published (Yayında):** Herkesin görebildiği aktif makaleler. Arama indeksine dahildir.
- **archived (Arşivlenmiş):** Güncelliğini yitirmiş ama silinmemiş makaleler.

### Durum Geçiş Kuralları

- draft → pending: Tüm roller yapabilir
- draft/pending → published: articles:publish yetkisi gerekir (admin, editor)
- published → archived: articles:archive yetkisi gerekir (admin, editor)
- pending → published (onay): articles:approve yetkisi gerekir

## Makale Oluşturma

Yeni makale oluştururken aşağıdaki alanlar kullanılabilir:

- **title (zorunlu):** 1-300 karakter. Slug otomatik oluşturulur.
- **content:** Milkdown JSON formatında zengin içerik.
- **excerpt:** İsteğe bağlı kısa özet.
- **contentType:** İçerik türü (reference, how-to, adr, runbook, faq, policy, onboarding).
- **tags:** Etiket dizisi — ID, isim veya slug kabul eder.
- **status:** Başlangıç durumu (yetkilere göre kısıtlı).

## Versiyon Kontrolü

Makale içeriği (content alanı) her değiştiğinde otomatik olarak yeni bir versiyon oluşturulur. Sadece başlık veya metadata değişikliklerinde versiyon oluşturulmaz.

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
