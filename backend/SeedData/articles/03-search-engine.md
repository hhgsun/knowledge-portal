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

Knowledge Portal dört arama modu sunar. Varsayılan mod fulltext'tir ve Ollama olmadan çalışır. Semantic ve RAG için Ollama embedding/chat servisleri kullanılır; hybrid arama lexical ve semantic sonuçları birleştirir.

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

Yayınlanmış makaleler ve metni çıkarılabilen ekleri varsayılan olarak yaklaşık 500 kelimelik, 50 kelime örtüşmeli parçalara ayrılır. Makale chunk'ları Markdown başlık/bölüm sınırlarını; ek chunk'ları PDF sayfası, Excel sayfası ve sunum slaydı gibi parser konumlarını korur. Paragraf, liste, tablo ve kod blokları hedef bütçeye sığıyorsa bölünmez; yalnız aşırı büyük tek bloklar kayan pencereye düşer. Hedef, örtüşme ve `ChunkingVersion` yapılandırılabilir. Ollama `bge-m3` modeli her parça için 1024 boyutlu embedding üretir. Vektörler PostgreSQL `pgvector` içinde tutulur; cosine distance sorguları HNSW indeksiyle hızlandırılır.

İndeksleme ve sorgu akışları model çıktısının `Ollama:EmbeddingDimensions` ile eşleştiğini PostgreSQL'e yazmadan veya vektör sorgusu çalıştırmadan önce doğrular. Health kontrolü de yanlış boyutlu bir modeli bağlı/sağlıklı kabul etmez.

- Liste tipi semantic aramanın varsayılan minimum benzerlik skoru 0.5'tir.
- Her makale için en iyi eşleşen chunk seçilir ve chunk indeksi sonuçta taşınır.
- Filtreler vektör sorgusunun içinde uygulanır; yayınlanmamış içerik sonuç havuzuna alınmaz.

Tam kelime eşleşmesi olmasa bile kavramsal yakınlık aradığınızda semantic modu kullanın.

## 3. Hybrid Arama

Fulltext ve semantic adaylar Reciprocal Rank Fusion (RRF) ile birleştirilir. Varsayılan ağırlıklar lexical için 0.4, semantic için 0.6 ve RRF `k` değeri için 60'tır. Deterministik query-understanding katmanı explicit metadata filtrelerini ayırır, yapılandırılmış acronym/synonym sözlüğünü genişletir ve yalnız bileşik soruları en fazla üç alt sorguya böler. Alt sorgular tekrar rank-fusion ile birleşir. Ranking; relevance yanında düşük ağırlıklı, merkezi yapılandırılan güncellik, onay ve içerik türü otorite sinyallerini kullanır. Varsayılan yerel reranker her zaman hazırdır; açıkça etkinleştirilirse bounded HTTPS cross-encoder kullanılır ve her hatada yerel sıralamaya dönülür.

Her iki sinyalden yararlanmak istediğiniz genel aramalarda hybrid mod önerilir. Semantic bacak kullanılamazsa lexical sonuçlarla kontrollü fallback uygulanır.

## 4. RAG Arama

RAG modu yalnızca makale listesi döndürmez; getirilen kanıtlara dayanarak doğal dilde yanıt oluşturur. Retrieval katmanı lexical ve semantic chunk adaylarını RRF ile birleştirir, yeniden sıralar, yakın kopyaları bastırır ve kaynak çeşitliliğini korur. Dar sorular tek üretim çağrısına, tüm içerikleri özetleme/karşılaştırma gibi geniş sorular bounded-parallel map-reduce akışına yönlendirilir.

Üretilen yanıt yapılandırılmış claim ve `[S1]` biçimindeki kanıt atıflarıyla doğrulanır. Her evidence öğesi ayrıca gerçek embedding satırının stabil `chunkId` değerini (lexical fallback için deterministik kimlik), yetki kontrollü canonical makale URL'sini ve varsa ayrıştırılmış PDF sayfa numarasını taşır. Bilinmeyen kanıt, lexical olarak desteklenmeyen iddia, sayı uyuşmazlığı veya negation çelişkisi bulunan claim kullanıcı yanıtına alınmaz. Yeterli kanıt yoksa sistem cevap uydurmak yerine açıkça reddeder.

Arama ekranında AI yanıtının kaynakları kompakt bir açılır/kapanır bölümde gösterilir. Bölüm açıldığında kaynak makaleler, ilişki skoru ve kanıt pasajları incelenebilir; her kaynak bağlantısı mevcut arama sonucunu kaybetmeden yeni bir tarayıcı sekmesinde açılır.

