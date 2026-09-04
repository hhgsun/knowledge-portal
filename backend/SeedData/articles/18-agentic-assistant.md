---
{
  "title": "Bilgi Asistanı ve Kaynaklı RAG",
  "contentType": "reference",
  "tags": ["project-knowledge-portal", "best-practices", "security", "monitoring"],
  "excerpt": "Bilgi Asistanı'nın tek amaçlı kaynaklı RAG akışı; Search sınırı, yetki, geri bildirim, cache ve dayanıklılık kontrolleri.",
  "status": "published"
}
---

## Amaç ve Kesin Sınır

Bilgi Asistanı yalnız portal bilgisinden kaynaklı yanıt üretir. Doküman sonuç listesi, portal analitiği, genel sohbet, serbest SQL veya yazma aracı sunmaz. Asistanın bounded turn planner'ı bir turu yalnız `retrieve`, `transform_previous` veya `clarify` olarak ele alır; bu, başka ürün yüzeylerine giden serbest bir router değildir. Doküman bulmak için `/search` ve `GET /api/search`; kanıtlara dayanarak yanıt almak için `/assistant`, `POST /api/assistant` veya MCP `ask_knowledge` kullanılır.

```text
POST /api/assistant
        │
        ▼
owned conversation + versioned grounded turn state
        │
        ▼
AssistantTurnPlanningService
        │
        ├── presentation-only → verified-claim transform
        ├── ambiguous/no state → clarification
        └── knowledge question → contextualized retrieval query
        │
        ▼
KnowledgeAnswerService → authorized evidence → grounded claims
        │
        ▼
AssistantPresentationService → safe typed content blocks
        │
        ├── Assistant interaction audit / feedback
        └── optional semantic answer cache
```

`SearchExecutionService` yalnız `fulltext`, `semantic` ve `hybrid` doküman araması yürütür. `KnowledgeAnswerService` ise Assistant ve MCP için tek kanonik RAG girişidir. Her ikisi inline `#etiket`, `@yazar`, generic `+kategori:değer` ve açık filtreleri aynı `KnowledgeQueryScopeService` üzerinden çözer; böylece filtre semantiği ortak, ürün davranışı ayrıdır.

## API ve Arayüz

```json
POST /api/assistant
{
  "message": "VPN politikasının istisnaları nelerdir?",
  "conversationId": null,
  "tags": ["security"],
  "authors": [],
        "contentTypes": ["policy"],
        "retrievalStrategy": "baseline"
}
```

`retrievalStrategy` varsayılan olarak `baseline` değerindedir. Pilot için `Assistant:AgenticRetrieval:Enabled=true` açıldığında arayüzde `agentic` (Araştırmalı) seçeneği görünür. Bu mod, yanıt üretilmeden önce modelden en fazla dört kısa retrieval sorgusu önerisi alır. Öneriler yalnız sorgu metnidir; tag, yazar veya `+kategori:değer` kapsamını değiştiremez, SQL/URL/tool komutu içeremez ve doğrudan veriye erişemez. Sunucu özgün soruyu her zaman korur, plan bozuk veya zaman aşımındaysa baseline sorgularına geri döner ve her sorguyu aynı published-only, hibrit retrieval ve grounding kontrollerinden geçirir.

Yanıt `normalizedQuery`, doğrulanmış `answer`, `intent`, `presentation`, allowlist edilmiş `contentBlocks`, `rag`, `toolCalls`, `warnings`, `interactionId`, `responseTimeMs`, `traceId`, `conversationId` ve `cacheHit` alanlarını taşır. `rag` içinde atıf yapılan `sources`, yalnız incelenen `consultedSources`, typed `claims`, provenance-bearing `evidence`, coverage değerleri, grounding durumu ve çatışma değerlendirmesi bulunur. Arama sonucu listesi dönmez; `intent` ve `presentation` yalnız cevap görevini ve görünümünü açıklar.

Model serbest bir tablo, HTML, SVG veya görselleştirme programı üretmez. Provider'dan yalnız kaynak kimliklerine bağlı atomik claim'ler alınır. Backend aynı doğrulanmış claim kümesinden geriye uyumlu Markdown `answer` ile `markdown`, `paragraph`, `bullet_list`, `ordered_list`, `table`, `process_flow` veya fact-card `infographic` typed bloklarını oluşturur. Frontend yalnız bu blok sözlüğünü render eder. Böylece tablo, infografik ve süreç şeması gibi zengin anlatımlar kullanılabilir, fakat sunum katmanı yeni olgu icat edemez veya çalıştırılabilir içerik taşıyamaz.

