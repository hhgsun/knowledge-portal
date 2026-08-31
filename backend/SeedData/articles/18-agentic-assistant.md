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

Bilgi Asistanı yalnız portal bilgisinden kaynaklı yanıt üretir. Doküman sonuç listesi, portal analitiği, genel sohbet, intent routing, serbest SQL veya yazma aracı sunmaz. Doküman bulmak için `/search` ve `GET /api/search`; kanıtlara dayanarak yanıt almak için `/assistant`, `POST /api/assistant` veya MCP `ask_knowledge` kullanılır.

```text
POST /api/assistant
        │
        ▼
conversation context + shared query scope
        │
        ▼
KnowledgeAnswerService
        │
        ▼
RagService → authorized evidence → grounded answer
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
  "onlyOwnContent": false,
  "tags": ["security"],
  "authors": [],
  "contentTypes": ["policy"]
}
```

Yanıt `normalizedQuery`, doğrulanmış `answer`, `rag`, `toolCalls`, `warnings`, `interactionId`, `responseTimeMs`, `traceId`, `conversationId` ve `cacheHit` alanlarını taşır. `rag` içinde atıf yapılan `sources`, yalnız incelenen `consultedSources`, typed `claims`, provenance-bearing `evidence`, coverage değerleri, grounding durumu ve çatışma değerlendirmesi bulunur. Arama sonucu listesi veya routing metadatası dönmez.

`POST /api/assistant/stream` gerçek SSE kullanır. Doğrulanmamış model tokenları istemciye açılmaz; retrieval ve grounding durum olaylarından sonra yalnız doğrulanmış sonuç `token` ve `complete` olaylarıyla gönderilir.

Arama ekranındaki “Bilgi Asistanına sor” eylemi sorguyu metni korunarak `/assistant?q=...` adresine taşır. Bu bir uyumluluk yönlendirmesi değildir; kullanıcı tarafından seçilen iki ayrı ürün akışı arasındaki açık geçiştir.

## Çok Turlu Konuşma

Konuşma bağlamı yalnız interaktif oturumda kullanılabilir ve kullanıcı sahipliğiyle korunur; API key geçmiş okuyamaz. Kullanıcı başına veritabanında en fazla bir konuşma bulunur. `/assistant` açıldığında oluşturulan yeni oturum konuşması aynı kullanıcıya ait önceki konuşmayı ve mesajlarını kalıcı olarak silip yerine geçer; frontend eski konuşmaları listelemez veya yeniden açmaz. Sayfa açık kaldığı sürece bütün takip soruları aynı kimliği kullanır. Asistan görünümü AppShell'in standart sayfa başlığı ve boşluk düzenine doğrudan yerleşir; ayrı border, gölge, radius ve arka planla çevrelenmiş bir uygulama kutusu oluşturmaz. Her tamamlanmış yanıtın yalnız atıf yapılan kaynakları doğrudan yanıtın altında bağlantı olarak gösterilir; ayrı kaynak inceleme, consulted-source veya kanıt pasajı paneli yoktur. `AssistantQueryContextualizer`, bounded son kullanıcı ve asistan turlarını untrusted veri olarak işleyip “peki bunun istisnası var mı?” gibi anaforik takip sorularını önceki konu, kesin teknik adlar ve explicit scope token'ları korunmuş bağımsız bir arama sorusuna dönüştürür. Model timeout, hata veya şema dışı çıktı verirse istek bozulmaz; son kullanıcı konusuyla deterministik rewrite uygulanır. Backend'in sahiplik kontrollü konuşma endpoint'leri yalnız bu aktif oturumun bağlamını sağlar.

