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

Knowledge Portal Bilgi Asistanı'nın RAG (Retrieval-Augmented Generation) akışı, yayınlanmış portal içeriğinden kanıt getirip bu kanıtlara dayalı bir yanıt üretir. RAG, Search'ün bir modu değildir; REST'te `/api/assistant`, MCP'de `ask_knowledge` üzerinden çalışır. Modelin genel bilgisini doğruluk kaynağı kabul etmez. Yeterli veya doğrulanabilir kanıt yoksa yanıt uydurmak yerine açıkça yetersiz bağlam sonucu döner.

RAG akışının ana adımları:

```text
Makale + ekler
    │
    ▼
Dayanıklı indeks kuyruğu → parent + child embedding + FTS
                                   │
Kullanıcı sorusu → rewrite / filtre / seçici decomposition
    └──────────────→ alt sorgu başına lexical + semantic retrieval
                              │
                              ▼
                 query fusion + RRF + rerank + authority/freshness
                              │
                  ACL-safe child → parent çözümleme
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

Kanonik Markdown okunabilir düz metne çevrilir. Makale gövdesi ile `includeInIndex=true` olan ve metni çıkarılabilen her ek ayrı kaynak kabul edilir:

- Makale Markdown'ı başlık/bölüm sınırları korunarak; ekler parser'ın page/sheet/slide konumları korunarak gerçek bir parent-child hiyerarşisine ayrılır. Parent hiçbir zaman başka başlığa, sayfaya, sheet'e veya slide'a geçmez. Paragraf, liste, tablo ve kod blokları hedef bütçeye sığıyorsa bölünmez; aşırı büyük tek bloklar kontrollü kayan pencereye düşer.
- İndeks worker'ı FTS ve embedding'den önce indeks dahilindeki ekler için tek canonical extraction üretir. Gövdeye de aktarılan `.md` gibi bir dosyada `includeInIndex=false` seçildiğinde ek indirilebilir kalır, fakat yinelenen FTS terimleri, semantic chunk'lar ve RAG kanıtları üretmez. DOCX/XLSX/PPTX/CSV tabloları GFM Markdown'dır; büyük tablolar child sınırında bölünürken kolon başlığı tekrarlanır. XLSX sparse hücre konumu ile formül/hesaplanmış değer kaybolmaz.
- Standalone PNG/JPEG/WebP/GIF ile PDF/DOCX/PPTX içindeki bounded görseller yerel multimodal model tarafından olgusal açıklama, literal OCR, Markdown tablo ve şema/graf ilişkilerine dönüştürülür. Görsel içindeki talimatlar güvenilmeyen veri kabul edilir. Sonuç bir kez attachment extraction cache'ine yazılır ve hem FTS hem embedding aynı metni kullanır.
- Karmaşık/taranmış belgelerde isteğe bağlı Unstructured `hi_res` endpoint'i tablo HTML'ini Markdown'a ve elementleri page provenance'a dönüştürür. Bu dış aktarım varsayılan kapalıdır; hata halinde native parser'a döner, `Required=true` ise iş fail/retry olur.
- Eklerden çıkarılacak metin `FileStorage:MaxExtractedCharacters` ile sınırlandırılır. Sınır, çıkarılan karakter sayısı ve truncation durumu kalıcı metadata'dır; sınır değişince sonraki indeks geçişi cache'i yeniden üretir ve storage teşhisleri kırpılan dosyaları sayar.
- Varsayılan parent hedefi yaklaşık 1000 kelime, searchable child hedefi 220 kelime ve child örtüşmesi 40 kelimedir. Yalnız child metinleri embed edilir; parent metinleri `article_chunk_parents` içinde bir kez saklanır ve her child `parent_chunk_id` taşır. `ParentChunkTargetWords`, `ChildChunkTargetWords`, `ChildChunkOverlapWords` ve `ChunkingVersion` yapılandırılabilir. Bütün sınırlar semantic index profile'a katıldığı için değişiklik dayanıklı kuyruk üzerinden atomik re-embedding tetikler.
- Kaynak başına ve makale toplamında yapılandırılabilir chunk sınırları uygulanır.
- Makale ve ek kaynakları round-robin interleave edilerek uzun bir kaynağın tüm bütçeyi tüketmesi önlenir.
- Her parent ve child `sourceType`, `attachmentId`, `sourceName` ve `sourceLocation` provenance alanlarını taşır; child konumu parent ve child sırasını birlikte içerir.
- `bge-m3` modeli 1024 boyutlu embedding üretir; boyut persistence öncesinde doğrulanır.
- Embedding'ler PostgreSQL `vector(1024)` kolonunda, cosine distance için HNSW indeksle saklanır.

İndeks işi hem fulltext hem semantic görünümü senkronize eder. Model, boyut veya chunking ayarı değiştiğinde türetilen semantic index profile değişir; başlangıç uzlaştırması ilgili makaleleri dirty işaretleyip dayanıklı kuyruğa alır. Önceki satırlar topluca silinmez, her makale yeni profile atomik olarak geçirilir; vector sorgusu farklı embedding modeline ait satırları hiçbir zaman birlikte skorlamaz. Yayından kaldırılan içeriklerin eski embedding'leri temizlenir.

## 2. Hybrid Retrieval

RAG yalnız semantic arama kullanmaz. `HybridRagRetriever` iki bağımsız aday kolu üretir:

1. **Lexical kol:** PostgreSQL Türkçe FTS ile makale/ek eşleşmeleri.
2. **Semantic kol:** pgvector cosine similarity ile chunk eşleşmeleri.

Kollardan biri geçici olarak hata verirse diğeriyle devam edilebilir. Makale seviyesindeki listeler varsayılan `k=60`, lexical `0.4` ve semantic `0.6` ağırlıklarıyla Reciprocal Rank Fusion üzerinden birleştirilir.

Lexical eşleşmeler de mümkün olduğunda kalıcı searchable child kayıtlarını kullanır. Böylece BM25/FTS ve vector yolları aynı child→parent kimliğinde birleşir; semantic sonuçta bulunmayan lexical child da doğru parent bağlamına genişleyebilir. Henüz semantic indeksi olmayan geçiş kayıtlarında makale metniyle sınırlı sentetik fallback korunur. Ardından:

- Yerel ve deterministik chunk reranker; retrieval skoru, query coverage, başlık/kaynak coverage ve tam ifade sinyalini birleştirir.
- Aynı makaledeki yüksek Jaccard benzerliğine sahip yakın kopyalar bastırılır.
- Sonuçlar makaleler arasında fair interleave edilir.
- `RagMaxChunksPerArticle` sınırı tek bir makalenin bağlamı ele geçirmesini önler.
- Etiket, yazar, içerik türü ve `onlyOwnContent` filtreleri retrieval içinde uygulanır ve makale metadata lookup'ında tekrar doğrulanır.

Sorgudan önce `KnowledgeQueryScopeService`, LLM maliyeti oluşturmadan explicit `#etiket`, `@yazar` ve generic `+kategori:değer` filtrelerini ayırır. `RagQueryUnderstandingService` kalan metinde yapılandırılmış acronym/synonym sözlüğünü genişletir ve yalnız karşılaştırma/bileşik soruları bounded alt sorgulara böler. Generic sınıflandırmalar inline token, REST/Assistant `facets` ve MCP `scope.facets` üzerinden canonical değerlerle doğrulanır; varsayılan `filter` davranışındaki explicit facet'ler retrieval sırasında kaynak modele girmeden önce uygulanır, `none` kategorileri AI kapsamına alınmaz. Aynı kategoride OR, kategoriler arasında AND kullanılır; bilinmeyen değer kapsamı genişletmez. Her alt sorgu aynı ACL ve facet filtresiyle çalışır; sonuçlar yeniden fusion ile birleşir. Güncellik sinyali exponential half-life, otorite sinyali makalenin aktif kategorilerdeki aktif lookup atamalarının en yüksek `lookup_values.authority_weight` değeri ve onay durumundan gelir; relevance ana sinyal olmaya devam eder. Henüz generic ataması olmayan eski kayıtlar `content_type` lookup ağırlığına geri döner. Böylece arama sıralaması ile yönetişim/reliability çıktısı aynı generic otorite kaynağını kullanır; kategori veya değer adlarına özel ikinci bir config matrisi yoktur.