`POST /api/assistant/stream` gerçek SSE kullanır. İstemci; doğrulama, model/konuşma hazırlığı, önbellek, kapsam çözümleme, retrieval, kanıt değerlendirmesi, üretim, grounding ve sunum aşamalarını sıralı `status` olaylarıyla izler. Her olay kalıcı bir `stage` kimliği ve kullanıcıya gösterilecek `message` taşır. Doğrulanmamış model tokenları istemciye açılmaz; yalnız doğrulanmış sonuç `token` ve `complete` olaylarıyla gönderilir.

Arama ekranındaki “Bilgi Asistanına sor” eylemi sorguyu metni korunarak `/assistant?q=...` adresine taşır. Bu bir uyumluluk yönlendirmesi değildir; kullanıcı tarafından seçilen iki ayrı ürün akışı arasındaki açık geçiştir.

## Çok Turlu Konuşma

Konuşma bağlamı yalnız interaktif oturumda kullanılabilir ve kullanıcı sahipliğiyle korunur; API key geçmiş okuyamaz. Kullanıcı başına veritabanında en fazla bir konuşma bulunur. `/assistant` açıldığında oluşturulan yeni oturum konuşması aynı kullanıcıya ait önceki konuşmayı ve mesajlarını kalıcı olarak silip yerine geçer; frontend eski konuşmaları listelemez veya yeniden açmaz. Sayfa açık kaldığı sürece bütün takip soruları aynı kimliği kullanır. Asistan görünümü AppShell'in standart sayfa başlığı ve boşluk düzenine doğrudan yerleşir; ayrı border, gölge, radius ve arka planla çevrelenmiş bir uygulama kutusu oluşturmaz. Her tamamlanmış yanıtın yalnız atıf yapılan kaynakları doğrudan yanıtın altında bağlantı olarak gösterilir; ayrı kaynak inceleme, consulted-source veya kanıt pasajı paneli yoktur.

Her assistant mesajı isteğe bağlı versioned `turn_state_json` taşır: özgün istek, normalize retrieval query, bounded intent/sunum, görünen cevap, doğrulanmış RAG claim/atıfları ve cevap profili. `AssistantTurnPlanningService`, “sırala”, “maddele”, “tablo yap”, “özetle”, “akış şeması yap” veya “infografik yap” gibi yalnız sunum isteyen bir takip turunda yeni arama ya da model çağrısı yapmaz; önceki doğrulanmış claim'leri istenen biçimde yeniden sunar. Önceki grounded state yoksa literal “sıralama” sorgusu aramak yerine kullanıcıdan hangi bilginin dönüştürüleceğini sorar. Bu dönüşümler sıfır generation token'ı raporlar ve önceki kaynak/provenance bağını korur.

Yeni bilgi isteyen takiplerde `AssistantQueryContextualizer`, bounded son kullanıcı ve asistan turlarını untrusted veri olarak işleyip “peki bunun istisnası var mı?” veya “nasıl entegre ederim?” gibi anaforik/öznesi düşürülmüş soruları önceki konu, kesin teknik adlar ve explicit scope token'ları korunmuş bağımsız bir arama sorusuna dönüştürür. Örneğin “MCP nedir?” turundan sonraki “nasıl entegre ederim?” sorgusu `MCP hakkında: nasıl entegre ederim?` biçiminde retrieval'a girer. Model timeout, hata veya şema dışı çıktı verirse istek bozulmaz; son kullanıcı konusuyla deterministik rewrite uygulanır. Konuşma metni hiçbir zaman kanıt sayılmaz; generation'a yalnız yeniden yetkilendirilmiş portal evidence girer. Backend'in sahiplik kontrollü konuşma endpoint'leri yalnız bu aktif oturumun bağlamını sağlar.

`Assistant:QueryContextualization:HydeEnabled=true` olduğunda aynı bounded çağrı kısa bir hypothetical knowledge passage da üretebilir. Bu metin gerçek bilgi veya kanıt sayılmaz: yalnız standalone query embedding'ine ek ikinci dense lookup çalıştırır; FTS'e, context builder'a, citation validator'a veya yanıt üretim promptuna girmez. Original-query ve HyDE dense adayları kimlik bazında `HydeWeight` (varsayılan 0,3) ile ağırlıklı birleştirilir; iki sinyalin aynı child'a isabeti yalnız küçük bir tie-break bonusu verir. Böylece üretilmiş metin original kullanıcı sorgusunu domine edemez.

## Yetki ve Fail-Closed Davranış

