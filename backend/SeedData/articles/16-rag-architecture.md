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
Kullanıcı sorusu → rewrite / filtre / seçici decomposition
    └──────────────→ alt sorgu başına lexical + semantic retrieval
                              │
                              ▼
                 query fusion + RRF + rerank + authority/freshness
                              │
                  seçici parent/komşu genişletme
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

Sorgudan önce `RagQueryUnderstandingService`, LLM maliyeti oluşturmadan explicit `#/@/##` ile `tag:/author:/type:` filtrelerini ayırır, yapılandırılmış acronym/synonym sözlüğünü genişletir ve yalnız karşılaştırma/bileşik soruları bounded alt sorgulara böler. Her alt sorgu aynı ACL filtresiyle çalışır; sonuçlar yeniden fusion ile birleşir. Güncellik sinyali exponential half-life, otorite sinyali içerik türü ve onay durumu üzerinden merkezi ayarlardan gelir; relevance ana sinyal olmaya devam eder.

Yüksek skorlu bir child chunk `section/page/sheet/...:chunk:N` provenance'ı taşıyorsa yapılandırılmış sayıdaki önceki/sonraki child aynı parent içinde eklenebilir. Genişletme published ve metadata-filter recheck'inden sonra çalışır; başka makaleye, eke, kaynağa veya parent'a geçmez. Varsayılan yerel reranker her zaman hazırdır. Opsiyonel external cross-encoder yalnız açıkça etkinleştirilir, candidate/metin/timeout sınırları kullanır ve hata veya geçersiz yanıtta yerel sonuca döner.

Varsayılan RAG semantic eşiği 0.3'tür. Bu değer liste tipi semantic aramadaki 0.5 eşiğinden daha düşüktür; çünkü genel soruların cosine skoru düşük olabilir ve nihai katman yetersiz kanıtta fail-closed davranır.

## 3. Dar ve Geniş Soru Yolları

Soru; özetleme, karşılaştırma, listeleme veya tüm corpus'u kapsama niyeti taşıyorsa geniş yol seçilir. Anahtar kelime listesi yapılandırmayla genişletilebilir.

### Dar Soru

Dar yol, en yüksek sıralı chunk'ları varsayılan olarak en fazla on farklı kaynak makaleden ve 8.000 kelimelik context bütçesi içinde tek LLM çağrısına paketler. Retriever ve `IRagContextBuilder` sonuçları makaleler arasında yeniden interleave eder. Builder ilk turda her farklı makaleye eşit bir kelime payı ayırır; bu nedenle lexical fallback'ten gelen çok uzun tek bir pasaj bütün bağlamı ele geçiremez. Farklı kaynakların ilk pasajları yerleştirildikten sonra kalan bütçe aynı makalelerin ek pasajlarında kullanılabilir. Böylece kullanıcı on ayrı arama sonucunu tek tek okumadan, en ilgili sonuçların ortak bilgisini tek yanıtta alabilir. `IRagContextBuilder` ayrıca tam kopyaları bastırır, source delimiter/prompt-injection sınırlarını güçlendirir ve evidence kimliğini korur. Grounding'e verilen pasaj, LLM'e gerçekten gönderilen kırpılmış pasajla aynıdır. Bu yol düşük gecikmeli, odaklı cevaplar içindir.

### Geniş Soru

Geniş yol daha büyük aday havuzunu batch'lere böler:

1. Map çağrıları her batch'teki ilgili gerçekleri kanıt kimlikleriyle çıkarır.
2. Map çağrıları yapılandırılabilir bounded parallelism ile yürür.
3. Reduce çağrısı başarılı map notlarını tek yanıtta birleştirir ve mevcut `[S#]` atıflarını korur.

Tekil map batch'leri veya reduce aşaması başarısız olursa başarılı parçalar kaybedilmez. Sonuç `partialResult=true` ve açıklayıcı uyarılarla dönebilir. İstek bütçesi nedeniyle adaylar kırpılırsa bu da uyarı olarak belirtilir.

## Kullanıcı Geri Bildirimi

Arama ekranındaki yardımcı oldu/olmadı düğmeleri ve isteğe bağlı negatif neden yalnız kullanıcının kendi RAG `searchQueryId` kaydına bağlanır. Kayıt; trace, prompt/retrieval sürümü, reranker kimliği, semantic index profile ve grounding durumunu taşır. Üretilen yanıt yeniden saklanmaz; yalnız SHA-256 fingerprint tutulur. Evaluation ekranı son 30 günün helpful oranını, nedenlerini, grounding ve configuration cohort'larını golden dataset metriklerinin yanında gösterir.

## 4. Yapılandırılmış Üretim ve Fail-Closed Doğrulama

