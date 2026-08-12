---
{
  "title": "Arama Motoru — Fulltext, Semantic, Hybrid ve RAG",
  "contentType": "reference",
  "tags": [
    "project-knowledge-portal",
    "best-practices",
    "performance"
  ],
  "excerpt": "Knowledge Portal'ın dört farklı arama modunun detaylı açıklaması, kullanım senaryoları ve yapılandırması.",
  "status": "published"
}
---

## Arama Modları

Knowledge Portal dört farklı arama modu sunar. Her mod farklı senaryolar için optimize edilmiştir. Varsayılan mod fulltext'tir ve Ollama olmadan çalışır.

## 1. Fulltext Arama (FTS5)

SQLite FTS5 sanal tablosu kullanılarak BM25 algoritması ile sıralanan tam metin aramasıdır. Ollama gerektirmez.

### Nasıl Çalışır?

- Makale başlığı, özet, içerik metni ve ek dosya içerikleri FTS5 indeksine eklenir.
- Arama sorgusu tokenize edilir ve BM25 skoru hesaplanır.
- FTS5 kullanılamayan durumlarda LIKE fallback devreye girer.
- Wildcard karakterleri (% ve _) otomatik olarak escape edilir.

### Ne Zaman Kullanmalı?

Kesin kelime eşleşmesi aradığınızda, teknik terimler veya hata kodları gibi spesifik metinleri bulmak istediğinizde kullanın.

## 2. Semantic Arama

Ollama nomic-embed-text modeli ile metin embedding'leri oluşturulur ve SIMD cosine similarity ile en benzer içerikler bulunur.

### Nasıl Çalışır?

- Makaleler ~500 kelimelik chunk'lara bölünür (50 kelime overlap).
- Her chunk için 768 boyutlu embedding vektörü oluşturulur.
- Arama sorgusu da embedding'e dönüştürülür.
- Cosine similarity ile en yüksek skorlu chunk'lar (min 0.3) döner.
- Her makale için en iyi chunk skoru kullanılır (best-chunk scoring).

### Ne Zaman Kullanmalı?

Anlamsal olarak benzer içerikler aradığınızda, tam kelime eşleşmesi olmasa bile kavramsal yakınlık istediğinizde kullanın. Örneğin 'container yönetimi' araması 'Docker orchestration' içeren makaleleri de bulabilir.

## 3. Hybrid Arama

Fulltext ve semantic aramanın sonuçlarını Reciprocal Rank Fusion (RRF) algoritması ile birleştirir.

### RRF Parametreleri

- **α (alpha):** 0.4 — Fulltext ağırlığı
- **β (beta):** 0.6 — Semantic ağırlığı
- **k:** 60 — RRF smoothing parametresi

Her sonuç matchType alanı ile hangi kaynaklardan geldiğini belirtir: fulltext, semantic veya both.

### Ne Zaman Kullanmalı?

En kapsamlı arama deneyimi için önerilir. Hem kelime eşleşmesi hem de anlamsal benzerlik bir arada değerlendirilir. Ollama kullanılamadığında otomatik olarak sadece fulltext'e düşer.

## 4. RAG Arama (AI Yanıtlı)

Retrieval-Augmented Generation: Semantic arama ile bulunan en ilgili makaleler bağlam olarak kullanılarak Ollama llama3.2 modeli ile doğal dilde yanıt üretilir.

### Nasıl Çalışır?

1. Semantic arama ile en ilgili 5 makale bulunur.
2. Bu makalelerin içeriği (max 3000 kelime) bağlam olarak hazırlanır.
3. Ollama llama3.2 modeline sorgu + bağlam gönderilir.
4. Model, bağlamdaki bilgilere dayanarak yanıt üretir.
5. Yanıt ile birlikte kaynak makaleler (sources) döner.

## Arama Filtre Sözdizimi

Arama sorgusunda özel sözdizimi ile filtreler ekleyebilirsiniz:

- `#etiket-slug` — Etiket filtresi (AND mantığı, birden fazla kullanılabilir)
- `@yazar-slug` — Yazar filtresi (OR mantığı, birden fazla kullanılabilir)
- `##icerik-turu` — İçerik türü filtresi (OR mantığı)

Örnek: `@admin #tutorial ##how-to react hooks`

## İndeksleme

Makaleler yayınlandığında, içerik değiştiğinde veya ek dosya eklendiğinde/silindiğinde otomatik olarak indekslenir. Background service her 5 saniyede bir kontrol eder ve batch olarak (10'lu gruplar) işler. İndeksleme durumu API üzerinden sorgulanabilir.
