# Knowledge Portal — AI-First Development Guide

> **Bu dosya projenin tek doğruluk kaynağıdır (Single Source of Truth).**
> Tüm AI agent'ları bu dosyayı referans almalıdır.
> Detaylı spec'ler `specs/` klasöründedir — bu dosya ile çelişirse **bu dosya geçerlidir**.

---

## Architecture

Split monorepo: `backend/` (ASP.NET Core Web API) + `frontend/` (React SPA).

| Layer | Stack |
|-------|-------|
| Backend | ASP.NET Core (.NET 10), EF Core, SQLite |
| Auth | JWT Bearer + API Key (kp_ prefix) |
| Frontend | React 19, Vite, React Router v7, Tailwind CSS v4 |
| Editor | TipTap (ProseMirror) |
| Tests | xUnit + WebApplicationFactory (backend only) |

## Conventions

### Backend (`backend/`)

- **Language**: C# 13, .NET 10, nullable enabled
- **Pattern**: Controllers → EF Core DbContext → SQLite (no service layer)
- **Naming**: PascalCase for C# code, snake_case for DB columns (configured in `AppDbContext.OnModelCreating`)
- **Auth**: `[Authorize]` attribute on controllers, `[AllowAnonymous]` for public endpoints
- **RBAC**: `RequirePermission` attribute with permission constants from `Permissions` class
- **API prefix**: All routes under `/api/` (e.g. `/api/articles`, `/api/auth/login`)
- **Entities**: `backend/Models/Entities/` — 11 models: User, Article, ArticleVersion, ArticleView, Tag, ArticleTag, ArticleVote, ArticleComment, ApiKey, SearchQuery, ArticleAttachment
- **Enum Validation**: `contentType` and `difficulty` are validated server-side against allow-lists
- **Seed data**: `DbInitializer.SeedAsync()` — admin user + 10 default tags
- **Port**: 5174
- **Rate Limiting**: ASP.NET Core built-in rate limiter on auth + search endpoints (defaults: auth=10/min, search=30/min, configurable via `appsettings.json` → `RateLimiting`)
- **Middleware pipeline**: GlobalExceptionMiddleware → CORS → RateLimiter → ApiKeyMiddleware → Authentication → Authorization → Controllers
- **Error format**: All errors return `{ "error": "Human-readable message" }`
- **Success response shapes**: List endpoints return `{ articles[], total }` or `{ users[], total }`, mutations return `{ id, slug, title }` or `{ message }`, auth returns `{ token, user }`

### Frontend (`frontend/`)

- **Language**: TypeScript strict
- **State**: React Context (`AuthContext`, `ThemeContext`) — no Redux/Zustand
- **API calls**: `useApi` hook (`src/hooks/useApi.ts`) — auto-attaches JWT, auto-logout on 401
- **Routing**: React Router v7, `ProtectedRoute` + `RoleRoute` wrappers in `App.tsx`
- **Components**: `src/components/layout/` (AppShell, Sidebar, Header), `src/components/editor/` (TipTap)
- **Types**: `src/types/api.ts` — shared TypeScript interfaces for all API responses
- **Notifications**: `sonner` toast library — use `toast.success()` / `toast.error()` for user feedback
- **Error Boundary**: `src/components/error-boundary.tsx` wraps the app
- **Pages**: `src/pages/` — flat directory, one file per page
- **CSS**: Tailwind CSS v4, utility-first, `cn()` helper from `src/lib/utils.ts`
- **Icons**: `lucide-react` only
- **Port**: 5173, API proxy `/api/*` → `http://localhost:5174`

## File Locations