Chat modeli `qwen2.5vl:7b`, temperature 0 ve alanları zorunlu kılan açık bir JSON şemasıyla çalışır. Üretim sözleşmesi bir sonuç listesi veya kaynak özeti istemez: ilk claim soruya doğrudan ve kısa cevabı verir; sonraki claim'ler kanıt varsa çalışma biçimi, gerekçe, pratik anlam, adımlar, sınırlar, varsayılanlar, istisnalar ve trade-off'ları açıklar. Model kaynakları doküman doküman tekrar etmek yerine ortak bilgiyi sentezler ve kendi cümleleriyle özetler; teknik ad, yapılandırma anahtarı, sayı, komut ve politika ifadelerinde gerekli kesinliği korur. Açıklama serbest çıkarım izni değildir: her olgusal cümle kendi evidence kimliğini taşır, kaynak uyuşmazlıkları sessizce birleştirilmez ve kanıtsız neden/sonuç üretilmez. Backend ilk doğrulanmış claim'i hızlı cevap paragrafı, kalan claim'leri `Açıklama` maddeleri olarak sunar.

Modelden serbest metin yerine kullanıcıya gösterilecek sırada tam ve doğal cümlelerden oluşan claim listesi, her claim için evidence kimlikleri ve yetersiz bağlam işareti istenir. Model JSON nesnesinin çevresine kod bloğu, kısa açıklama veya düşünme etiketi eklerse yalnızca içindeki eksiksiz ve sözleşmeye uygun JSON nesnesi alınır; çevre metni yok sayılır ve kullanıcıya gösterilmez. Sağlayıcı structured-output seçeneğini yok sayıp `[S1]` biçiminde kesin atıflar içeren düz metin döndürürse yalnızca atıflı bölümler deterministik olarak claim'e çevrilir ve normal kanıt kontrollerinden geçirilir; atıfsız serbest metin hiçbir zaman kurtarılmaz veya gösterilmez. Yapılandırılmış üretim başarısız olduğunda, bütün claim'ler grounding kontrolünde elendiğinde veya broad/map-reduce özeti `RagBroadMinimumClaims` kapsam hedefinin altında kaldığında `RagGroundingRepairEnabled` açıksa aynı kanıt, reddedilen taslak ve deterministik doğrulama geri bildirimiyle bir kez sınırlı düzeltme çağrısı yapılır. Broad onarımında ilk üretim ve onarım turlarındaki destekli, benzersiz claim'ler birleştirilir; onarım yine kısa kalırsa farklı doğrulanmış kanıt cümleleri `extractive_enrichment` olarak eklenir. Düzeltme çıktısı da bütün normal evidence kimliği, lexical destek, sayı, negation ve başlık kontrollerinden geçmek zorundadır; güvenlik filtresini bypass etmez. Bu ikinci çıktı da reddedilirse sorgu terimleriyle örtüşen cümleler doğrudan secret/talimat güvenlik filtresinden geçmiş doğrulanabilir kanıt pasajlarından seçilir ve bilinen evidence kimlikleriyle, değiştirilmeden `extractive_fallback`/partial result olarak döndürülür. Cümleler kanıttan birebir alındığı için lexical, sayı ve negation desteği yapısal olarak korunur. İlgili güvenli cümle yoksa sistem yine fail-closed reddeder.

Her kaynak bloğu sabit bir `[S1]`, `[S2]` benzeri kimlik taşır. Kısa anahtar kelime, ürün adı, yapılandırma anahtarı veya yapılandırma anahtarını soran doğrudan tanım sorgularında modelden iki katmanlı yanıt istenir: ilk claim kaynakla uyumlu kısa özeti korur, sonraki claim'ler aynı kanıttaki amaç, çalışma biçimi, varsayılan, sınır ve fallback ayrıntılarını açıklar. `Reranking:External nedir?` gibi iki noktayla ayrılmış yapılandırma yollarında kaynağın `Reranking:External, ...` biçimindeki kompakt açıklaması, dilbilgisel `-dır` yüklemi bulunmasa da yeterli açıklayıcı terim taşıyorsa geçerli tanım kabul edilir. Doğrulayıcı noktalama biçiminden bağımsız olarak, kanıtta birden fazla ayrı ilgili bilgi bulunduğunda ilk destekli claim'i özet paragrafı olarak tutar ve en az bir ek destekli açıklama claim'ini yeni paragrafta zorunlu kılar; bu durumda tek claim'li yanıt düzeltme akışını tetikler. `Nedir`/`what is` gibi soru kalıpları konu eşleşmesi sayılmaz; soru biçimindeki başlıklar ve farklı doküman başlıkları açıklama claim'i olamaz. Yapılandırma açıklaması istenen konuya ait terim taşımalı veya doğrulanmış tanımla aynı evidence öğesindeki devam cümlesi olmalıdır. Model düzeltme çağrısında da yalnız aynı destekli özeti döndürürse özet kaybedilmez; sorguyla örtüşen, güvenlik kontrolünden geçmiş doğrulanmış kanıt cümleleri `extractive_enrichment` olarak ikinci paragrafa eklenir ve sonuç partial olarak işaretlenir. Bütün model claim'leri elenirse iki noktalı yapılandırma sorgularında düzleştirilmiş kanıt içinden tam anahtarın girdisi bir sonraki yapılandırma anahtarına kadar güvenle çıkarılır; sorguyla örtüşen kısa doğrulanmış pasajlar açıklama paragrafı olarak eklenir ve sonuç `extractive_fallback` olur. Kanıtta yalnız tek ilgili bilgi varsa kaynak dışı ayrıntı üretmemek için tek claim kabul edilebilir. Model çıktısı kullanıcıya doğrudan verilmez; `RagCitationValidator` şu kontrolleri uygular:

