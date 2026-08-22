---
{
  "title": "RAG Mimarisi ve İşleyişi",
  "contentType": "reference",
  "tags": [
    "project-knowledge-portal",
    "best-practices",
    "security",
    "monitoring"
  ],
  "excerpt": "Knowledge Portal RAG akışının indeksleme, hybrid retrieval, üretim, fail-closed doğrulama, dayanıklılık ve gözlemlenebilirlik mimarisi.",
  "status": "published"
}
---

## Amaç ve Sınır

Knowledge Portal'ın RAG (Retrieval-Augmented Generation) modu, yayınlanmış portal içeriğinden kanıt getirip bu kanıtlara dayalı bir yanıt üretir. Modelin genel bilgisini doğruluk kaynağı kabul etmez. Yeterli veya doğrulanabilir kanıt yoksa yanıt uydurmak yerine açıkça yetersiz bağlam sonucu döner.

RAG akışının ana adımları:

```text
Makale + ekler
    │
    ▼
Dayanıklı indeks kuyruğu → chunk + embedding + FTS
                                   │
Kullanıcı sorusu                   ▼
    └──────────────→ lexical + semantic retrieval
                              │
                              ▼
                     RRF + rerank + dedupe
                              │
                    dar soru ─┴─ geniş soru
                       │             │
                  tek üretim     map → reduce
                       └──────┬──────┘
                              ▼
                    claim/atıf doğrulama
                              │
                              ▼
                 doğrulanmış yanıt + evidence
```

## 1. İndeksleme ve Provenance

Makale yayınlandığında, içeriği değiştiğinde veya eki eklenip silindiğinde önce `index_jobs` tablosuna iş yazılır. Ardından kullanıcıların yeni içeriği Full‑Text aramada hemen bulabilmesi için yerel PostgreSQL FTS aynı istekte best-effort güncellenir; semantic embedding asenkron kalır. Kuyruk makale başına generation counter ile değişiklikleri birleştirir. Worker'lar işleri `FOR UPDATE SKIP LOCKED` ile claim eder; lease, bounded parallelism, exponential retry ve terminal failure takibi kullanır. Worker her işi işlerken FTS'yi güncel veriden yeniden hesapladığı için eager işlem RAG'in dayanıklı kuyruğunu, generation guard'ını veya semantic indeks tutarlılığını bypass etmez.

Kanonik Markdown okunabilir düz metne çevrilir. Makale gövdesi ile metni çıkarılabilen her ek ayrı kaynak kabul edilir:

- Makale Markdown'ı başlık/bölüm sınırları korunarak; ekler parser'ın page/sheet/slide konumları korunarak chunk'lara ayrılır. Paragraf, liste, tablo ve kod blokları hedef bütçeye sığıyorsa bölünmez; aşırı büyük tek bloklar kontrollü kayan pencereye düşer.
- Eklerden çıkarılacak metin `FileStorage:MaxExtractedCharacters` ile sınırlandırılır. Sınır, çıkarılan karakter sayısı ve truncation durumu kalıcı metadata'dır; sınır değişince sonraki indeks geçişi cache'i yeniden üretir ve storage teşhisleri kırpılan dosyaları sayar.
- Varsayılan hedef 500 kelime ve örtüşme 50 kelimedir; `ChunkTargetWords`, `ChunkOverlapWords` ve `ChunkingVersion` yapılandırılabilir. Chunking sürümü ve sınırlar içerik hash'ine katıldığı için değişiklik dayanıklı kuyruk üzerinden re-embedding tetikler.
- Kaynak başına ve makale toplamında yapılandırılabilir chunk sınırları uygulanır.
- Makale ve ek kaynakları round-robin interleave edilerek uzun bir kaynağın tüm bütçeyi tüketmesi önlenir.
- Her chunk `sourceType`, `attachmentId`, `sourceName` ve `sourceLocation` gibi provenance alanlarını taşır.
- `bge-m3` modeli 1024 boyutlu embedding üretir; boyut persistence öncesinde doğrulanır.
- Embedding'ler PostgreSQL `vector(1024)` kolonunda, cosine distance için HNSW indeksle saklanır.

