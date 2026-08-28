---
{
  "title": "Knowledge Portal — Başlangıç Rehberi",
  "contentType": "onboarding",
  "tags": [
    "project-knowledge-portal",
    "getting-started",
    "tutorial"
  ],
  "excerpt": "Knowledge Portal'ı kullanmaya başlamak için temel bilgiler, ilk makale oluşturma ve sistem tanıtımı.",
  "status": "published"
}
---

## Knowledge Portal Nedir?

Knowledge Portal, kurumsal bilgi birikimini organize etmek, aramak ve paylaşmak için tasarlanmış modern bir bilgi yönetim platformudur. Ekip üyeleri makaleler oluşturabilir, etiketleyebilir, versiyonlayabilir; fulltext, semantic ve hybrid aramayla doküman bulabilir; Bilgi Asistanı ile kaynaklı RAG yanıtı alabilir.

## Temel Özellikler

- **Zengin İçerik Editörü:** Milkdown tabanlı editör ile başlıklar, listeler, kod blokları, görseller ve tablolar oluşturabilirsiniz.
- **Doküman Arama:** PostgreSQL fulltext, pgvector tabanlı semantic ve RRF ile hybrid sonuç listeleri.
- **Bilgi Asistanı:** Portal kanıtlarına dayalı, atıflı ve fail-closed RAG yanıtları.
- **Versiyon Kontrolü:** Her içerik değişikliği otomatik olarak versiyonlanır. Önceki sürümlere geri dönebilirsiniz.
- **Etiket Sistemi:** Makaleleri etiketlerle kategorize edin, arama sırasında #etiket sözdizimi ile filtreleyin.
- **Dosya Ekleri:** Makalelere resim, PDF, Word ve diğer dosyaları ekleyebilirsiniz. Metin içerikli dosyalar arama indeksine dahil edilir.
- **Geri Bildirim:** Makalelere oy verin (faydalı/faydasız) ve yorum bırakın.

## Sisteme Giriş

Sisteme iki farklı yöntemle giriş yapabilirsiniz:

1. **E-posta ve Şifre:** Kayıt olurken belirlediğiniz e-posta ve şifre ile giriş yapın.
2. **Azure AD:** Kurumsal Microsoft hesabınız ile tek tıkla giriş yapın. İlk girişte otomatik hesap oluşturulur.

İlk kurulumda otomatik olarak bir yönetici hesabı oluşturulur. Giriş bilgileri için sistem yöneticinize başvurun ve ilk girişten sonra şifrenizi güncellemeyi unutmayın.

## İlk Makalenizi Oluşturun

1. Sol menüden 'Yeni Makale' butonuna tıklayın.
2. Makale başlığını girin — URL-dostu slug otomatik oluşturulur.
3. İçerik türünü seçin (Reference, How-To Guide, FAQ, Runbook, vb.).
4. İlgili etiketleri ekleyin.
5. Milkdown editörü ile zengin içerik yazın.
6. Taslak olarak kaydedin veya yayınlama yetkisiniz varsa doğrudan yayınlayın.

## Roller ve Yetkiler

Sistemde üç temel rol bulunur:

- **Admin:** Tüm yetkilere sahiptir. Kullanıcıları ve tüm kullanıcıların API key'lerini yönetebilir; herhangi bir makaleyi düzenleyebilir veya silebilir.
- **Editor:** Makale yayınlama, arşivleme, onaylama, etiket yönetimi, analitik görüntüleme ve kendi API key'lerini yönetme yetkisine sahiptir.
- **Viewer:** Makale oluşturabilir, kendi makalelerini düzenleyip yayınlayabilir ve kendi API key'lerini yönetebilir. Arşivleme, onaylama ve makale silme yetkisi yoktur.

## Sonraki Adımlar

Bu rehberi okuduktan sonra aşağıdaki makalelere göz atmanızı öneririz:

- API Kullanım Kılavuzu — Programatik erişim için
- Arama Motoru Rehberi — Gelişmiş arama özelliklerini keşfedin
- Makale Yönetimi — Versiyonlama, durum geçişleri ve iş akışları
