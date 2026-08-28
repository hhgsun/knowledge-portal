---
{
  "title": "Arama Motoru — Fulltext, Semantic, Hybrid ve RAG",
  "contentType": "reference",
  "tags": [
    "project-knowledge-portal",
    "best-practices",
    "performance"
  ],
  "excerpt": "Knowledge Portal'ın dört arama modunun güncel mimarisi, kullanım senaryoları, filtreleri ve indeksleme davranışı.",
  "status": "published"
}
---

## Arama Modları

Knowledge Portal API'si dört arama modu sunar. Doküman arama ekranı fulltext, semantic ve hybrid sonuç listelerine odaklanır; kaynaklı RAG yanıtlarının kanonik kullanıcı deneyimi konuşma geçmişi ve güvenli yönlendirme sağlayan **Bilgi Asistanı**dır. Arama ekranındaki bir sorgu, “Kanıtlı yanıt al” bağlantısıyla metni kaybetmeden Asistan'ın yanıt moduna aktarılabilir; eski `/search?q=...&type=rag` arayüz bağlantıları da Asistan'a yönlendirilir. Doğrudan `GET /api/search?type=rag` API sözleşmesi MCP ve diğer entegrasyonlar için korunur. Varsayılan API modu fulltext'tir ve Ollama olmadan çalışır. Semantic ve RAG için Ollama embedding/chat servisleri kullanılır; hybrid arama lexical ve semantic sonuçları birleştirir.

## 1. Fulltext Arama

PostgreSQL `tsvector`/`tsquery` altyapısı ve GIN indeksi kullanılır. Türkçe sözlük sayesinde stemming ve stopword desteği sağlanır; başlık, özet, makale gövdesi ve metni çıkarılabilen ekler aranır.

### Nasıl Çalışır?

- Çok kelimeli sorgu önce AND mantığıyla çalışır.
- Sonuç yoksa OR sorgusu, ardından başlık/özet üzerinde güvenli `ILIKE` fallback uygulanır.
- `%` ve `_` karakterleri LIKE sorgularında escape edilir.
- Sonuçlar rank'e göre, eşitlikte makale kimliğine göre deterministik sıralanır.
- Fulltext ve yalnız etiketle gezinti modları sayfalıdır; filtrelerden sonraki gerçek `total` ve `totalPages` değerleri döner.
- Sonuç, makale gövdesindeki eşleşmenin çevresinden yaklaşık 240 karakterlik bir `snippet` içerebilir.

Kesin terim, hata kodu veya ürün adı ararken fulltext iyi bir seçimdir.

## 2. Semantic Arama

Yayınlanmış makaleler ve ekleri gerçek parent-child hiyerarşisine ayrılır. Başlık/PDF sayfası/Excel sheet'i/sunum slaydı sınırlarını geçmeyen parent'lar varsayılan yaklaşık 1.000 kelimedir; aranan ve embed edilen child'lar yaklaşık 220 kelime ve 40 kelime örtüşmelidir. Paragraf, liste, GFM Markdown tablosu ve kod blokları hedef bütçeye sığıyorsa biçimiyle korunur; büyük tablolar bölünürken kolon başlığı her child'da tekrarlanır. Yalnız child için `bge-m3` ile 1024 boyutlu embedding üretilir, eşleşme sonrasında yetkili parent bağlamı RAG'e taşınır. Vektörler PostgreSQL `pgvector` içinde tutulur; cosine distance sorguları HNSW indeksiyle hızlandırılır.

Ekler FTS ve semantic indeks öncesinde tek bir kalıcı canonical extraction'dan geçer. DOCX, XLSX, PPTX ve CSV tabloları satır-sütun ilişkisini koruyan GFM Markdown'a çevrilir; XLSX formülüyle hesaplanmış değer birlikte saklanır. PNG/JPEG/WebP/GIF dosyaları ile PDF/DOCX/XLSX/PPTX içindeki uygun görseller sınırlı sayıda yerel `qwen2.5vl` çağrısıyla açıklama, literal OCR, tablo ve şema/graf ilişkilerine dönüştürülür. Karmaşık veya taranmış belgeler için `DocumentParsing:External` altında Unstructured `hi_res` endpoint'i açıkça etkinleştirilebilir; varsayılan kapalıdır ve dosya kurum dışına gönderilmez. Parser/sürüm, strateji, vision model/prompt/bütçeleri veya çıkarım limiti değişince extraction profile ve semantic index profile değişir; dayanıklı kuyruk FTS ve embedding'i atomik makale geçişiyle yeniler.