Retrieval her aşamada değişmez `published` koşulunu uygular. Taslak ve arşivlenmiş makaleler kullanıcıya ait olsa veya rolü nedeniyle normal makale ekranında görülebilse bile Assistant/RAG kaynağı olamaz. Creator veya API-key sahipliği yayınlanmış havuzu daraltmaz; Assistant ve RAG yayınlanmış tüm makaleleri görebilir. `onlyOwnContent` yalnız normal arayüz ile REST makale/arama API'lerinde kullanılır. Açık konu/yazar/sınıflandırma filtreleri published havuzu daraltabilir. Yalnız published kontrolünden geçen parent kanıtlar modele girer. Kaynak metindeki prompt-injection işaretleri, secret redaction, citation/claim doğrulaması, sayısal ve polarity çelişki denetimleri korunur.

Yeterli kanıt yoksa Asistan uydurmaz. AI kapalıysa, kapasite doluysa, devre açıksa veya timeout oluşursa doküman aramasına sessizce düşmez; uygun `429`, `503` veya `504` hatası döner. Kullanıcı isterse ayrı Doküman Ara deneyimine geçer.

## MCP ve Entegrasyonlar

MCP `search_articles` yalnız doküman sonuçları için `fulltext`, `semantic` ve `hybrid` modlarını kabul eder. Kaynaklı yanıt için `ask_knowledge` kullanılır. Bütün MCP bilgi araçları creator/API-key sahipliğinden bağımsız biçimde yayınlanmış tüm makaleleri görebilir ve `only_own_content` kabul etmez. `ask_knowledge`; `question` ile inline/açık konu kapsamlarını alır ve REST Assistant ile aynı `KnowledgeAnswerService` ve published-only sınırını kullanır. Semantic arama ve AI yanıtı ayrı concurrency/circuit-breaker havuzlarına sahiptir; bir AI darboğazı salt lexical aramayı etkilemez.

## Cache, Audit ve Geri Bildirim

Semantic answer cache yalnız yeterli citation coverage sağlayan, partial/insufficient olmayan grounded cevapları saklar. Anahtar kullanıcı, rol, auth kaynağı, API-key, filtre kapsamı, corpus/runtime sürümü, model, prompt, retrieval ve chunking profillerini kapsar; bunlardan biri değişince cache miss olur.

`assistant_interactions` ham sorgu veya cevabı değil; SHA-256 sorgu/yanıt fingerprint'leri, RAG sürüm/profil/grounding bilgileri, tool/timing kimlikleri ve isteğe bağlı geri bildirimi saklar. `POST /api/assistant/feedback` yalnız interaction sahibine açıktır. Kaynak tıklamaları `POST /api/assistant/source-click` ile aynı interaction'a bağlanır. Search kalite kayıtları ile Assistant RAG kayıtları birbirine yazılmaz.

## Operasyon ve Yapılandırma

`Assistant:Enabled=false` olduğunda Assistant endpoint'leri 404 döner; Search çalışmaya devam eder. `Assistant:AgenticRetrieval:Enabled=false` varsayılandır; etkin olduğunda `MaxQueries` (1-4) ve `PlanningTimeoutSeconds` (1-30) planlama bütçesini sınırlar. `GET /api/capabilities` runtime enablement, grounded RAG, streaming, conversation, feedback, semantic-cache ve etkin `retrievalStrategies` listesinin yanında `FileStorage` kaynaklı attachment uzantı/boyut/adet sınırlarını bildirir. Frontend upload kontrolleri için endpoint'i Assistant build flag'inden bağımsız yükler; `VITE_ASSISTANT_ENABLED=false` yalnız Assistant route ve menüsünü build sırasında kaldırır.

Search ve Assistant ayrı rate-limit politikaları kullanır. Assistant toplam süresi `Assistant:TotalTimeoutSeconds`, mesaj boyutu `Assistant:MaxMessageCharacters` ile sınırlıdır. RAG çalışma görünümü ve modelsiz debug endpoint'leri session-admin için sırasıyla `GET /api/admin/rag/observability` ve `GET /api/admin/rag/debug?q=...` adreslerindedir.

Prometheus'ta `kp_rag_*` pipeline metriklerine ek olarak Assistant süre, tool-call, feedback, audit hatası ve answer-cache sonuçları izlenir. Usage operasyonları `assistant.answer` ve `assistant.stream.answer` olarak kaydedilir. Yönetici RAG evaluation dataset'i tek turlu vakaların yanında `turns` dizili konuşma vakalarını da kabul eder. Her tur için beklenen `expectedIntent`, `expectedPresentation` ve `expectedRetrieval` tanımlanabilir; `conversationTaskAccuracy` ile `retrievalDecisionAccuracy` eşikleri “MCP nedir?” → “sırala” gibi görev bağlamı regresyonlarını canlı kalite kapısında durdurur.