Yüksek skorlu child, reranking ve published/metadata ACL recheck'inden sonra `parent_chunk_id` ile yapısal parent'ına çözülür. Aynı parent'a isabet eden birden fazla child tek parent context'e deduplicate edilir. Parent sorgusu yalnız yetkilendirilmiş makale kimlikleriyle çalışır; başka makaleye, eke veya provenance sınırına geçemez. Rolling geçiş sırasında parent FK'sı olmayan eski satırlar için aynı bölüm içindeki sınırlı komşu fallback'i geçici olarak korunur. Varsayılan yerel reranker her zaman hazırdır. Opsiyonel external cross-encoder yalnız açıkça etkinleştirilir; yerel olarak en iyi sıralanmış bounded adayları işler, provider skorlarını doğrulayıp normalize eder ve timeout, overload, circuit, HTTP ya da şema hatasında yerel sonuca döner.

Varsayılan RAG semantic eşiği 0.3'tür. Bu değer liste tipi semantic aramadaki 0.5 eşiğinden daha düşüktür; çünkü genel soruların cosine skoru düşük olabilir ve nihai katman yetersiz kanıtta fail-closed davranır.

## 3. Dar ve Geniş Soru Yolları

Kullanıcı `compact`, `balanced` veya `comprehensive` cevap profili seçebilir; seçim tarayıcıda tutulur ve her istekte backend'e gönderilir. `compact` yalnız dar yolu, `comprehensive` geniş/map-reduce yolunu seçer. Profil gönderilmezse varsayılan `balanced` kullanılır; özetleme, karşılaştırma, listeleme, “kapsamlı/detaylı” anlatım veya tüm corpus'u kapsama niyeti otomatik olarak `comprehensive` profile yükseltilir. Anahtar kelime listesi yapılandırmayla genişletilebilir. Etkin profil cevapta ve MCP sonucunda görünür, semantic answer cache kimliğine dahildir.