İndeksleme ve sorgu akışları model çıktısının `Ollama:EmbeddingDimensions` ile eşleştiğini PostgreSQL'e yazmadan veya vektör sorgusu çalıştırmadan önce doğrular. Health kontrolü de yanlış boyutlu bir modeli bağlı/sağlıklı kabul etmez.

- Liste tipi semantic aramanın varsayılan minimum benzerlik skoru 0.5'tir.
- Her makale için en iyi eşleşen chunk seçilir ve chunk indeksi sonuçta taşınır.
- Filtreler vektör sorgusunun içinde uygulanır; yayınlanmamış içerik sonuç havuzuna alınmaz.

Tam kelime eşleşmesi olmasa bile kavramsal yakınlık aradığınızda semantic modu kullanın.

## 3. Hybrid Arama

Fulltext ve semantic adaylar Reciprocal Rank Fusion (RRF) ile birleştirilir. Varsayılan ağırlıklar lexical için 0.4, semantic için 0.6 ve RRF `k` değeri için 60'tır. Deterministik query-understanding katmanı explicit metadata filtrelerini ayırır, yapılandırılmış acronym/synonym sözlüğünü genişletir ve yalnız bileşik soruları en fazla üç alt sorguya böler. Alt sorgular tekrar rank-fusion ile birleşir. Ranking; relevance yanında düşük ağırlıklı, merkezi yapılandırılan güncellik, onay ve içerik türü otorite sinyallerini kullanır. Varsayılan yerel reranker her zaman hazırdır; açıkça etkinleştirilirse bounded HTTPS cross-encoder kullanılır ve her hatada yerel sıralamaya dönülür.

Her iki sinyalden yararlanmak istediğiniz genel aramalarda hybrid mod önerilir. Semantic bacak kullanılamazsa lexical sonuçlarla kontrollü fallback uygulanır.

## 4. RAG Arama

RAG modu yalnızca makale listesi döndürmez; getirilen kanıtlara dayanarak önce kısa sonuç, ardından rol bazlı kanıtlı açıklama oluşturur. Model doküman başlıklarını veya sonuçları tek tek aktarmak yerine ilgili pasajları sentezler; açıklamadaki her olgusal cümle yine deterministik grounding kontrolünden geçer. Retrieval katmanı lexical ve semantic chunk adaylarını RRF ile birleştirir, dinamik content-type otoritesiyle yeniden sıralar, yakın kopyaları bastırır ve kaynak çeşitliliğini korur. Dar sorularda kaynak genişliği query token karmaşıklığı ve marjinal relevance skoruna göre varsayılan 3-10 makale arasında seçilir; context model-calibrated token bütçesiyle sınırlandırılır. Tüm içerikleri özetleme/karşılaştırma gibi geniş sorular bounded-parallel map-reduce akışına yönlendirilir. API ve arayüz, cevapta atıf yapılan kaynaklarla yalnız incelenen kaynakları ayrı gösterir; deterministik sayısal/polarity uyuşmazlıkları yönetişim önceliğiyle ayrıca raporlar.

Üretilen yanıt yapılandırılmış claim ve `[S1]` biçimindeki kanıt atıflarıyla doğrulanır. Her evidence öğesi ayrıca gerçek embedding satırının stabil `chunkId` değerini (lexical fallback için deterministik kimlik), yetki kontrollü canonical makale URL'sini ve varsa ayrıştırılmış PDF sayfa numarasını taşır. Bilinmeyen kanıt, lexical olarak desteklenmeyen iddia, sayı uyuşmazlığı veya negation çelişkisi bulunan claim kullanıcı yanıtına alınmaz. Yeterli kanıt yoksa sistem cevap uydurmak yerine açıkça reddeder.

Bilgi Asistanı AI yanıtında atıf yapılan ve yalnız incelenen kaynakları ayrı gösterir; kanıt pasajları ve varsa sayfa/ek konumu ayrıca incelenebilir. Kaynak bağlantıları makaleyi açar ve tıklama mevcut arama kalite telemetrisine bağlanır. Doğrudan RAG API yanıtı aynı claim, evidence, kaynak yönetişimi ve conflict assessment alanlarını korur.

Uygulama ayrıntıları, güvenlik ve dayanıklılık kontrolleri için **RAG Mimarisi ve İşleyişi** makalesine bakın.

## Arama Filtreleri

Inline sözdizimi ve eşdeğer query parametreleri birlikte kullanılabilir:

