---
{
  "title": "RBAC ve Yetkilendirme Sistemi",
  "contentType": "reference",
  "tags": [
    "project-knowledge-portal",
    "security",
    "best-practices"
  ],
  "excerpt": "Rol tabanlı erişim kontrolü (RBAC), yetki matrisi ve güvenlik mimarisi açıklaması.",
  "status": "published"
}
---

## RBAC Mimarisi

Knowledge Portal, rol tabanlı erişim kontrolü (Role-Based Access Control) kullanır. Her kullanıcı bir role atanır ve bu rol, kullanıcının hangi işlemleri yapabileceğini belirler.

## Roller

### Admin

Sistem yöneticisi rolü. Tüm yetkilere sahiptir:

- Tüm makaleleri düzenleme ve silme
- Makale yayınlama, arşivleme ve onaylama
- Kullanıcı yönetimi (oluşturma, düzenleme, silme, rol atama)
- Kendi API key'lerini ve tüm kullanıcıların API key'lerini yönetme
- Etiket yönetimi
- Analitik görüntüleme

### Editor

İçerik editörü rolü. Kendi makalelerini düzenler, ek olarak:

- Makale yayınlama ve arşivleme
- Yayınlanmış makalelere onay güven sinyali ekleme/kaldırma
- Etiket yönetimi
- Analitik görüntüleme
- Kendi API key'lerini yönetme

### Viewer

Temel kullanıcı rolü. Yeni kayıtlarda varsayılan rol:

- Makale oluşturma ve kendi makalelerini yayınlama
- Kendi makalelerini düzenleme
- Yayınlanmış makaleleri ve kendi makalelerini görüntüleme
- Oy verme ve yorum yapma
- Kendi API key'lerini yönetme

## Yetki Matrisi

Aşağıda tüm yetkiler ve hangi rollerin bunlara sahip olduğu listelenmiştir:

- `articles:create` — admin, editor, viewer
- `articles:edit_own` — admin, editor, viewer
- `articles:edit_any` — admin
- `articles:delete_any` — admin
- `articles:publish` — admin, editor, viewer
- `articles:archive` — admin, editor
- `articles:approve` — admin, editor
- `tags:manage` — admin, editor
- `users:manage` — admin
- `analytics:view` — admin, editor
- `api_keys:manage` — admin, editor, viewer
- `api_keys:manage_any` — admin
- `featured_links:manage` — admin

## Uygulama Desenleri

RBAC iki farklı desende uygulanır:

1. **Attribute-based:** [RequirePermission("permission")] attribute'u ile basit kontroller. Endpoint seviyesinde uygulanır.
2. **Inline:** RbacService.HasPermission() ile sahiplik tabanlı veya koşullu kontroller. Örneğin: edit_own kontrolünde makalenin sahibi mi diye bakılır.

## Session-Only Endpoint'ler

Bazı hassas endpoint'ler sadece JWT session ile erişilebilir, API key ile erişilemez:

- Admin kullanıcı yönetimi (/api/admin/users)
- Analitik (/api/analytics)
- API key yönetimi (/api/keys)
- Admin API key yönetimi (/api/admin/keys)
- Arama reindex, onarım, teşhis, durum ve RAG gözlemlenebilirlik işlemleri
- Sistem logları ve RAG kalite değerlendirmesi yönetimi
- Kendi oyunu geri alma dışındaki silme endpoint'leri

API key ile bu endpoint'lere erişim denendiğinde HTTP 403 yanıtı döner.

API key principal'ı, sahibi admin olsa bile en fazla editor rolünün yetkilerini taşır. `articles:delete_any` her durumda reddedilir; admin-only `users:manage`, `api_keys:manage_any` ve `featured_links:manage` yetkileri key'e aktarılmaz.