```
backend/
├── Controllers/          # API endpoints (11 controllers)
├── Auth/                 # JwtService, RbacService, ApiKeyMiddleware, Permissions, ClaimsPrincipalExtensions, RequirePermissionAttribute
├── Data/                 # AppDbContext, DbInitializer
├── Middleware/            # GlobalExceptionMiddleware
├── Models/
│   ├── Dtos.cs           # All request/response DTOs (C# records)
│   └── Entities/         # EF Core entity classes (11 models: User, Article, ArticleVersion, ArticleView, Tag, ArticleTag, ArticleVote, ArticleComment, ApiKey, SearchQuery, ArticleAttachment)
├── Migrations/           # EF Core migrations
├── Program.cs            # App configuration & DI
└── appsettings.json      # Connection strings, JWT config, RateLimiting

backend/Tests/
├── Integration/          # WebApplicationFactory integration tests
└── Unit/                 # Unit tests (RbacService, JwtService, etc.)

frontend/
├── src/contexts/         # AuthContext (JWT auth state)
├── src/hooks/            # useApi (fetch wrapper), useArticleImages (deferred upload)
├── src/types/            # Shared TypeScript API types
├── src/components/       # layout/ + editor/ + attachments/
├── src/pages/            # 15 page components
├── src/lib/utils.ts      # cn() helper
├── src/App.tsx           # Routes
└── vite.config.ts        # Proxy config

specs/                    # Detailed specifications (subordinate to this file)
├── api-surface.md        # Complete API contract
├── architecture.md       # System topology & layering
├── data-model.md         # ER diagram & entity details
├── frontend-structure.md # Component tree & dependencies
├── mission.md            # Project goals & success metrics
├── security.md           # Auth & RBAC detailed docs
├── smoke-tests.md        # Startup & smoke test checklist
└── tech-stack.md         # All packages & versions

.github/
└── copilot-instructions.md  # VS Code Copilot auto-loaded instructions
```

## Default Credentials

- **Email**: `admin@knowledge.local`
- **Password**: `admin123`

## Commands

| Task | Command |
|------|---------|
| Run backend | `cd backend && dotnet run` |
| Run frontend | `cd frontend && npm run dev` |
| Run backend tests | `cd backend/Tests && dotnet test` |
| Apply migrations | `cd backend && dotnet ef database update` |
| New migration | `cd backend && dotnet ef migrations add <Name>` |
| Build frontend | `cd frontend && npm run build` |
| Build backend | `cd backend && dotnet build` |

## RBAC Permission Matrix

| Permission | admin | editor | viewer |
|-----------|:-----:|:------:|:------:|
| `articles:create` | ✓ | ✓ | ✓ |
| `articles:edit_own` | ✓ | ✓ | ✓ |
| `articles:edit_any` | ✓ | | |
| `articles:delete_own` | ✓ | ✓ | ✓ |
| `articles:delete_any` | ✓ | | |
| `articles:publish` | ✓ | ✓ | |
| `articles:archive` | ✓ | ✓ | |
| `articles:approve` | ✓ | ✓ | |
| `tags:manage` | ✓ | ✓ | |
| `users:manage` | ✓ | | |
| `analytics:view` | ✓ | ✓ | |
| `api_keys:manage` | ✓ | | |

## Endpoint Authorization Matrix

