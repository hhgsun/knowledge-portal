---
{
  "title": "İçerik Yazım ve Etiketleme Rehberi",
  "contentType": "policy",
  "tags": [
    "project-knowledge-portal",
    "best-practices",
    "getting-started"
  ],
  "excerpt": "Knowledge Portal'da kaliteli, bulunabilir ve sürdürülebilir içerik üretmek için yazım standartları, içerik türü seçimi ve etiketleme kuralları.",
  "status": "published"
}
---

## Neden Bir Yazım Rehberi?

Bilgi portalının değeri, içeriklerin bulunabilirliği ve güvenilirliği ile ölçülür. Tutarlı başlıklar, doğru içerik türü seçimi ve disiplinli etiketleme; arama sonuçlarının kalitesini, RAG yanıtlarının doğruluğunu ve içeriklerin bakımını doğrudan etkiler.

## Doğru İçerik Türünü Seçin

- **Reference:** Kalıcı başvuru bilgisi — API dokümantasyonu, yapılandırma referansı, kavram açıklamaları.
- **How-To Guide:** Belirli bir hedefe götüren adım adım talimatlar. Tek bir görevi kapsar.
- **ADR (Architecture Decision Record):** Alınan teknik kararlar, gerekçeleri ve değerlendirilen alternatifler.
- **Runbook:** Operasyonel prosedürler — deployment, yedekleme, olay müdahalesi. Acil durumda takip edilecek netlikte olmalıdır.
- **FAQ:** Sık sorulan sorular ve kısa yanıtları.
- **Policy:** Ekipçe uyulması beklenen kurallar ve standartlar (bu makale gibi).
- **Onboarding:** Yeni ekip üyeleri için başlangıç rehberleri.

## Başlık Yazımı

- Başlık, makalenin yanıtladığı soruyu yansıtmalı: "Azure AD ile Kurumsal Giriş" gibi — "Giriş Notları" gibi belirsiz başlıklardan kaçının.
- Aranacak anahtar kelimeleri başlıkta geçirin — kullanıcılar çoğunlukla başlıktaki terimlerle arar.
- Başlıktan URL-dostu slug otomatik üretilir; Türkçe karakterler otomatik translitere edilir.

## İçerik Yapısı

1. **Özet (excerpt) yazın:** 1-2 cümlelik özet, arama sonuçlarında ve listelerde gösterilir.
2. **Başlık hiyerarşisi kullanın:** H2 ana bölümler, H3 alt bölümler. Tek uzun paragraf yerine taranabilir yapı kurun.
3. **Kod bloklarını dil belirterek ekleyin:** Editörde kod bloğu dili seçilirse sözdizimi vurgulama uygulanır.
4. **Adımları numaralı liste ile verin:** How-to ve runbook içeriklerinde her adım tek bir eylem olmalı.
5. **Görsel ekleyin:** Ekran görüntülerini editöre doğrudan yapıştırabilirsiniz — kaydetme sırasında kalıcı hale gelir.

## Etiketleme Kuralları

- Her makaleye en az bir, en fazla 4-5 etiket ekleyin. Aşırı etiketleme filtrelemeyi anlamsızlaştırır.
- Yeni etiket oluşturmadan önce mevcut etiketleri kontrol edin — "k8s" ve "kubernetes" gibi eş anlamlı etiketler içeriği böler.
- Makale oluşturma veya kendi makalenizi düzenleme sırasında rolünüzden bağımsız olarak yeni bir etiket yazıp makaleyle birlikte kaydedebilirsiniz. Bu yetki yalnızca makale bağlamındadır; Etiket Yönetimi ekranında bağımsız oluşturma, yeniden adlandırma ve silme işlemleri `tags:manage` yetkisi gerektirir.
- Proje kapsamındaki içerikler için proje etiketi kullanın: bu portalın kendi dokümantasyonu project-knowledge-portal etiketini taşır.
- Aramada #etiket-slug sözdizimi ile filtreleme yapılabildiğini unutmayın — etiket slug'ları kısa ve tahmin edilebilir olmalı.

## Güvenlik: İçerikte Ne Paylaşılmaz?

- Şifre, API key, token veya bağlantı dizesi gibi gizli bilgileri asla makale içeriğine veya eklere koymayın.
- Örneklerde her zaman yer tutucu kullanın: kullanici@ornek.com, kp_your_api_key_here gibi.
- Gerçek gizli değerler için secret manager veya environment variable kullanın; makalede yalnızca değerin nereden alınacağını tarif edin.
- Kişisel veri (TC kimlik, telefon, adres) içeren ekran görüntülerini maskeleyerek ekleyin.

## Yayın Öncesi Kontrol Listesi

1. Başlık aranabilir ve açıklayıcı mı?
2. Excerpt dolduruldu mu?
3. İçerik türü doğru seçildi mi?
4. Etiketler eklendi mi ve mevcut etiketlerle tutarlı mı?
5. İçerikte gizli bilgi (şifre, key, token) yok mu?
6. Kod örnekleri test edildi mi?

## İçeriği Güncel Tutma

Uzun süre gözden geçirilmeyen makaleler analitik panelinde "güncelliğini yitirmiş" (stale) olarak işaretlenir. Sahip olduğunuz makaleleri periyodik olarak gözden geçirin: hâlâ doğruysa yeniden yayınlayarak gözden geçirme tarihini yenileyin, geçerliliğini yitirdiyse arşivleyin. Her içerik değişikliğinde otomatik versiyon oluşturulduğu için güncellemelerden çekinmeyin — önceki sürüme her zaman dönülebilir.