`Assistant:QueryContextualization:HydeEnabled=true` olduğunda aynı bounded çağrı kısa bir hypothetical knowledge passage da üretebilir. Bu metin gerçek bilgi veya kanıt sayılmaz: yalnız standalone query embedding'ine ek ikinci dense lookup çalıştırır; FTS'e, context builder'a, citation validator'a veya yanıt üretim promptuna girmez. Original-query ve HyDE dense adayları kimlik bazında `HydeWeight` (varsayılan 0,3) ile ağırlıklı birleştirilir; iki sinyalin aynı child'a isabeti yalnız küçük bir tie-break bonusu verir. Böylece üretilmiş metin original kullanıcı sorgusunu domine edemez.

## Yetki ve Fail-Closed Davranış

Retrieval her aşamada yayın durumu, kullanıcı rolü, API-key sahipliği ve istek filtrelerini uygular; yalnız ACL tekrar kontrolünden geçen parent kanıtlar modele girer. Kaynak metindeki prompt-injection işaretleri, secret redaction, citation/claim doğrulaması, sayısal ve polarity çelişki denetimleri korunur.

Yeterli kanıt yoksa Asistan uydurmaz. AI kapalıysa, kapasite doluysa, devre açıksa veya timeout oluşursa doküman aramasına sessizce düşmez; uygun `429`, `503` veya `504` hatası döner. Kullanıcı isterse ayrı Doküman Ara deneyimine geçer.

## MCP ve Entegrasyonlar

MCP `search_articles` yalnız doküman sonuçları için `fulltext`, `semantic` ve `hybrid` modlarını kabul eder. Kaynaklı yanıt için `ask_knowledge` kullanılır. Araç; `question`, inline/açık filtreler ve `onlyOwnContent` alır, REST Assistant ile aynı `KnowledgeAnswerService` ve yetki sınırlarını kullanır. Semantic arama ve AI yanıtı ayrı concurrency/circuit-breaker havuzlarına sahiptir; bir AI darboğazı salt lexical aramayı etkilemez.

## Cache, Audit ve Geri Bildirim

Semantic answer cache yalnız yeterli citation coverage sağlayan, partial/insufficient olmayan grounded cevapları saklar. Anahtar kullanıcı, rol, auth kaynağı, API-key, filtre kapsamı, corpus/runtime sürümü, model, prompt, retrieval ve chunking profillerini kapsar; bunlardan biri değişince cache miss olur.

`assistant_interactions` ham sorgu veya cevabı değil; SHA-256 sorgu/yanıt fingerprint'leri, RAG sürüm/profil/grounding bilgileri, tool/timing kimlikleri ve isteğe bağlı geri bildirimi saklar. `POST /api/assistant/feedback` yalnız interaction sahibine açıktır. Kaynak tıklamaları `POST /api/assistant/source-click` ile aynı interaction'a bağlanır. Search kalite kayıtları ile Assistant RAG kayıtları birbirine yazılmaz.

## Operasyon ve Yapılandırma

`Assistant:Enabled=false` olduğunda Assistant endpoint'leri 404 döner; Search çalışmaya devam eder. `GET /api/capabilities` runtime enablement, grounded RAG, streaming, conversation, feedback ve semantic-cache durumunun yanında `FileStorage` kaynaklı attachment uzantı/boyut/adet sınırlarını bildirir. Frontend upload kontrolleri için endpoint'i Assistant build flag'inden bağımsız yükler; `VITE_ASSISTANT_ENABLED=false` yalnız Assistant route ve menüsünü build sırasında kaldırır.

Search ve Assistant ayrı rate-limit politikaları kullanır. Assistant toplam süresi `Assistant:TotalTimeoutSeconds`, mesaj boyutu `Assistant:MaxMessageCharacters` ile sınırlıdır. RAG çalışma görünümü ve modelsiz debug endpoint'leri session-admin için sırasıyla `GET /api/admin/rag/observability` ve `GET /api/admin/rag/debug?q=...` adreslerindedir.

Prometheus'ta `kp_rag_*` pipeline metriklerine ek olarak Assistant süre, tool-call, feedback, audit hatası ve answer-cache sonuçları izlenir. Usage operasyonları `assistant.answer` ve `assistant.stream.answer` olarak kaydedilir.