| Endpoint | Method | Auth | Permission | Session-Only |
|----------|--------|:----:|-----------|:------------:|
| `/api/health` | GET | ✗ | — | — |
| `/api/auth/login` | POST | ✗ | — | — |
| `/api/auth/register` | POST | ✗ | — | — |
| `/api/auth/me` | GET | ✓ | — | ✗ |
| `/api/auth/profile` | PUT | ✓ | — | ✗ |
| `/api/articles` | GET | ✓ | — | ✗ |
| `/api/articles` | POST | ✓ | `articles:create` | ✗ |
| `/api/articles/{idOrSlug}` | GET | ✓ | — | ✗ |
| `/api/articles/{id}` | PUT | ✓ | `articles:edit_own` / `articles:edit_any` + `articles:publish` (for status→published) + `articles:archive` (for status→archived) | ✗ |
| `/api/articles/{id}` | DELETE | ✓ | `articles:delete_own` / `articles:delete_any` | ✗ |
| `/api/articles/{id}/approve` | POST | ✓ | `articles:approve` | ✗ |
| `/api/articles/{id}/reject` | POST | ✓ | `articles:approve` | ✗ |
| `/api/articles/{id}/versions` | GET | ✓ | — | ✗ |
| `/api/articles/{id}/versions/{versionId}` | GET | ✓ | — | ✗ |
| `/api/articles/{id}/versions/{versionId}/restore` | POST | ✓ | `articles:edit_own` / `articles:edit_any` | ✗ |
| `/api/articles/{id}/vote` | POST | ✓ | — | ✗ |
| `/api/articles/{id}/vote` | DELETE | ✓ | — | ✗ |
| `/api/articles/{id}/votes` | GET | ✓ | — | ✗ |
| `/api/articles/{id}/comments` | GET | ✓ | — | ✗ |
| `/api/articles/{id}/comments` | POST | ✓ | — | ✗ |
| `/api/articles/{id}/comments/{commentId}` | DELETE | ✓ | — | ✗ |
| `/api/articles/{id}/related` | GET | ✓ | — | ✗ |
| `/api/articles/{id}/attachments` | GET | ✓ | — | ✗ |
| `/api/articles/{id}/attachments` | POST | ✓ | `articles:edit_own` / `articles:edit_any` | ✗ |
| `/api/articles/{id}/attachments/{attachmentId}` | DELETE | ✓ | `articles:edit_own` / `articles:edit_any` | ✗ |
| `/api/attachments/{id}/download` | GET | ✓ | — | ✗ |
| `/api/tags` | GET | ✓ | — | ✗ |
| `/api/tags` | POST | ✓ | `tags:manage` | ✗ |
| `/api/tags` | PUT | ✓ | `tags:manage` | ✗ |
| `/api/tags?id={id}` | DELETE | ✓ | `tags:manage` | ✗ |
| `/api/search` | GET | ✓ | — | ✗ |
| `/api/search/click` | POST | ✓ | — | ✗ |
| `/api/analytics` | GET | ✓ | `analytics:view` | ✓ |
| `/api/dashboard` | GET | ✓ | — | ✗ |
| `/api/admin/users` | GET | ✓ | `users:manage` | ✓ |
| `/api/admin/users` | POST | ✓ | `users:manage` | ✓ |
| `/api/admin/users` | PUT | ✓ | `users:manage` | ✓ |
| `/api/admin/users?id={id}` | DELETE | ✓ | `users:manage` | ✓ |
| `/api/keys` | GET | ✓ | `api_keys:manage` | ✓ |
| `/api/keys` | POST | ✓ | `api_keys:manage` | ✓ |
| `/api/keys?id={id}` | DELETE | ✓ | `api_keys:manage` | ✓ |

## Validation Rules

| Field | Min | Max | Notes |
|-------|-----|-----|-------|
| `password` | 8 | 128 | Required for register, login, admin user create/update |
| `email` | — | — | Valid email format, unique per user |
| `user.name` | 1 | — | Required |
| `article.title` | 1 | 300 | Required |
| `article.excerpt` | — | — | Optional, trimmed |
| `article.status` | — | — | Enum: draft, pending, published, archived |
| `article.contentType` | — | — | Enum: reference, how-to, adr, runbook, faq, policy, onboarding |
| `article.difficulty` | — | — | Enum: beginner, intermediate, advanced |
| `tag.name` | 1 | 50 | Required, unique slug generated |
| `search.q` | 1 | — | Required |
| `search.limit` | 1 | 50 | Default 20 |
| `articles.limit` | 1 | 100 | Default 20 |
| `profile.name` | 1 | — | Required for profile update |
| `profile.newPassword` | 8 | 128 | Optional, requires currentPassword |
| `attachment.file` | 1 byte | 20MB | Required, extension whitelist enforced |
| `attachment.extensions` | — | — | Allowed: .png, .jpg, .jpeg, .gif, .webp, .pdf, .md, .txt, .docx, .xlsx, .yaml, .json, .csv, .svg |
| `attachment.maxPerArticle` | — | 20 | Configurable via appsettings.json |

## Feature Status