### Dar Soru

Uygulamanın token preflight hesabındaki 32.768 `RagModelContextTokens` değeri her Ollama chat çağrısına açıkça `num_ctx` olarak gönderilir; provider penceresi ile yerel bütçe böylece ayrışmaz. Çıktı rezervi varsayılan olarak 4.096 token'dır.

Dar yol kaynak sayısını sabit tutmaz. Kısa ve doğrudan sorgu varsayılan olarak en az üç güçlü makaleyle çalışır; query token karmaşıklığı, decomposition ve açıklama niyeti arttıkça sınır en fazla ona yükselir. En iyi makalenin skorunun varsayılan %55'inin altında kalan marjinal kaynaklar minimum güvenlik tabanı dışında bağlama alınmaz. Retriever ve `IRagContextBuilder` sonuçları makaleler arasında yeniden interleave eder.

Context kelime sayısıyla değil token bütçesiyle yönetilir. Varsayılan 12.000 context token'ı; 32.768 model penceresinden çıktı ve sistem prompt rezervleri çıkarılarak ayrıca sınırlandırılır. Ollama gerçek token sayısını yalnız yanıt sonrasında verdiği için `RagTokenCounter` Qwen/Unicode için muhafazakâr preflight tahmini yapar ve sonraki istekleri `ChatResponse.Usage.InputTokenCount` ile kalibre eder. Builder ilk turda her farklı makaleye eşit token payı ayırır; lexical fallback'ten gelen çok uzun tek bir pasaj bütün bağlamı ele geçiremez. Farklı kaynakların ilk pasajları yerleştirildikten sonra kalan bütçe aynı makalelerin ek pasajlarında kullanılabilir. `IRagContextBuilder` ayrıca tam kopyaları bastırır, source delimiter/prompt-injection sınırlarını güçlendirir ve evidence kimliğini korur. Grounding'e verilen pasaj, LLM'e gerçekten gönderilen kırpılmış pasajla aynıdır.

Her Asistan cevabı `tokenUsage` içinde input, output ve toplam üretim token'larını döndürür; frontend toplamı cevap başlığında gösterir ve input/output kırılımını erişilebilir açıklamada sunar. Dar cevapta generation, geniş cevapta tüm map/reduce ve varsa grounding-repair çağrıları birlikte sayılır. Sağlayıcı kesin kullanım vermezse eksik sayılar aynı Unicode-duyarlı sayaçla tahmin edilir ve `estimated=true` işaretlenir. Semantik cevap önbelleği yeni chat üretimi yapmadığından önbellek isabetinde kullanım sıfırdır.

### Geniş Soru

Geniş yol daha büyük aday havuzunu batch'lere böler:

1. Map çağrıları her batch'teki ilgili gerçekleri kanıt kimlikleriyle çıkarır.
2. Map çağrıları yapılandırılabilir bounded parallelism ile yürür.
3. Reduce çağrısı başarılı map notlarını tek yanıtta birleştirir ve mevcut `[S#]` atıflarını korur.