- `#etiket-slug` — etiket filtresi; birden fazlası AND mantığıyla birleşir.
- `@yazar-slug` — yazar filtresi; birden fazlası OR mantığıyla birleşir.
- `##icerik-turu` — içerik türü filtresi; birden fazlası OR mantığıyla birleşir.
- `onlyOwnContent=true` — API key ile yalnız o key üzerinden oluşturulan içerikleri sınırlar.
- `includeContent=true` — kanonik Markdown string'ini `contentMarkdown` alanında ekler. Türetilmiş düz metin yalnızca detay yanıtındaki `contentText` alanında sunulur.
- `includeAttachments=true` — ek dosya metadatasını ekler.

Örnek: `@admin #tutorial ##how-to react hooks`

## İndeksleme

Yayınlama, içerik değişikliği ve ek ekleme/silme işlemleri önce PostgreSQL-backed dayanıklı `index_jobs` kuyruğuna iş bırakır, ardından aynı istek içinde Full‑Text indeksini best-effort günceller. Bu nedenle normal akışta yeni içerik Full‑Text aramada hemen görünür; semantic embedding ise Ollama bağımlılığı nedeniyle asenkron kalır. Toplu ve kaynak içe aktarımlarındaki eager FTS denemesi transaction savepoint ile yalıtılır. Aynı makaledeki değişiklikler generation counter ile birleştirilir. Worker'lar işleri lease ve `FOR UPDATE SKIP LOCKED` ile sahiplenir; aynı anda çalıştırabileceklerinden fazla işi processing durumuna almaz, bounded parallelism, makale başına varsayılan 600 saniyelik toplam süre sınırı, exponential retry ve terminal failure takibi uygular. Worker ayrıca indeks işareti eksik olduğu hâlde kuyruk satırı bulunmayan veya işi tamamlanmış görünen makaleleri periyodik olarak uzlaştırır; geçici bir başlangıç/veritabanı kesintisi bu nedenle kalıcı kuyruk boşluğuna dönüşmez. Aktif ve terminal-hatalı işler bu otomatik uzlaştırma tarafından sıfırlanmaz. Worker eager FTS başarılı olsa bile güncel veriden FTS'yi tekrar doğrular, ardından semantic indeksi senkronize eder; böylece request-path hızlandırması dayanıklılık ve concurrent-edit garantilerini zayıflatmaz.

Editör ve yöneticiler makale listesinde, detay sayfasında ve düzenleme ekranında sürüme duyarlı indeks durumunu görür: `İndekslendi`, `İndeksleniyor`, `İndeks bekliyor`, `İndeks güncel değil` veya `İndeksleme başarısız`. İşaret yalnızca embedding satırının varlığına dayanmaz; makalenin güncel revizyonu için gerekli lexical ve (etkinse) semantic indekslerin tamamlanmış olmasını doğrular. Normal okuyucu yanıtları bu operasyonel alanı içermez.

Arama yanıtındaki indeks kapsamı uyarısı seçilen moda ve aktif filtrelere göre hesaplanır. Fulltext arama yalnız `FtsIndexedAt`, semantic arama yalnız `IndexedAt`, hybrid ve RAG ise iki indeksi birlikte değerlendirir. Böylece yalnız semantic indeksi bekleyen bir makale Full‑Text sonuçlarında gereksiz uyarı oluşturmaz; filtre kapsamı dışındaki makaleler de mevcut aramayı eksikmiş gibi göstermez. Güncel olmayan semantic indekslerde eski embedding'ler geçici olarak hizmet vermeye devam edebileceği için mesaj, sonuçların "eksik veya eski" olabileceğini açıkça belirtir.

Yönetici kullanıcılar `/api/search/embedding-status`, `/api/search/diagnostics`, `/api/search/storage-status`, `/api/search/rag-observability` ve `/api/search/rag-debug` endpoint'leriyle arama altyapısını izleyebilir. Debug işlemi LLM çağırmadan rewrite/alt sorgu/filtre planını, yalnız yetkili rerank adaylarını, child→parent genişletmesini ve gerçek context bütçesini gösterir. Arama teşhis ekranı başarısız ve karakter sınırında kırpılmış ek çıkarımlarını da görünür kılar. `/settings/search` ekranındaki onarım yalnız indeksi eksik, retry bekleyen, terminal-hatalı veya lease süresi geçmiş kuyruk işlerini yeniden açar; sağlıklı veya aktif işleri değiştirmez.