| Feature | Status | Notes |
|---------|--------|-------|
| JWT Auth | ✅ Implemented | 24h expiry, HMAC-SHA256 |
| API Key Auth | ✅ Implemented | kp_ prefix, BCrypt hash, prefix-indexed lookup |
| Articles CRUD | ✅ Implemented | Full lifecycle with versioning |
| Tags | ✅ Implemented | CRUD + article tagging |
| Search (fulltext) | ✅ Implemented | SQL LIKE on title/excerpt with wildcard escaping |
| Search (tag-based) | ✅ Implemented | @tag prefix syntax, multiple tags with AND logic |
| Search (semantic) | ⏳ Placeholder | Returns fulltext results, needs embedding model |
| Search (hybrid) | ⏳ Placeholder | Returns fulltext results, needs embedding model |
| Search (RAG) | ⏳ Placeholder | Returns stub message |
| Search Click Tracking | ✅ Implemented | POST /api/search/click records which result was clicked |
| Analytics | ✅ Implemented | Session-only endpoint |
| Admin Users | ✅ Implemented | Session-only, self-protection |
| API Key Management | ✅ Implemented | Create/list/delete |
| Article Feedback | ✅ Implemented | Vote (1 per user/article, toggle) + Comments (independent, multiple). Wilson Score. View count in responses. |
| Related Articles | ✅ Implemented | Tag-overlap based, GET /api/articles/{id}/related |
| Article Versions | ✅ Implemented | Created on content change |
| View Tracking | ✅ Implemented | Deduplicated per user/article/15min window |
| Rate Limiting | ✅ Implemented | Login, register, search endpoints |
| Health Check | ✅ Implemented | GET /api/health |
| OpenAPI/Swagger | ✅ Implemented | Available at /swagger in development |
| Read Time Calculation | ✅ Implemented | Auto-calculated from content (~200 wpm) |
| 404 Page | ✅ Implemented | NotFoundPage for unmatched routes |
| Version Diff | ✅ Implemented | Line-based diff comparison between versions |
| Dark Mode Toggle | ✅ Implemented | Light/Dark/System toggle, persisted to localStorage |
| Notifications | ❌ Not implemented | Bell icon is cosmetic only |
| User Profile Page | ✅ Implemented | Name/email update + password change via PUT /api/auth/profile |
| Pagination UI | ✅ Implemented | Articles list + Admin Users have prev/next controls |
| Article Attachments | ✅ Implemented | File upload/download/delete, TipTap image insert, max 20MB, extension whitelist |
| Avatar Upload | ❌ Not implemented | Avatar field exists but no upload endpoint |

## Known Frontend Gaps

No known gaps at this time.

## Key Behaviors

- **Slug regeneration**: When article title changes via PUT, slug is regenerated (if not conflicting)
- **Version creation**: Triggered when `content` field changes (not title-only or metadata-only edits)
- **Version restore**: POST `/api/articles/{id}/versions/{versionId}/restore` copies version content/title back to article, creates a new version with "Restored to version N" summary, recalculates read time
- **LastReviewedAt**: Automatically set to UTC now when article status becomes `published` (via direct update or approve action)
- **Read time calculation**: Auto-calculated from content text (~200 words/min), updated on create and content change
- **Viewer article visibility**: Viewers see published articles + their own (any status)
- **API key source**: Claims include `source: "api-key"` — session-only endpoints check this
- **Article list tags**: GET /api/articles response includes `tags` array per article
- **Tag input flexibility**: `Tags` array in create/update accepts tag ID, tag name, or tag slug — resolved in that priority order. When request comes via API key, unknown tags are auto-created.
- **Search wildcard escaping**: `%` and `_` characters are escaped in LIKE queries
- **Search multi-tag**: Multiple `@tag` prefixes can be used (e.g. `@react @typescript query`). Articles must match ALL specified tags (AND logic). Response returns `tags: string[]` array instead of single `tag` field.
- **Search click tracking**: Search responses include `searchQueryId` — clients POST `/api/search/click` with article clicked
- **View deduplication**: Same user viewing same article within 15 minutes counts as 1 view (hardcoded window)
- **Vote toggle**: POST `/api/articles/{id}/vote` with same `isHelpful` value → removes vote. Different value → changes vote. No existing vote → creates vote. One vote per user per article (unique constraint).
- **Vote reason**: `reason` field is only accepted when `isHelpful: false`. Free-text, optional.
- **Wilson Score**: Lower bound of Wilson score confidence interval (95%, z=1.96). Returned in article list and votes endpoint.
- **Comments independent of votes**: Users can leave comments without voting. Multiple comments per user allowed. Own comments can be deleted; admins can delete any.
- **View count in responses**: `GET /api/articles` list and `GET /api/articles/{idOrSlug}` detail both include `viewCount` field.
- **Tag upsert**: POST `/api/tags` returns 200 with existing tag if slug matches, 201 for newly created tag
- **Tag update**: PUT `/api/tags` renames tag and regenerates slug; returns 409 if new slug conflicts
- **Tag delete constraint**: DELETE `/api/tags?id=` returns 409 if tag has associated articles; only content-free tags can be deleted
- **Article GET supports slug**: `GET /api/articles/{idOrSlug}` accepts both article ID and slug for lookup
- **Publish/Archive enforcement**: Setting `status: "published"` requires `articles:publish` permission; `status: "archived"` requires `articles:archive`. Checked inline in ArticlesController PUT (not via attribute)
- **RBAC enforcement patterns**: Two patterns coexist: (1) `[RequirePermission("...")]` attribute for simple checks, (2) inline `RbacService.HasPermission()` for ownership-based or conditional checks (edit/delete/publish/archive)
- **Attachment upload**: Files stored on disk at `data/uploads/{articleId}/{storedFileName}`, metadata in `article_attachments` table. Extension whitelist + MIME validation enforced. Max 20MB/file, 20 files/article.
- **Attachment deferred upload**: Frontend uses deferred upload pattern — files are queued locally and only uploaded when the article is saved. New files show "Kaydedilince yüklenecek" badge with green background.
- **Attachment deferred delete**: In edit mode, deleting a file marks it with strikethrough + "Kaydedilince silinecek" badge. Undo is available. Actual API DELETE happens on save.
- **Image deferred upload**: Images pasted/dropped into TipTap editor are stored as blob URLs temporarily. On save, blob URLs are replaced with real `/api/attachments/{id}/download` URLs after upload.
- **Attachment cascade**: Article deletion removes all attachment DB records (cascade) AND physical files from disk
- **Attachment download**: Served via controller (auth required), not static file middleware. `PhysicalFile()` streams the file with correct Content-Type and Content-Disposition.