İndeks işi hem fulltext hem semantic görünümü senkronize eder. Model, boyut veya chunking ayarı değiştiğinde türetilen semantic index profile değişir; başlangıç uzlaştırması ilgili makaleleri dirty işaretleyip dayanıklı kuyruğa alır. Önceki satırlar topluca silinmez, her makale yeni profile atomik olarak geçirilir; vector sorgusu farklı embedding modeline ait satırları hiçbir zaman birlikte skorlamaz. Yayından kaldırılan içeriklerin eski embedding'leri temizlenir.

## 2. Hybrid Retrieval

RAG yalnız semantic arama kullanmaz. `HybridRagRetriever` iki bağımsız aday kolu üretir:

1. **Lexical kol:** PostgreSQL Türkçe FTS ile makale/ek eşleşmeleri.
2. **Semantic kol:** pgvector cosine similarity ile chunk eşleşmeleri.

Kollardan biri geçici olarak hata verirse diğeriyle devam edilebilir. Makale seviyesindeki listeler varsayılan `k=60`, lexical `0.4` ve semantic `0.6` ağırlıklarıyla Reciprocal Rank Fusion üzerinden birleştirilir.

Lexical eşleşmenin gerçek pasajı semantic sonuçta yoksa makale gövdesinden veya çıkarılmış ek metninden provenance-bearing sentetik chunk eklenir. Ardından:

- Yerel ve deterministik chunk reranker; retrieval skoru, query coverage, başlık/kaynak coverage ve tam ifade sinyalini birleştirir.
- Aynı makaledeki yüksek Jaccard benzerliğine sahip yakın kopyalar bastırılır.
- Sonuçlar makaleler arasında fair interleave edilir.
- `RagMaxChunksPerArticle` sınırı tek bir makalenin bağlamı ele geçirmesini önler.
- Etiket, yazar, içerik türü ve `onlyOwnContent` filtreleri retrieval içinde uygulanır ve makale metadata lookup'ında tekrar doğrulanır.

Varsayılan RAG semantic eşiği 0.3'tür. Bu değer liste tipi semantic aramadaki 0.5 eşiğinden daha düşüktür; çünkü genel soruların cosine skoru düşük olabilir ve nihai katman yetersiz kanıtta fail-closed davranır.

## 3. Dar ve Geniş Soru Yolları

Soru; özetleme, karşılaştırma, listeleme veya tüm corpus'u kapsama niyeti taşıyorsa geniş yol seçilir. Anahtar kelime listesi yapılandırmayla genişletilebilir.

### Dar Soru

Dar yol, en yüksek sıralı chunk'ları en fazla üç farklı kaynak makaleden ve varsayılan 8.000 kelimelik context bütçesi içinde tek LLM çağrısına paketler. `IRagContextBuilder` tam kopyaları bastırır, kaynak çeşitliliğini ve bütçeyi uygular, source delimiter/prompt-injection sınırlarını güçlendirir ve evidence kimliğini korur. Grounding'e verilen pasaj, LLM'e gerçekten gönderilen kırpılmış pasajla aynıdır. Bir makaleden birden fazla ilgili chunk kullanılabilir. Bu yol düşük gecikmeli, odaklı cevaplar içindir.

## Kullanıcı Geri Bildirimi

Arama ekranındaki yardımcı oldu/olmadı düğmeleri geri bildirimi yalnız kullanıcının kendi RAG `searchQueryId` kaydına bağlar. Kayıt; trace, prompt sürümü, semantic index profile ve grounding durumunu taşır. Üretilen yanıt yeniden saklanmaz; karşılaştırma için yalnız SHA-256 fingerprint tutulur. Böylece kalite regresyonları yapılandırma sürümleriyle ilişkilendirilebilirken gereksiz hassas metin kopyası oluşmaz.