Tekil map batch'leri veya reduce aşaması başarısız olursa başarılı parçalar kaybedilmez. Sonuç `partialResult=true` ve açıklayıcı uyarılarla dönebilir. İstek bütçesi nedeniyle adaylar kırpılırsa bu da uyarı olarak belirtilir.

## Kullanıcı Geri Bildirimi

Asistan ekranındaki yardımcı oldu/olmadı düğmeleri ve isteğe bağlı negatif neden yalnız kullanıcının kendi `interactionId` kaydına bağlanır. `assistant_interactions`; trace, prompt/retrieval sürümü, reranker kimliği, semantic index profile ve grounding durumunu taşır. Üretilen yanıt yeniden saklanmaz; yalnız SHA-256 fingerprint tutulur. Evaluation ekranı son 30 günün helpful oranını, nedenlerini, grounding ve configuration cohort'larını golden dataset metriklerinin yanında gösterir.

## 4. Yapılandırılmış Üretim ve Fail-Closed Doğrulama

Provider yalnız tek canonical claim nesnesi (`claims` ve `insufficientContext`) üretir; ayrı bir serbest metin `answer` alanı ürettirilmez. Backend doğrulanmış atomik claim'lerden kullanıcıya görünen Markdown yanıtı kurar. Bu, aynı cevabın iki kez üretilmesini kaldırır ve çıktı bütçesini gerçek kapsama ayırır. Legacy/fake çıktılardaki `answer` alanı yalnız geriye uyumluluk için okunabilir; doğruluk kaynağı değildir.

Chat modeli dinamik seçilir. `OllamaModelCatalogService`, `Ollama:BaseUrl` altındaki `GET /api/tags` yanıtından kurulu modelleri otomatik keşfeder ve destekleniyorsa `POST /api/show` içindeki `completion` capability'sini doğrular. Yapılandırılmış embedding modeli ile embedding-only model adları kullanıcı listesinden çıkarılır. Başarılı katalog kısa süreli cache'lenir; sağlayıcı geçici olarak erişilemezse son başarılı katalog, ilk açılışta ise `Ollama:ChatModel` fallback'i kullanılır ve arayüz uyarı gösterir. Admin `/settings/llm` üzerinden veritabanındaki varsayılanı değiştirir. Kullanıcı Bilgi Asistanı ekranından model seçer; bu seçim yalnız tarayıcı storage'ında tutulur, her Assistant isteğinde gönderilir ve veritabanına yazılmaz. Backend seçimi mevcut katalogla doğrular. Seçim yoksa admin varsayılanı, sonra `Ollama:ChatModel`/ilk keşfedilen model kullanılır; MCP ve sistem işleri admin varsayılanını kullanır. Embedding modeli ve attachment vision extraction profili etkilenmez. Semantic answer cache anahtarı etkin modeli içerdiği için farklı modellerin yanıtları paylaşılmaz.

Etkin chat modeli temperature 0 ve alanları zorunlu kılan açık bir JSON şemasıyla çalışır. Üretim sözleşmesi bir sonuç listesi veya kaynak özeti istemez: ilk claim soruya doğrudan ve kısa cevabı verir; sonraki claim'ler kanıt varsa çalışma biçimi, gerekçe, pratik anlam, adımlar, sınırlar, varsayılanlar, istisnalar ve trade-off'ları açıklar. Her claim zorunlu olarak `summary`, `explanation`, `step`, `constraint`, `exception` veya `conflict` rolü taşır. Backend bu rolleri yerelleştirilmiş Markdown bölümlerine dönüştürür; legacy veya extractive fallback claim'lerinde ilk claim deterministik olarak `summary` rolüne yükseltilir. Model kaynakları doküman doküman tekrar etmek yerine ortak bilgiyi sentezler ve kendi cümleleriyle özetler; teknik ad, yapılandırma anahtarı, sayı, komut ve politika ifadelerinde gerekli kesinliği korur. Açıklama serbest çıkarım izni değildir: her olgusal cümle kendi evidence kimliğini taşır ve kanıtsız neden/sonuç üretilmez.

Kaynak bloklarındaki approval, dinamik authority, review state, reliability ve update zamanı güvenilir server metadata'sıdır. Sayısal veya açık olumlu/olumsuz ifadeler aynı konu için çelişirse öncelik sırası onay → otorite → review state → reliability → update zamanıdır; bütün sinyaller eşitse sistem otomatik seçim yapmaz. Deterministik `conflictAssessment` yalnız sayı ve açık polarity uyuşmazlığını iddia eder; genel semantic contradiction tespiti yaptığını söylemez. Model her rakip kaynağın olgusunu ayrı `conflict` claim'i olarak üretir, böylece her cümle kendi kaynağına karşı bağımsız doğrulanabilir.