## Placeholder Fields (Not Yet Active)

These entity fields exist in the database but are not yet used in business logic:

| Field | Entity | Purpose | Status |
|-------|--------|---------|--------|
| `IndexedAt` | Article | Timestamp for semantic search indexing | Never set — awaiting embedding model integration |
| `ReviewIntervalDays` | Article | Configurable staleness threshold per article | Has DB default (90) but analytics uses hardcoded 90 days |

## Rules for AI Agents

### MUST DO
- Reference this file as the primary source of truth
- Use `Permissions` constants (not magic strings) for RBAC checks
- Use `useApi` hook for all authenticated frontend API calls
- Use `toast` from `sonner` for all user feedback (success/error)
- Use `[Authorize]` + `RequirePermission` for backend RBAC (or inline `RbacService.HasPermission()` for ownership/conditional checks)
- Use `RoleRoute` wrapper for frontend role-restricted pages
- Keep pages in `frontend/src/pages/` as flat files
- Return `{ "error": "..." }` for all error responses
- Write xUnit integration tests for new backend features
- **After EVERY change, synchronize documentation** (see Documentation Sync Rules below)

### DOCUMENTATION SYNC RULES

> **Her geliştirme sonrası aşağıdaki kurallar uygulanmalıdır. Atlama kabul edilmez.**

#### Trigger → Action Matrix

| Değişiklik | Güncellenmesi Gereken Bölümler |
|-----------|-------------------------------|
| Yeni endpoint eklendi | Endpoint Authorization Matrix, `specs/api-surface.md` |
| Endpoint kaldırıldı | Endpoint Authorization Matrix, `specs/api-surface.md`'den sil |
| Yeni entity/field eklendi | File Locations (gerekirse), `specs/data-model.md` |
| Entity/field kaldırıldı | `specs/data-model.md`'den sil, Validation Rules'dan sil |
| Permission eklendi/kaldırıldı | RBAC Permission Matrix |
| Yeni sayfa eklendi | File Locations (frontend), `specs/frontend-structure.md` |
| Sayfa kaldırıldı | File Locations'dan sil, `specs/frontend-structure.md`'den sil |
| Yeni feature tamamlandı | Feature Status tablosu → ✅ |
| Feature kaldırıldı | Feature Status tablosundan satırı sil |
| Yeni validation kuralı | Validation Rules tablosu |
| DTO değişti (backend) | `frontend/src/types/api.ts` güncelle |
| Yeni component eklendi | `specs/frontend-structure.md` directory layout |
| Component kaldırıldı | `specs/frontend-structure.md`'den sil |
| Config değişikliği (appsettings) | İlgili bölümde belirt (Commands, Conventions vb.) |
| Yeni paket/kütüphane eklendi | `specs/tech-stack.md` |
| Paket kaldırıldı | `specs/tech-stack.md`'den sil |
| Known Frontend Gap kapatıldı | Known Frontend Gaps tablosundan satırı sil |
| Yeni bilinen eksiklik | Known Frontend Gaps tablosuna ekle |