### Geniş Soru

Geniş yol daha büyük aday havuzunu batch'lere böler:

1. Map çağrıları her batch'teki ilgili gerçekleri kanıt kimlikleriyle çıkarır.
2. Map çağrıları yapılandırılabilir bounded parallelism ile yürür.
3. Reduce çağrısı başarılı map notlarını tek yanıtta birleştirir ve mevcut `[S#]` atıflarını korur.

Tekil map batch'leri veya reduce aşaması başarısız olursa başarılı parçalar kaybedilmez. Sonuç `partialResult=true` ve açıklayıcı uyarılarla dönebilir. İstek bütçesi nedeniyle adaylar kırpılırsa bu da uyarı olarak belirtilir.

## 4. Yapılandırılmış Üretim ve Fail-Closed Doğrulama

Chat modeli `qwen2.5vl:7b`, temperature 0 ve alanları zorunlu kılan açık bir JSON şemasıyla çalışır. Modelden serbest metin yerine claim listesi, her claim için evidence kimlikleri ve yetersiz bağlam işareti istenir. Model JSON nesnesinin çevresine kod bloğu, kısa açıklama veya düşünme etiketi eklerse yalnızca içindeki eksiksiz ve sözleşmeye uygun JSON nesnesi alınır; çevre metni yok sayılır ve kullanıcıya gösterilmez. Sağlayıcı structured-output seçeneğini yok sayıp `[S1]` biçiminde kesin atıflar içeren düz metin döndürürse yalnızca atıflı bölümler deterministik olarak claim'e çevrilir ve normal kanıt kontrollerinden geçirilir; atıfsız serbest metin hiçbir zaman kurtarılmaz veya gösterilmez. Yapılandırılmış üretim tümüyle başarısız olduğunda veya modelin bütün claim'leri grounding kontrolünde elendiğinde sorgu terimleriyle örtüşen cümleler doğrudan secret/talimat güvenlik filtresinden geçmiş doğrulanabilir kanıt pasajlarından seçilir ve bilinen evidence kimlikleriyle, değiştirilmeden `extractive_fallback`/partial result olarak döndürülür. Cümleler kanıttan birebir alındığı için lexical, sayı ve negation desteği yapısal olarak korunur. İlgili güvenli cümle yoksa sistem yine fail-closed reddeder.

Her kaynak bloğu sabit bir `[S1]`, `[S2]` benzeri kimlik taşır. Model çıktısı kullanıcıya doğrudan verilmez; `RagCitationValidator` şu kontrolleri uygular:

- Evidence kimliği gerçekten sağlanan kaynaklar arasında mı?
- Claim, her atıflı kanıttaki yerel cümle veya contrast-separated clause pencerelerinden en az biriyle yeterli lexical desteğe sahip mi? Kanıt pasajları tek metinde birleştirilmez; ilgisiz bir cümledeki olumsuzluk başka bir claim'in polarity sonucunu değiştiremez.
- Claim'deki sayılar kanıtla uyumlu mu?
- Claim ile kanıt arasında olumlu/olumsuz anlam çelişkisi var mı?
- Atıfsız veya doğrulanamayan claim var mı?

Kullanıcıya görünen yanıt yalnız doğrulamayı geçen claim'lerden yeniden oluşturulur. Böylece düzgün görünen fakat dayanağı olmayan model metni cevap içine sızamaz. Yanıt; `sources`, provenance-bearing `evidence`, citation ID coverage, claim support coverage, grounding durumu, partial/refusal bilgisi ve uyarıları birlikte taşır. Evidence, prompt içi `sourceId` yanında stabil `chunkId`, yetki kontrollü `canonicalUrl` ve PDF provenance'ı varsa `pageNumber` döndürür; lexical sentetik passage kimliği içerik ve provenance'dan deterministik üretilir. Arayüz citation kimliği geçerliliğini ve gerçek claim desteğini ayrı oranlar olarak gösterir.

