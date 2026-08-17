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
- API key yönetimi
- Etiket yönetimi
- Analitik görüntüleme

### Editor

İçerik editörü rolü. Kendi makalelerini tam kontrol eder, ek olarak:

- Makale yayınlama ve arşivleme
- Yayınlanmış makalelere onay güven sinyali ekleme/kaldırma
- Etiket yönetimi
- Analitik görüntüleme

### Viewer

Temel kullanıcı rolü. Yeni kayıtlarda varsayılan rol:

- Makale oluşturma ve kendi makalelerini yayınlama
- Kendi makalelerini düzenleme ve silme
- Yayınlanmış makaleleri ve kendi makalelerini görüntüleme
- Oy verme ve yorum yapma

## Yetki Matrisi

Aşağıda tüm yetkiler ve hangi rollerin bunlara sahip olduğu listelenmiştir:

- `articles:create` — admin, editor, viewer
- `articles:edit_own` — admin, editor, viewer
- `articles:edit_any` — admin
- `articles:delete_own` — admin, editor, viewer
- `articles:delete_any` — admin
- `articles:publish` — admin, editor
- `articles:archive` — admin, editor
- `articles:approve` — admin, editor
- `tags:manage` — admin, editor
- `users:manage` — admin
- `analytics:view` — admin, editor
- `api_keys:manage` — admin

## Uygulama Desenleri

RBAC iki farklı desende uygulanır:

1. **Attribute-based:** [RequirePermission("permission")] attribute'u ile basit kontroller. Endpoint seviyesinde uygulanır.
2. **Inline:** RbacService.HasPermission() ile sahiplik tabanlı veya koşullu kontroller. Örneğin: edit_own kontrolünde makalenin sahibi mi diye bakılır.

## Session-Only Endpoint'ler

Bazı hassas endpoint'ler sadece JWT session ile erişilebilir, API key ile erişilemez:

- Admin kullanıcı yönetimi (/api/admin/users)
- Analitik (/api/analytics)
- API key yönetimi (/api/keys)
- Arama reindex ve embedding durumu

API key ile bu endpoint'lere erişim denendiğinde HTTP 403 yanıtı döner.