- Model birden fazla cümleyi tek claim alanına koysa bile cümleler atomik olarak ayrılır; başlık veya özet, aynı claim içindeki destekli bir cevap cümlesine tutunarak doğrulamayı geçemez.
- Saf tanım sorularında (`X nedir?`, `What is X?`) claim'in kaynaktaki X tanımını doğrudan vermesi gerekir. Doküman başlığı, bölüm adı veya rehberin ne anlattığını söyleyen katalog metni cevap kabul edilmez. Tanım portal kaynağından alınır; modelin genel bilgisiyle düzeltilmez.
- Evidence kimliği gerçekten sağlanan kaynaklar arasında mı?
- Claim, her atıflı kanıttaki yerel cümle veya contrast-separated clause pencerelerinden en az biriyle yeterli lexical desteğe sahip mi? Eşleşme Türkçe çekim ekleri için yalnız uzun ve baskın ortak kökü kontrollü biçimde kabul eder; sayı ve olumsuzluk kontrolleri ayrıca korunur. Kanıt pasajları tek metinde birleştirilmez; ilgisiz bir cümledeki olumsuzluk başka bir claim'in polarity sonucunu değiştiremez.
- Claim'deki sayılar kanıtla uyumlu mu?
- Claim ile kanıt arasında olumlu/olumsuz anlam çelişkisi var mı?
- Claim yalnız makale başlığını tekrar ederek gerçek bir önerme sunmadan cevap görünümü mü veriyor?
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
- Yetkili aday/context debug: `GET /api/search/rag-debug?q=...` (session-admin, LLM çağrısı yok)
- Prometheus alarmları: `ops/prometheus/rag-alerts.yml`
- Grafana dashboard: `ops/grafana/rag-overview.json`
- Ölçülebilir hedefler: `specs/rag-slo.md`

Yönetici arayüzündeki RAG evaluation alanı dinamik golden dataset'ler ve eşikler tanımlar. Çalıştırmalar dataset, config, model ve prompt snapshot'larını saklar; Recall, MRR, NDCG, fact/citation/grounding/refusal/safety/latency metrikleri üretir. CI, gerçek PostgreSQL/pgvector fidelity kapısına ek olarak canlı Ollama golden-dataset kalite kapısını zorunlu çalıştırır.

## Önemli Yapılandırmalar

Başlıca ayarlar `backend/appsettings.json` içindedir:

- `Ollama:RagCandidateLimit` / `RagBroadCandidateLimit`
- `Ollama:RagSourceLimit` dar yolda tek çağrıda değerlendirilecek farklı makale sayısı; varsayılan 10, güvenli üst sınır 20
- `Ollama:RagMaxChunksPerArticle` / `RagMaxContextWords`
- `Ollama:RagMapReduceBatchChunks` / `RagMaxOutputTokens` / `RagBroadMinimumClaims`
- `Ollama:RagGroundingRepairEnabled` reddedilen üretim için tek seferlik, aynı kanıta bağlı düzeltme çağrısı
- `Ollama:RagRrfK` / `RagLexicalWeight` / `RagSemanticWeight`
- `Ollama:RagDuplicateThreshold` / `RagMinSimilarityScore`
- `Ollama:QueryUnderstanding:*` rewrite, synonym ve decomposition ayarları
- `Ollama:ContextExpansion:*` parent-komşu genişletme sınırları
- `Ollama:Ranking:*` freshness, approval ve content-type authority ağırlıkları
- `Reranking:External:*`, retrieval sonrasında aday pasajları harici bir cross-encoder ile yeniden sıralayan isteğe bağlı katmandır. `Enabled=false` ile varsayılan olarak kapalıdır; kapalıyken veya geçerli bir `Endpoint` verilmediğinde yerel deterministik sıralama aynen kullanılır. Etkinleştirildiğinde en fazla `MaxCandidates` adayın her birinden `MaxDocumentCharacters` kadar metin, yapılandırılan `Model` ve sorguyla endpoint'e gönderilir; `ApiKey` varsa Bearer kimlik doğrulaması uygulanır. Çağrı `TimeoutSeconds` ile sınırlandırılır. Geçerli harici skorlar `ScoreWeight` oranında yerel skorlarla birleştirilir; timeout, HTTP hatası veya geçersiz/boş yanıt halinde istek bozulmaz ve yerel sıralama sonucuna geri dönülür. Varsayılanlar sırasıyla 8 saniye, 50 aday, aday başına 4.000 karakter ve 0,8 harici skor ağırlığıdır.
- `Ollama:ChunkTargetWords` / `ChunkOverlapWords` / `ChunkingVersion`
- `RagResilience:*` timeout, budget, retry, parallelism ve circuit breaker ayarları

Bu değerler değiştirilirken latency, recall, citation coverage, refusal oranı ve hata bütçesi birlikte değerlendirilmelidir. Değişiklik sonrası PostgreSQL fidelity testleri ve canlı RAG kalite kapısı çalıştırılmalıdır.