Modelden serbest metin yerine kullanıcıya gösterilecek sırada tam ve doğal cümlelerden oluşan claim listesi, her claim için evidence kimlikleri ve yetersiz bağlam işareti istenir. Model JSON nesnesinin çevresine kod bloğu, kısa açıklama veya düşünme etiketi eklerse yalnızca içindeki eksiksiz ve sözleşmeye uygun JSON nesnesi alınır; çevre metni yok sayılır ve kullanıcıya gösterilmez. Sağlayıcı structured-output seçeneğini yok sayıp `[S1]` biçiminde kesin atıflar içeren düz metin döndürürse yalnızca atıflı bölümler deterministik olarak claim'e çevrilir ve normal kanıt kontrollerinden geçirilir; atıfsız serbest metin hiçbir zaman kurtarılmaz veya gösterilmez. Yapılandırılmış üretim başarısız olduğunda, bütün claim'ler grounding kontrolünde elendiğinde veya broad/map-reduce özeti `RagBroadMinimumClaims` kapsam hedefinin altında kaldığında `RagGroundingRepairEnabled` açıksa aynı kanıt, reddedilen taslak ve deterministik doğrulama geri bildirimiyle bir kez sınırlı düzeltme çağrısı yapılır. Broad onarımında ilk üretim ve onarım turlarındaki destekli, benzersiz claim'ler birleştirilir; onarım yine kısa kalırsa farklı doğrulanmış kanıt cümleleri `extractive_enrichment` olarak eklenir. Düzeltme çıktısı da bütün normal evidence kimliği, lexical destek, sayı, negation ve başlık kontrollerinden geçmek zorundadır; güvenlik filtresini bypass etmez. Bu ikinci çıktı da reddedilirse sorgu terimleriyle örtüşen cümleler doğrudan secret/talimat güvenlik filtresinden geçmiş doğrulanabilir kanıt pasajlarından seçilir ve bilinen evidence kimlikleriyle, değiştirilmeden `extractive_fallback`/partial result olarak döndürülür. Cümleler kanıttan birebir alındığı için lexical, sayı ve negation desteği yapısal olarak korunur. İlgili güvenli cümle yoksa sistem yine fail-closed reddeder.

Her kaynak bloğu sabit bir `[S1]`, `[S2]` benzeri kimlik taşır. Kısa anahtar kelime, ürün adı, yapılandırma anahtarı veya yapılandırma anahtarını soran doğrudan tanım sorgularında modelden iki katmanlı yanıt istenir: ilk claim kaynakla uyumlu kısa özeti korur, sonraki claim'ler aynı kanıttaki amaç, çalışma biçimi, varsayılan, sınır ve fallback ayrıntılarını açıklar. `Reranking:External nedir?` gibi iki noktayla ayrılmış yapılandırma yollarında kaynağın `Reranking:External, ...` biçimindeki kompakt açıklaması, dilbilgisel `-dır` yüklemi bulunmasa da yeterli açıklayıcı terim taşıyorsa geçerli tanım kabul edilir. Doğrulayıcı noktalama biçiminden bağımsız olarak, kanıtta birden fazla ayrı ilgili bilgi bulunduğunda ilk destekli claim'i özet paragrafı olarak tutar ve en az bir ek destekli açıklama claim'ini yeni paragrafta zorunlu kılar; bu durumda tek claim'li yanıt düzeltme akışını tetikler. `Nedir`/`what is` gibi soru kalıpları konu eşleşmesi sayılmaz; soru biçimindeki başlıklar ve farklı doküman başlıkları açıklama claim'i olamaz. Yapılandırma açıklaması istenen konuya ait terim taşımalı veya doğrulanmış tanımla aynı evidence öğesindeki devam cümlesi olmalıdır. Model düzeltme çağrısında da yalnız aynı destekli özeti döndürürse özet kaybedilmez; sorguyla örtüşen, güvenlik kontrolünden geçmiş doğrulanmış kanıt cümleleri `extractive_enrichment` olarak ikinci paragrafa eklenir ve sonuç partial olarak işaretlenir. Bütün model claim'leri elenirse iki noktalı yapılandırma sorgularında düzleştirilmiş kanıt içinden tam anahtarın girdisi bir sonraki yapılandırma anahtarına kadar güvenle çıkarılır; sorguyla örtüşen kısa doğrulanmış pasajlar açıklama paragrafı olarak eklenir ve sonuç `extractive_fallback` olur. Kanıtta yalnız tek ilgili bilgi varsa kaynak dışı ayrıntı üretmemek için tek claim kabul edilebilir. Model çıktısı kullanıcıya doğrudan verilmez; `RagCitationValidator` şu kontrolleri uygular:

- Model birden fazla cümleyi tek claim alanına koysa bile cümleler atomik olarak ayrılır; başlık veya özet, aynı claim içindeki destekli bir cevap cümlesine tutunarak doğrulamayı geçemez.
- Saf tanım sorularında (`X nedir?`, `What is X?`) claim'in kaynaktaki X tanımını doğrudan vermesi gerekir. Doküman başlığı, bölüm adı veya rehberin ne anlattığını söyleyen katalog metni cevap kabul edilmez. Tanım portal kaynağından alınır; modelin genel bilgisiyle düzeltilmez.
- Evidence kimliği gerçekten sağlanan kaynaklar arasında mı?
- Claim, her atıflı kanıttaki yerel cümle veya contrast-separated clause pencerelerinden en az biriyle yeterli lexical desteğe sahip mi? Eşleşme Türkçe çekim ekleri için yalnız uzun ve baskın ortak kökü kontrollü biçimde kabul eder; sayı ve olumsuzluk kontrolleri ayrıca korunur. Kanıt pasajları tek metinde birleştirilmez; ilgisiz bir cümledeki olumsuzluk başka bir claim'in polarity sonucunu değiştiremez.
- Grounded olmak tek başına yeterli değildir: claim veya atıf yaptığı makale/ek başlığı, sorunun ayırt edici konu token'larıyla da eşleşmelidir. Soru açıkça bir politika istiyorsa `politika/policy` ankrajı zorunludur; RBAC, API key veya genel portal güvenliği metni yalnız “güvenlik” kelimesini taşıdığı için politika cevabına dönüşemez. İlk konu eşleşmesi geçen claim'in aynı evidence kaynağındaki devam cümleleri kabul edilebilir; bu, her cümlede konu adını tekrarlama zorunluluğu yaratmaz.
- Noktalamasız kısa Markdown bölüm başlıkları ve katalog etiketleri açıklayıcı olgu sayılmaz; extractive enrichment/fallback bunları yanıt maddesi olarak kullanmaz.
- Claim'deki sayılar kanıtla uyumlu mu?
- Claim ile kanıt arasında olumlu/olumsuz anlam çelişkisi var mı?
- Claim yalnız makale başlığını tekrar ederek gerçek bir önerme sunmadan cevap görünümü mü veriyor?
- Atıfsız veya doğrulanamayan claim var mı?

Kullanıcıya görünen yanıt yalnız doğrulamayı geçen claim'lerden yeniden oluşturulur. Böylece düzgün görünen fakat dayanağı olmayan model metni cevap içine sızamaz. `sources` yalnız doğrulanmış claim'lerde gerçekten atıf yapılan makaleleri, `consultedSources` ise modele verilen bütün makaleleri taşır. İki liste authority, approval, review, reliability ve update metadata'sını içerir; arayüz her kaynağı “Yanıtta kullanıldı” veya “Yalnız incelendi” olarak ayırır. Yanıt ayrıca provenance-bearing `evidence`, citation ID coverage, claim support coverage, grounding durumu, conflict assessment, partial/refusal bilgisi ve uyarıları taşır. Assistant arayüzü çok sayıdaki teknik doğrulama uyarısını varsayılan olarak kapalı, adet gösteren bir uyarı satırında toplar; kullanıcı ayrıntıları açtığında liste sınırlı yükseklikte kaydırılabilir. Evidence, prompt içi `sourceId` yanında stabil `chunkId`, yetki kontrollü `canonicalUrl` ve PDF provenance'ı varsa `pageNumber` döndürür.

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

- Runtime görünümü: `GET /api/admin/rag/observability`
- Yetkili aday/context debug: `GET /api/admin/rag/debug?q=...` (session-admin, LLM çağrısı yok)
- Prometheus alarmları: `ops/prometheus/rag-alerts.yml`
- Grafana dashboard: `ops/grafana/rag-overview.json`
- Ölçülebilir hedefler: `specs/rag-slo.md`

