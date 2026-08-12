---
{
  "title": "Dosya Ekleri (Attachments) Kullanımı",
  "contentType": "how-to",
  "tags": [
    "project-knowledge-portal",
    "tutorial"
  ],
  "excerpt": "Makalelere dosya ekleme, görsel yapıştırma, desteklenen formatlar ve indeksleme davranışı.",
  "status": "published"
}
---

## Genel Bakış

Knowledge Portal makalelere dosya eklemenize olanak tanır. Eklenen dosyalar güvenli bir şekilde sunucuda saklanır, metin içerikli dosyalar (PDF, Word, Markdown vb.) otomatik olarak arama indeksine dahil edilir.

## Desteklenen Dosya Formatları

Aşağıdaki uzantılara sahip dosyalar yüklenebilir:

- **Görseller:** .png, .jpg, .jpeg, .gif, .webp, .svg
- **Dokümanlar:** .pdf, .docx, .txt, .md
- **Veri dosyaları:** .xlsx, .csv, .json, .yaml

## Sınırlamalar

- **Maksimum dosya boyutu:** 20 MB
- **Makale başına maksimum dosya:** 20 adet
- Uzantı whitelist dışındaki dosyalar reddedilir.

## Frontend'de Dosya Ekleme

### Dosya Yükleme

1. Makale düzenleme sayfasında 'Ekler' bölümüne gidin.
2. Dosya seçin veya sürükle-bırak ile ekleyin.
3. Dosyalar 'Kaydedilince yüklenecek' badge'i ile gösterilir (deferred upload).
4. Makaleyi kaydettiğinizde dosyalar sunucuya yüklenir.

### Görsel Yapıştırma (Paste/Drop)

Milkdown editörüne doğrudan görsel yapıştırabilir veya sürükleyebilirsiniz:

1. Bir görseli kopyalayın (Ctrl+C) veya dosyayı editöre sürükleyin.
2. Görsel geçici blob URL ile editörde gösterilir.
3. Makale kaydedildiğinde blob URL'ler gerçek download URL'lerine dönüştürülür.

### Dosya Silme

Düzenleme modunda bir eki sildiğinizde, dosya hemen silinmez — üzeri çizili olarak işaretlenir ve 'Kaydedilince silinecek' badge'i gösterilir. Geri alma (undo) mümkündür. Asıl silme işlemi makale kaydedildiğinde gerçekleşir.

## API ile Dosya İşlemleri

```bash
# Dosya yükleme (multipart/form-data)
curl -X POST http://localhost:5174/api/articles/{articleId}/attachments \
  -H "Authorization: Bearer {token}" \
  -F "file=@document.pdf"

# Ek listesi
GET /api/articles/{articleId}/attachments

# Dosya indirme
GET /api/attachments/{attachmentId}/download

# Dosya silme
DELETE /api/articles/{articleId}/attachments/{attachmentId}
```

## İndeksleme Davranışı

Metin içerikli dosyalar otomatik olarak arama indeksine dahil edilir:

- **İndekslenen formatlar:** .pdf, .docx, .txt, .md, .csv, .json, .yaml
- **Maksimum metin:** Her ek için en fazla 50.000 karakter çıkarılır.
- Desteklenmeyen veya bozuk dosyalar sessizce atlanır.
- Yayınlanmış makalelere ek ekleme/silme, FTS5 ve embedding indekslerinin yeniden oluşturulmasını tetikler.

## Depolama

Dosyalar disk üzerinde data/uploads/{articleId}/ dizininde saklanır. Makale silindiğinde tüm fiziksel dosyalar ve veritabanı kayıtları cascade ile temizlenir.
