---
{
  "title": "Öne Çıkan Bağlantılar (Featured Links) Yönetimi",
  "contentType": "how-to",
  "tags": [
    "project-knowledge-portal",
    "tutorial",
    "best-practices"
  ],
  "excerpt": "Kenar çubuğunda gösterilen öne çıkan bağlantıların oluşturulması, türleri, renk/ikon özelleştirmesi ve API kullanımı.",
  "status": "published"
}
---

## Genel Bakış

Öne çıkan bağlantılar (Featured Links), kenar çubuğunda gösterilen ve sık kullanılan içeriklere hızlı erişim sağlayan kısayollardır. Bir etikete, bir içerik türüne veya özel bir URL'ye işaret edebilirler. Yönetimi için featured_links:manage yetkisi (admin rolü) gereklidir; görüntüleme için giriş yapmış olmak yeterlidir.

## Bağlantı Türleri

- **tag:** Bir etiket slug'ına işaret eder. Tıklandığında o etikete sahip makaleler listelenir. Hedef etiketin sistemde mevcut olması gerekir.
- **content_type:** Bir içerik türüne işaret eder (reference, how-to, runbook vb.). Hedef içerik türünün lookup değerlerinde tanımlı olması gerekir.
- **custom:** Özel bir hedef. İç yol (/ ile başlayan) veya http(s) URL olabilir. Harici dokümantasyon, dashboard veya araçlara bağlantı vermek için idealdir.

## Bağlantı Özellikleri

- **label (zorunlu):** Kenar çubuğunda gösterilecek metin.
- **linkType (zorunlu):** tag, content_type veya custom.
- **target (zorunlu):** Türe göre etiket slug'ı, içerik türü değeri veya URL/yol.
- **icon:** İsteğe bağlı Lucide ikon adı. Yönetim ekranındaki seçici tüm katalogda arama yapar; tarayıcıyı binlerce SVG ile yormamak için yalnız ilk eşleşme grubunu çizer. Resmî Lucide kataloğu bağlantısından bir ikon seçip adını (ör. `alarm-clock`) özel ikon alanına da girebilirsiniz. Geçerli ad yazılırken canlı önizleme gösterilir; seçimden sonra ikon ve canonical adı seçicide birlikte görünür. Set içinde bulunmayan adlar kabul edilmez.
- **color:** İsteğe bağlı renk — bağlantının kenar çubuğundaki vurgu rengini belirler. Hazır paletten seçim yapılabilir, tam renk spektrumunu sunan renk çarkından herhangi bir ton belirlenebilir veya `#2563eb` gibi 3/6 haneli özel bir HEX kodu girilebilir. Renk değişirken seçili ikon, vurgu rengi ve hafif renkli arka planıyla birlikte canlı önizlenir.
- **sortOrder:** Sıralama değeri. Belirtilmezse listenin sonuna eklenir.
- **isActive:** Pasif bağlantılar kenar çubuğunda gösterilmez ama silinmez — geçici olarak gizlemek için kullanın.

## API Kullanımı

```bash
# Aktif bağlantıları listele
GET /api/featured-links

# Pasif bağlantılar dahil listele (yönetim ekranı için)
GET /api/featured-links?includeInactive=true

# Yeni bağlantı oluştur (admin)
POST /api/featured-links
{
  "label": "Runbook'lar",
  "linkType": "content_type",
  "target": "runbook",
  "icon": "terminal",
  "color": "orange"
}

# Bağlantı güncelle (admin)
PUT /api/featured-links
{
  "id": "abc123",
  "label": "Operasyon Runbook'ları",
  "isActive": false
}

# Bağlantı sil (admin)
DELETE /api/featured-links?id=abc123
```

## Doğrulama Kuralları

- tag türünde hedef etiket sistemde yoksa istek reddedilir.
- content_type türünde hedef içerik türü tanımlı değilse istek reddedilir.
- custom türünde hedef / ile başlamalı veya http(s) URL olmalıdır.
- Bağlantı türü (linkType) oluşturulduktan sonra değiştirilemez — farklı tür gerekiyorsa yeni bağlantı oluşturun.

## İyi Uygulamalar

- Bağlantı sayısını sınırlı tutun (5-8 arası) — kenar çubuğu kalabalıklaşırsa hızlı erişim avantajı kaybolur.
- En sık kullanılan içerikleri en üste yerleştirin (sortOrder ile).
- Renk ve ikonları tutarlı kullanın — örneğin tüm runbook bağlantıları için aynı renk ailesi.
- Geçici kampanya veya duyuru bağlantılarını silmek yerine isActive=false yaparak arşivleyin.