Yönetici arayüzündeki RAG evaluation alanı dinamik golden dataset'ler ve eşikler tanımlar. Çalıştırmalar dataset, config, model ve prompt snapshot'larını saklar; Recall, MRR, NDCG, fact/citation/grounding/refusal/safety/latency metrikleri üretir. CI, gerçek PostgreSQL/pgvector fidelity kapısına ve canlı Ollama golden-dataset kalite kapısına ek olarak REST Assistant routing kontrolünü ve modern `/mcp` `ask_knowledge` çağrısının canlı model, kanıt ve trace sözleşmesini zorunlu çalıştırır.

## Önemli Yapılandırmalar

Başlıca ayarlar `backend/appsettings.json` içindedir:

- `Ollama:RagCandidateLimit` / `RagBroadCandidateLimit`
- `Ollama:RagMinimumSourceLimit` / `RagSourceLimit` / `RagSourceRelativeScoreFloor` adaptif kaynak genişliği; varsayılan 3 / 10 / 0,55
- `Ollama:RagMaxChunksPerArticle`
- `Ollama:RagMaxContextTokens` / `RagModelContextTokens` / `RagPromptReserveTokens` ve `RagTokenizer:LatinCharactersPerToken`
- `Ollama:RagMapReduceBatchChunks` / `RagMaxOutputTokens` (varsayılan 4.096) / `RagBroadMinimumClaims` (varsayılan 8; kapsam hedefi mevcut ilgili gerçek kapasitesiyle sınırlanır)
- `Assistant:DefaultAnswerProfile` varsayılan `balanced` profilini bildirir; REST `answerProfile` ve MCP `answer_profile`, `compact|balanced|comprehensive` değerlerini kabul eder
- `Ollama:RagGroundingRepairEnabled` reddedilen üretim için tek seferlik, aynı kanıta bağlı düzeltme çağrısı
- `Ollama:RagRrfK` / `RagLexicalWeight` / `RagSemanticWeight`
- `Ollama:RagDuplicateThreshold` / `RagMinSimilarityScore`
- `Ollama:QueryUnderstanding:*` rewrite, synonym ve decomposition ayarları
- `Ollama:ContextExpansion:*` child→parent çözümleme eşiği/tohum sınırı ve yalnız rolling-upgrade legacy komşu fallback sınırları
- `Ollama:Ranking:*` freshness/approval katkıları ve dinamik `lookup_values.authority_weight` değerinin relevance skoruna bounded katkı çarpanı
- `Reranking:External:*`, retrieval sonrasında aday pasajları harici bir cross-encoder ile yeniden sıralayan isteğe bağlı katmandır. `Enabled=false` ile varsayılan olarak kapalıdır; kapalıyken veya geçerli bir HTTPS/loopback `Endpoint` verilmediğinde yerel deterministik sıralama aynen kullanılır. Etkinleştirildiğinde yerel reranker'ın en iyi `MaxCandidates` adayı; bounded title/source/excerpt/passage metni, bounded sorgu ve yapılandırılan `Model` ile endpoint'e gönderilir. `RequestFormat=objects|strings|texts` generic object, Cohere/Jina tarzı string document ve TEI tarzı texts sözleşmelerini; `ApiKeyHeader=Authorization|X-API-Key|Api-Key` kimlik doğrulama profilini seçer. Redirect kapalıdır. Toplam `TimeoutSeconds`, `MaxRetries`, `MaxResponseBytes`, concurrency/queue bulkhead ve ardışık hata circuit breaker sınırları uygulanır. `results`, `data`, `scores` ve doğrudan array skor şemaları kabul edilir; duplicate/geçersiz indeksler elenir, `MinimumScoreCoverage` altındaki yanıt tümüyle reddedilir, `[0,1]` dışı logits min-max normalize edilir. Geçerli skorlar `ScoreWeight` oranında yerel skorlarla birleştirilir; eksik bir aday harici skorla cezalandırılmaz. Her hata/overload durumunda istek bozulmadan yerel sonuca dönülür ve `kp_rag_reranker_*` metrikleri outcome/latency kaydeder. Varsayılanlar 8 saniye, 1 retry, 50 aday, aday başına 4.000 karakter, 262.144 response byte, yüzde 80 skor coverage ve 0,8 harici skor ağırlığıdır.
- `Assistant:QueryContextualization:*`, owned konuşmalarda bounded alternating history ile bağımsız takip sorgusu üretimini yönetir. “Nasıl kullanılır?”, “nasıl çalışır?” ve “örnek ver” gibi öznesi düşürülmüş takip soruları önceki kullanıcı konusuyla birleştirilir. LLM rewrite önceki konuyu kaybederse deterministic topic guard önceki kullanıcı sorusundan güvenli standalone sorguya döner ve ilgisiz olabilecek HyDE pasajını atar. `Enabled`, `HydeEnabled`, `HydeWeight` (varsayılan 0,3), history message/character limitleri, 8 saniyelik timeout, output/query/HyDE karakter limitleri bulunur. HyDE yalnız ağırlığı sınırlandırılmış ikinci dense retrieval sinyalidir; hiçbir zaman kanıt veya generation context değildir. Contextualization outcome ve latency değerleri `kp_assistant_query_contextualization*` metriklerinde izlenir.
- `Ollama:ParentChunkTargetWords` / `ChildChunkTargetWords` / `ChildChunkOverlapWords` / `ChunkingVersion`
- `DocumentParsing:Vision:*` görsel sayısı/boyutu/çıktı sınırları; `DocumentParsing:External:*` açık izinli Unstructured endpoint/strateji/timeout/fallback politikası. Harici parser modeli veya konfigürasyonu değiştiğinde `External:ProfileVersion` artırılır; profil ayrıca vision model/prompt/bütçeleri ile çıkarım karakter limitini kapsar ve eski indeksin yeniden üretilmesini sağlar.
- `RagResilience:*` timeout, budget, retry, parallelism ve circuit breaker ayarları

