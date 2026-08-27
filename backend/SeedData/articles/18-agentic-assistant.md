---
{
  "title": "Agentic Routing ve Bilgi Asistanı",
  "contentType": "reference",
  "tags": [
    "project-knowledge-portal",
    "best-practices",
    "security",
    "monitoring"
  ],
  "excerpt": "Knowledge Portal asistanının güvenli intent routing, mevcut arama/RAG servislerini kullanma, yetki, fallback, gözlemlenebilirlik ve kaldırılabilirlik sınırları.",
  "status": "published"
}
---

## Amaç ve Kapsam

`/assistant`, kullanıcının isteğini sabit ve salt-okunur yeteneklerden birine yönlendirir. Asistan yeni bir arama motoru değildir; portalın mevcut hybrid search, grounded RAG ve analytics servislerini tek bir kontrollü giriş noktasında birleştirir. İlk sürümde makale oluşturma/değiştirme, API çağrısıyla mutasyon, serbest SQL veya Text-to-SQL aracı yoktur.

```text
POST /api/assistant
        │
        ▼
 explicit mode → deterministic rules → optional structured classifier
        │
        ▼
 server policy / RBAC
        │
        ▼
 bounded orchestrator
   ├── SearchExecutionService: hybrid
   ├── SearchExecutionService: grounded RAG
   ├── AnalyticsReportService: authorized statistics
   └── canned general chat / clarification
```

Bağımlılık tek yönlüdür: Assistant katmanı mevcut servislere bağımlıdır; arama, RAG, MCP ve analytics Assistant katmanına bağımlı değildir. Bu sınır özelliğin sonradan güvenle kapatılmasını veya koddan çıkarılmasını sağlar.

## Routing Akışı

Kullanıcı arayüzünde `Auto`, `Yanıt`, `Arama`, `Analitik` ve `Sohbet` modları bulunur. Açıkça seçilen mod model çağırmadan uygulanır. `Auto` modunda sıralama şöyledir:

1. Türkçe ve İngilizce yüksek güvenli sinyaller deterministik olarak değerlendirilir.
2. Yalnız belirsiz istekler düşük token bütçeli, temperature 0 ve zorunlu JSON şemalı sınıflandırıcıya gider.
3. Sınıflandırıcı güveni `AgenticRouting:MinConfidence` altında kalırsa güvenli hybrid arama seçilir.
4. Sınıflandırıcı yoksa veya hata verirse şirket bilgisi uydurmak yerine hybrid arama kullanılır.

Sınıflandırıcı sorguyu yeniden yazamaz: şema yalnız rota, güven, sebep ve birleşik arama isteğini kabul eder; retrieval'a kullanıcının normalize edilmiş özgün metni gider. Sınıflandırıcı çağrıları ana RAG zincirinden ayrı concurrency bulkhead, kısa kuyruk timeout'u, circuit breaker ve timeout ile korunur. Yalnız kesin eşleşen sorgu fingerprint'i için kısa süreli karar cache'i kullanılır; cache ham sorguyu tutmaz.

Desteklenen rotalar:

- `knowledge_search`: Mevcut `SearchExecutionService` üzerinden hybrid sonuç listesi.
- `knowledge_answer`: Aynı servis üzerinden kaynaklı ve fail-closed RAG yanıtı.
- `analytics`: `AnalyticsReportService` üzerinden belirli dönem portal özeti.
- `general_chat`: Şirket olgusu üretmeyen sabit selamlama/yetenek açıklaması.
- `clarification`: Güvenle yönlendirilemeyen girdide açıklama isteği.

Hem yanıt hem kaynak listesi açıkça istenirse bounded orchestrator en çok bir RAG ve bir hybrid search çağrısını birleştirebilir. `MaxToolCalls` bu birleşimi sınırlar. RAG başarısız veya kullanılamaz durumdaysa serbest model metni dönmez; hybrid sonuçlar açıklayıcı uyarıyla gösterilir.

## Yetkilendirme ve Güvenlik

Model kararı hiçbir zaman yetki vermez. `AssistantPolicyService`, seçilen rotayı JWT/API-key principal'ına karşı ayrıca denetler. Search ve RAG normal makale görünürlük/ownership filtrelerini aynen kullanır. Analytics yalnız `analytics:view` yetkili admin/editor interaktif oturumlarına açıktır; viewer ve API key istekleri 403 alır.

