---
{
  "title": "Analitik, Dashboard ve Geri Bildirim Sistemi",
  "contentType": "reference",
  "tags": [
    "project-knowledge-portal",
    "monitoring",
    "best-practices"
  ],
  "excerpt": "Dashboard metrikleri, analitik paneli, makale oylama ve yorum sisteminin çalışma şekli.",
  "status": "published"
}
---

## Dashboard

Ana sayfa dashboard'u (GET /api/dashboard) tüm giriş yapmış kullanıcılara açıktır ve portalın genel durumunu özetler:

- **Toplam makale sayısı** — Sistemdeki tüm makaleler.
- **Haftalık görüntülenme** — Son 7 gündeki toplam makale görüntülenmesi.
- **Bugünkü aramalar** — Gün içinde yapılan arama sayısı.
- **Güncelliğini yitirmiş makaleler** — Uzun süredir gözden geçirilmemiş (stale) makale sayısı.
- **Son yayınlanan makaleler** — En yeni 5 yayınlanmış makale.
- **Popüler aramalar** — En çok yapılan 5 arama sorgusu.

## Analitik Paneli

Analitik paneli (GET /api/analytics) daha detaylı metrikler sunar. Erişim için analytics:view yetkisi gerekir (admin ve editor rolleri) ve yalnızca session (JWT) ile erişilebilir — API key ile erişilemez.

- **Durum dağılımı:** Makalelerin draft/published/archived durumlarına göre sayıları.
- **Popüler aramalar:** En çok yapılan 10 arama sorgusu ve sayıları.
- **Sonuçsuz aramalar:** Sonuç döndürmeyen 10 arama — içerik açığı tespiti için en değerli metrik. Kullanıcıların aradığı ama bulamadığı konular yeni makale adaylarıdır.
- **En çok görüntülenenler:** Son 7 günde en çok görüntülenen 10 makale.

## Görüntülenme Sayımı

Makale görüntülenmeleri otomatik kaydedilir. Aynı kullanıcının aynı makaleyi 15 dakika içinde tekrar açması yeni görüntülenme olarak sayılmaz (deduplication). Bu sayede metrikler yapay olarak şişmez.

## Oylama (Faydalı / Faydasız)

Her kullanıcı bir makaleye tek oy verebilir. Oylama davranışı:

- **Yeni oy:** POST /api/articles/{id}/vote ile isHelpful değeri gönderilir.
- **Aynı oyu tekrarlama:** Oy geri çekilir (toggle davranışı).
- **Farklı oy:** Mevcut oy güncellenir (faydalı → faydasız veya tersi).
- **Gerekçe:** Faydasız oylarında isteğe bağlı gerekçe (reason) eklenebilir. Faydalı oylarda gerekçe alınmaz.

Oy dağılımı GET /api/articles/{id}/votes ile sorgulanabilir. Faydasız oy gerekçeleri, makale sahibinin içeriği iyileştirmesi için önemli bir sinyaldir.

## Yorumlar

Makalelere yorum bırakılabilir:

```bash
# Yorum ekle
POST /api/articles/{articleId}/comments
{"content": "Bu makaleye X senaryosu da eklenebilir."}

# Yorumları listele
GET /api/articles/{articleId}/comments

# Yorum sil (kendi yorumu veya admin)
DELETE /api/articles/{articleId}/comments/{commentId}
```

## Metrikleri İçerik Kalitesi İçin Kullanma

1. Sonuçsuz aramaları düzenli inceleyin — her sonuçsuz arama, eksik bir makale demektir.
2. Faydasız oy alan makaleleri gerekçeleriyle birlikte gözden geçirin ve güncelleyin.
3. Güncelliğini yitirmiş (stale) makaleleri periyodik olarak gözden geçirip yeniden yayınlayın veya arşivleyin.
4. En çok görüntülenen makaleleri öne çıkan bağlantılara (Featured Links) ekleyerek erişimi kolaylaştırın.