Bu değerler değiştirilirken latency, recall, citation coverage, refusal oranı ve hata bütçesi birlikte değerlendirilmelidir. Değişiklik sonrası PostgreSQL fidelity testleri ve canlı RAG kalite kapısı çalıştırılmalıdır.

## Gelecek İyileştirmesi: Tekrarsız Ek İndeksleme ve Adaptif Parser/Vision

Bu bölüm planlanan bir iyileştirmedir; mevcut davranış değildir. Kaynak Markdown veya başka bir belge `articles.content` içine aktarıldıktan sonra orijinal dosya attachment olarak da tutulursa bugün iki kaynak ayrı ayrı indekslenebilir. Gelecekte attachment başına `archive_only`, `text_only`, `visual_only` ve `full` benzeri bir indeksleme politikası ile normalize içerik/chunk fingerprint'i eklenmelidir. Canonical makale gövdesine aktarılmış Markdown'ın orijinali varsayılan olarak `archive_only` kalmalı; indirilebilirlik ve provenance korunurken aynı metin FTS ağırlığına ve embedding havuzuna ikinci kez girmemelidir. Metni gövdede bulunan fakat özgün tablo/görsel kanıtı taşıyan belgelerde `visual_only` kullanılabilir. Yakın-duplicate eleme ancak gerçek portal corpus'u üzerinde ölçülerek açılmalıdır.

Parser katmanı provider tabanlı tutulmalıdır. Native .NET/OpenXML çıkarıcı desteklenen born-digital dosyalarda hızlı ve deterministik yol olarak kalırken Microsoft MarkItDown daha geniş formatlar için isteğe bağlı Python sidecar adayı olarak değerlendirilebilir; taranmış veya karmaşık layout belgeleri Unstructured `hi_res` ya da onaylı başka bir layout servisine yönlendirilebilir. Seçim yalnız format sayısına göre değil; golden corpus üzerinde okuma sırası, tablo hücresi doğruluğu, provenance, Recall/MRR/NDCG, gecikme, kaynak maliyeti ve hata oranıyla yapılmalıdır.

Vision çağrıları da adaptif hale getirilmelidir: perceptual hash ile tekrar eden logo/ikonlar elenmeli, küçük/düşük bilgi taşıyan görseller atlanmalı, önce ucuz OCR/layout denenmeli ve multimodal model yalnız tablo, grafik, diyagram, taranmış sayfa veya düşük güvenli OCR için çağrılmalıdır. Görsel açıklamaları dokümanlar arasında hash-cache ile paylaşılmalı ve format/sayfa bazlı bütçeler uygulanmalıdır. Sonuçlar ingestion sırasında kalıcı saklanmaya devam etmeli; normal arama veya RAG sorgusu vision çağrısı üretmemelidir. Uygulama ACL, provenance, citation, extraction-profile invalidation ve durable retry/fallback davranışlarını korumalı; canlıya geçiş kalite metriklerinin yanında doküman başına vision çağrısı ve GPU/provider maliyet eşiğine bağlanmalıdır.
