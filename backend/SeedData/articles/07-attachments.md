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

- **Maksimum dosya boyutu:** `FileStorage:MaxFileSizeMB` ile yapılandırılır (varsayılan 20 MB).
- **Makale başına maksimum dosya:** `FileStorage:MaxAttachmentsPerArticle` ile yapılandırılır (varsayılan 20 adet).
- **Çıkarılan metin sınırı:** Varsayılan 50.000 karakter; `FileStorage:MaxExtractedCharacters` ile 1.000–5.000.000 arasında yapılandırılır.
- Uzantı whitelist dışındaki dosyalar reddedilir.

İzin verilen uzantıların tek çalışma zamanı kaynağı `FileStorage:AllowedExtensions` ayarıdır. Her izinli uzantı için kabul edilen MIME değerleri `FileStorage:AllowedContentTypes` altında yapılandırılır; MIME politikası bulunmayan uzantı güvenli biçimde reddedilir. Authenticated frontend uzantı listesini ve boyut/adet sınırlarını `GET /api/capabilities` üzerinden alır; böylece backend ayarı değiştiğinde dosya seçicilerinde ayrıca hard-coded liste güncellemek gerekmez. Normal attachment endpoint'inin request-body sınırı da `MaxFileSizeMB` değerinden türetilir; asıl dosya boyutu denetimi backend'de aynı ayarla yapılır.

## Frontend'de Dosya Ekleme

### Dosya Yükleme

1. Makale düzenleme sayfasında 'Ekler' bölümüne gidin.
2. Dosya seçin veya sürükle-bırak ile ekleyin.
3. Dosyalar 'Kaydedilince yüklenecek' badge'i ile gösterilir (deferred upload).
4. Dosyanın metni arama ve RAG'e girmemeliyse (örneğin aynı `.md` içeriği editör gövdesine de konduysa) **İndekse dahil et** seçeneğini kapatın.
5. Makaleyi kaydettiğinizde dosyalar sunucuya yüklenir.

### Görsel Yapıştırma (Paste/Drop)

Milkdown editörüne doğrudan görsel yapıştırabilir veya sürükleyebilirsiniz:

1. Bir görseli kopyalayın (Ctrl+C) veya dosyayı editöre sürükleyin.
2. Görsel geçici blob URL ile editörde gösterilir.
3. Makale kaydedildiğinde blob URL'ler gerçek download URL'lerine dönüştürülür.

### Dosya Silme

Düzenleme modunda bir eki sildiğinizde, dosya hemen silinmez — üzeri çizili olarak işaretlenir ve 'Kaydedilince silinecek' badge'i gösterilir. Geri alma (undo) mümkündür. Asıl silme işlemi makale kaydedildiğinde gerçekleşir.

## API ile Dosya İşlemleri

Bu bölümdeki `{site-url}`, tarayıcıda açtığınız mevcut Knowledge Portal adresidir (ör. `https://knowledge.example.com`); protokol dahil ve sonunda `/` olmadan yazılır.

```bash
# Dosya yükleme (multipart/form-data)
curl -X POST "{site-url}/api/articles/{articleId}/attachments" \
  -H "Authorization: Bearer {token}" \
  -F "file=@document.pdf" \
  -F "includeInIndex=false"

# Ek listesi
GET /api/articles/{articleId}/attachments

# Dosya indirme
GET /api/attachments/{attachmentId}/download

# Dosya silme
DELETE /api/articles/{articleId}/attachments/{attachmentId}
```

## İndeksleme Davranışı

Metin içerikli dosyalar varsayılan olarak arama indeksine dahil edilir:

- **Açık kontrol:** Multipart `includeInIndex` alanı varsayılan `true` değeridir. `false` olduğunda dosya saklanır ve indirilebilir, fakat FTS, semantic search ve RAG kanıtlarına girmez.
- **İndekslenen formatlar:** .pdf, .docx, .txt, .md, .csv, .json, .yaml
- **Maksimum metin:** Her ek için yapılandırılmış sınır kadar karakter çıkarılır. Kullanılan sınır, çıkarılan karakter sayısı ve truncation bilgisi ek metadata'sında saklanır.
- Yapılandırılmış sınır değiştiğinde önbellekteki çıkarım bir sonraki indeks geçişinde dosyadan yeniden üretilir.
- Desteklenmeyen metinsiz formatlar `no_text`, bozuk dosyalar `failed` durumuyla kaydedilir; sınırı aşanlar `/api/search/storage-status` içinde ayrıca sayılır.
- Yayınlanmış makalelere ek ekleme/silme, PostgreSQL fulltext ve pgvector embedding indekslerinin yeniden oluşturulmasını tetikler.

## Depolama

Dosyalar disk üzerinde data/uploads/{articleId}/ dizininde saklanır. Makale silindiğinde veritabanı kayıtları cascade ile temizlenir; fiziksel makale dizini kurtarılabilir `data/uploads/.trash` alanına taşınır.