#### Kurallar

1. **Ekleme**: Yeni bir özellik/endpoint/entity eklendiğinde, ilgili tüm dokümantasyon bölümleri güncellenir. Eğer dosya yoksa oluşturulur.
2. **Kaldırma**: Bir özellik/endpoint/entity kaldırıldığında, ilgili dokümantasyon satırları/bölümleri silinir. Ölü referans bırakılmaz.
3. **Değişiklik**: Mevcut davranış değiştiğinde (ör. validation kuralı, enum değeri), eski bilgi güncellenir.
4. **Tutarlılık**: `specs/` dosyaları AGENTS.md ile çelişemez. Çelişki durumunda AGENTS.md geçerlidir ve `specs/` düzeltilir.
5. **types/api.ts senkronizasyonu**: Backend DTO değişikliği → `frontend/src/types/api.ts` aynı commit'te güncellenir.
6. **Ölü kod/field temizliği**: Kullanılmayan entity field, component veya endpoint kaldırıldığında hem kod hem dokümantasyondan silinir.
7. **Feature Status doğruluğu**: Yarım kalan iş "✅ Implemented" olarak işaretlenmez. Sadece tam çalışan özellikler işaretlenir.

#### Post-Change Validation Checklist (Zorunlu)

> **Her conversation'ın SONUNDA** aşağıdaki checklist uygulanır. Uyumsuzluk varsa düzeltmeden conversation kapatılmaz.

- [ ] Yeni/değişen endpoint → Endpoint Authorization Matrix'te var mı?
- [ ] Yeni/değişen endpoint → `specs/api-surface.md`'de var mı?
- [ ] Yeni sayfa/component → `specs/frontend-structure.md`'de var mı?
- [ ] Feature tamamlandı → Feature Status ✅ olarak güncellendi mi?
- [ ] Known Gap kapatıldı → Known Frontend Gaps'ten satır silindi mi?
- [ ] DTO değişti → `frontend/src/types/api.ts` güncellendi mi?
- [ ] Validation kuralı değişti → Validation Rules tablosu güncellendi mi?
- [ ] Yeni paket → `specs/tech-stack.md`'ye eklendi mi?
- [ ] File Locations ağacı hâlâ doğru mu? (sayfa sayısı, controller sayısı vb.)

### MUST NOT
- Do NOT add Next.js, SSR, or server components — pure SPA + REST API
- Do NOT use ASP.NET Identity — auth is custom JWT via `JwtService`
- Do NOT add Redux, Zustand, or other state managers — use React Context
- Do NOT add component libraries (MUI, Chakra, etc.) — use Tailwind CSS + lucide-react + sonner
- Do NOT use magic permission strings — use `Permissions` class constants
- Do NOT store JWT in httpOnly cookies — current design uses localStorage (accepted trade-off for SPA)

### CONVENTIONS
- DB column names are snake_case; C# properties are PascalCase
- Viewers CAN create articles (status limited to draft/pending on create; publish/archive blocked by permission on update)
- Password minimum length is 8 characters everywhere
- All IDs are 21-character truncated GUIDs (hex, lowercase)
- Timestamps are ISO 8601 UTC in API responses
- `useApi` hook returns `{ fetchWithAuth }` — callers chain `.then(r => r.json())` for data
- Query-param based DELETE: Tags (`?id=`), Admin Users (`?id=`), API Keys (`?id=`)
- Admin Users PUT: `userId` is in the request body, not in the URL path
- Import API types from `src/types/api.ts` for type safety
- Rate limits are configurable via `appsettings.json` → `RateLimiting:AuthLimit` / `RateLimiting:SearchLimit`