## 5. İçerik Güvenliği

Portal içeriği güvenilir talimat değil, güvenilmeyen veri olarak ele alınır. Prompt injection ve sır sızıntısına karşı defense-in-depth kontroller uygulanır:

- Kaynak metindeki portal key, bearer token, JWT, AWS access-key ID ve atanmış secret/token/password değerleri prompt öncesinde redakte edilir.
- `source` delimiter dizileri nötralize edilir; içerik kendi bloğunu kapatamaz.
- Riskli talimat kalıpları işaretlenir ve modele `SECURITY-RISK` metadata'sı verilir.
- Sistem prompt'u kaynak içindeki komutları izlemeyi, tool çalıştırmayı, URL ziyaretini, rol değiştirmeyi veya credential açıklamayı açıkça yasaklar.
- Detection içeriği sessizce silen bir güvenlik kanıtı değildir; risk sinyali ve ek koruma katmanıdır.

## 6. Dayanıklılık ve Kaynak Kontrolü

`RagResilience` yapılandırması aşağıdaki kontrolleri merkezileştirir:

- Process-wide concurrency bulkhead ve sınırlı queue wait.
- Tüm istek için toplam süre bütçesi.
- Retrieval, generation ve reduce için ayrı typed timeout'lar.
- Yalnız transient AI hatalarında bounded retry.
- Ardışık AI hatalarında geçici circuit breaker.
- Geniş sorularda bounded map parallelism.

Desteklenen production topolojisi tek backend instance olduğu için bu state process içindedir. Kapasite dolu, circuit açık veya timeout durumları ayrı hata türleri ve metrik etiketleriyle görünür hale gelir.

## 7. Gözlemlenebilirlik ve Kalite Kapısı

RAG istekleri privacy-safe query fingerprint ile loglanır; ham kullanıcı sorusu loglanmaz. `kp_rag_*` metrikleri istek sonucu, stage hataları/gecikmeleri, aday/context boyutu, LLM çağrıları, refusal, partial sonuç, citation coverage ve aktif istekleri kapsar. Trace'ler isteğe bağlı OTLP exporter üzerinden gönderilebilir.

- Runtime görünümü: `GET /api/search/rag-observability`
- Prometheus alarmları: `ops/prometheus/rag-alerts.yml`
- Grafana dashboard: `ops/grafana/rag-overview.json`
- Ölçülebilir hedefler: `specs/rag-slo.md`

Yönetici arayüzündeki RAG evaluation alanı dinamik golden dataset'ler ve eşikler tanımlar. Çalıştırmalar dataset, config, model ve prompt snapshot'larını saklar; Recall, MRR, NDCG, fact/citation/grounding/refusal/safety/latency metrikleri üretir. CI, gerçek PostgreSQL/pgvector fidelity kapısına ek olarak canlı Ollama golden-dataset kalite kapısını zorunlu çalıştırır.

## Önemli Yapılandırmalar

Başlıca ayarlar `backend/appsettings.json` içindedir:

- `Ollama:RagCandidateLimit` / `RagBroadCandidateLimit`
- `Ollama:RagMaxChunksPerArticle` / `RagMaxContextWords`
- `Ollama:RagMapReduceBatchChunks` / `RagMaxOutputTokens`
- `Ollama:RagRrfK` / `RagLexicalWeight` / `RagSemanticWeight`
- `Ollama:RagDuplicateThreshold` / `RagMinSimilarityScore`
- `Ollama:ChunkTargetWords` / `ChunkOverlapWords` / `ChunkingVersion`
- `RagResilience:*` timeout, budget, retry, parallelism ve circuit breaker ayarları

Bu değerler değiştirilirken latency, recall, citation coverage, refusal oranı ve hata bütçesi birlikte değerlendirilmelidir. Değişiklik sonrası PostgreSQL fidelity testleri ve canlı RAG kalite kapısı çalıştırılmalıdır.