Uygulama ayrıntıları, güvenlik ve dayanıklılık kontrolleri için **RAG Mimarisi ve İşleyişi** makalesine bakın.

## Arama Filtreleri

Inline sözdizimi ve eşdeğer query parametreleri birlikte kullanılabilir:

- `#etiket-slug` — etiket filtresi; birden fazlası AND mantığıyla birleşir.
- `@yazar-slug` — yazar filtresi; birden fazlası OR mantığıyla birleşir.
- `##icerik-turu` — içerik türü filtresi; birden fazlası OR mantığıyla birleşir.
- `onlyOwnContent=true` — API key ile yalnız o key üzerinden oluşturulan içerikleri sınırlar.
- `includeContent=true` — kanonik Markdown'dan türetilmiş düz metni ekler.
- `includeAttachments=true` — ek dosya metadatasını ekler.

Örnek: `@admin #tutorial ##how-to react hooks`

## İndeksleme

Yayınlama, içerik değişikliği ve ek ekleme/silme işlemleri önce PostgreSQL-backed dayanıklı `index_jobs` kuyruğuna iş bırakır, ardından aynı istek içinde Full‑Text indeksini best-effort günceller. Bu nedenle normal akışta yeni içerik Full‑Text aramada hemen görünür; semantic embedding ise Ollama bağımlılığı nedeniyle asenkron kalır. Toplu ve kaynak içe aktarımlarındaki eager FTS denemesi transaction savepoint ile yalıtılır. Aynı makaledeki değişiklikler generation counter ile birleştirilir. Worker'lar işleri lease ve `FOR UPDATE SKIP LOCKED` ile sahiplenir; aynı anda çalıştırabileceklerinden fazla işi processing durumuna almaz, bounded parallelism, makale başına varsayılan 600 saniyelik toplam süre sınırı, exponential retry ve terminal failure takibi uygular. Worker ayrıca indeks işareti eksik olduğu hâlde kuyruk satırı bulunmayan veya işi tamamlanmış görünen makaleleri periyodik olarak uzlaştırır; geçici bir başlangıç/veritabanı kesintisi bu nedenle kalıcı kuyruk boşluğuna dönüşmez. Aktif ve terminal-hatalı işler bu otomatik uzlaştırma tarafından sıfırlanmaz. Worker eager FTS başarılı olsa bile güncel veriden FTS'yi tekrar doğrular, ardından semantic indeksi senkronize eder; böylece request-path hızlandırması dayanıklılık ve concurrent-edit garantilerini zayıflatmaz.

Editör ve yöneticiler makale listesinde, detay sayfasında ve düzenleme ekranında sürüme duyarlı indeks durumunu görür: `İndekslendi`, `İndeksleniyor`, `İndeks bekliyor`, `İndeks güncel değil` veya `İndeksleme başarısız`. İşaret yalnızca embedding satırının varlığına dayanmaz; makalenin güncel revizyonu için gerekli lexical ve (etkinse) semantic indekslerin tamamlanmış olmasını doğrular. Normal okuyucu yanıtları bu operasyonel alanı içermez.

Arama ekranındaki indeks kapsamı uyarısı seçilen moda ve aktif filtrelere göre hesaplanır. Fulltext arama yalnız `FtsIndexedAt`, semantic arama yalnız `IndexedAt`, hybrid ve RAG ise iki indeksi birlikte değerlendirir. Böylece yalnız semantic indeksi bekleyen bir makale Full‑Text sonuçlarında gereksiz uyarı oluşturmaz; filtre kapsamı dışındaki makaleler de mevcut aramayı eksikmiş gibi göstermez. Güncel olmayan semantic indekslerde eski embedding'ler geçici olarak hizmet vermeye devam edebileceği için mesaj, sonuçların "eksik veya eski" olabileceğini açıkça belirtir.

Yönetici kullanıcılar `/api/search/embedding-status`, `/api/search/diagnostics`, `/api/search/storage-status`, `/api/search/rag-observability` ve `/api/search/rag-debug` endpoint'leriyle arama altyapısını izleyebilir. Debug işlemi LLM çağırmadan rewrite/alt sorgu/filtre planını, yalnız yetkili rerank adaylarını, parent-komşu genişletmesini ve gerçek context bütçesini gösterir. Arama teşhis ekranı başarısız ve karakter sınırında kırpılmış ek çıkarımlarını da görünür kılar. `/settings/search` ekranındaki onarım yalnız indeksi eksik, retry bekleyen, terminal-hatalı veya lease süresi geçmiş kuyruk işlerini yeniden açar; sağlıklı veya aktif işleri değiştirmez.