Kullanıcı metni sınıflandırıcıya güvenilmeyen JSON veri olarak gönderilir; sınıflandırıcının tool erişimi yoktur. Grounded RAG mevcut kaynak prompt-injection işaretleme, secret redaction, kanıt kimliği, sayısal/polarity ve citation doğrulamalarını korur. Genel sohbet sabit metindir. Tool registry kod düzeyinde salt-okunurdur; yapılandırma değişikliği yazma veya SQL aracı ortaya çıkaramaz.

İstekler `Assistant:MaxMessageCharacters`, sınıflandırıcı timeout'u, toplam timeout, search limit ve tool-call sınırıyla bounded çalışır. Assistant endpoint'i mevcut kullanıcı/IP/API-key bazlı `search` rate-limit politikasını paylaşır.

## API ve Arayüz

```json
POST /api/assistant
{
  "message": "İzin politikasını açıkla ve ilgili dokümanları göster",
  "preferredRoute": "auto"
}
```

Yanıt; gerçek rota, routing kaynağı ve confidence değerinin yanında `answer`, `results`, kaynak/claim/evidence içeren `rag`, yetkili `analytics`, `toolCalls`, `warnings`, `searchQueryId`, `interactionId`, süre ve trace kimliği döndürür. Arayüzde kaynaklar tıklanabilir, kanıt konumları/sayfaları gösterilir ve arama sonucu tıklamaları mevcut kalite telemetrisine bağlanır. Kullanıcı isteği iptal edebilir veya yanlış rota için tek tıkla güvenli `answer`/`search` modunda yeniden deneyebilir. Arayüz modelden türetilmiş görsel açıklamalar gibi ingestion verisini makale gövdesine sessizce yazmaz; Assistant yalnız indekslenmiş kanıtı kaynak/provenance ile sunar.

## Yapılandırma, Gözlemlenebilirlik ve Kapatma

Backend için temel kill switch `Assistant:Enabled` değeridir. `false` olduğunda endpoint 404 döner; `/api/search`, RAG ve MCP değişmeden çalışır. Authenticated `GET /api/capabilities`, runtime durumu frontend'e bildirir; böylece backend kapalıyken görünür fakat çalışmayan menü kalmaz. `AgenticRouting:Enabled=false`, Assistant'ı kaldırmadan sınıflandırmayı kapatır ve açık/varsayılan rotayı kullanır. Frontend build sırasında `VITE_ASSISTANT_ENABLED=false` verilirse sidebar öğesi ve `/assistant` rotası bundle'a eklenmez.

Prometheus metrikleri:

- `kp_assistant_routes`: rota ve karar kaynağı sayıları.
- `kp_assistant_tool_calls`: çağrılan sabit yetenekler ve sonuçları.
- `kp_assistant_duration_ms`: uçtan uca süre ve sonuç.
- `kp_assistant_classifier_requests`, `kp_assistant_classifier_duration_ms`, `kp_assistant_classifier_active`: classifier cache/dayanıklılık/kapasite görünümü.
- `kp_assistant_feedback`: yararlı/yararlı değil ve sebep dağılımı.
- `kp_assistant_audit_failures`: privacy-safe audit yazma hataları.

Usage tracking operasyonu `assistant.<route>` biçimindedir. `assistant_interactions` yalnız SHA-256 sorgu fingerprint'i, route/tool/timing kimlikleri ve isteğe bağlı geri bildirimi saklar; ham sorgu ve cevap saklanmaz. Geri bildirim interaction owner kontrolüyle güncellenir. RAG yanıt oyu ayrıca mevcut owned `search_queries` kaydına aktarılır ve admin RAG kalite ekranında hem RAG hem Assistant rota cohort'ları gösterilir. CI'da Türkçe/İngilizce ve adversarial örneklerden oluşan deterministik `AssistantRouting` golden gate zorunludur. Deploy sonrası admin-session-only `POST /api/assistant/route-preview`, hiçbir tool çalıştırmadan gerçek classifier kararlarını ölçer; canlı script en az %80 başarı ve en az üç gerçek classifier vakası ister. Prometheus alarmları ile Grafana panosu latency, classifier degradation, route ve feedback metriklerini izler.

Fiziksel kaldırma gerekirse `AssistantController`, Assistant router/policy/orchestrator/resilience/audit servisleri, Assistant DTO'ları/testleri, `AssistantPage`, capability context/route/navigation ve ilgili yapılandırma/dokümantasyon kaldırılır. `AnalyticsReportService` kaldırılmaz; normal `AnalyticsController` da bu ortak rapor servisini kullanır. `assistant_interactions` ürün içeriğinden bağımsız tek Assistant-owned tablodur; tutulmayacaksa ayrı ve açık bir migration ile kaldırılır. Search, RAG, MCP ve makale verileri bu işlemden etkilenmez.
