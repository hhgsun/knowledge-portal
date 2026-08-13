# Knowledge Portal — AI-First Development Guide

> **Bu dosya projenin tek doğruluk kaynağıdır (Single Source of Truth).**
> Tüm AI agent'ları bu dosyayı referans almalıdır.
> Detaylı spec'ler `specs/` klasöründedir — bu dosya ile çelişirse **bu dosya geçerlidir**.

---

## Architecture

Split monorepo: `backend/` (ASP.NET Core Web API) + `frontend/` (React SPA).

| Layer | Stack |
|-------|-------|
| Backend | ASP.NET Core (.NET 10), EF Core, PostgreSQL |
| Auth | JWT Bearer + API Key (`X-API-Key: kp_` prefix) + Azure AD (MSAL v5 redirect-bridge) |
| Frontend | React 19, Vite, React Router v7, Tailwind CSS v4 |
| Editor | Milkdown Crepe (ProseMirror); canonical CommonMark/GFM Markdown |
| Tests | xUnit + WebApplicationFactory (backend only). **No Docker** — the entire suite runs on EF Core InMemory (isolated DB per test class) with deterministic fakes: `FakeEmbeddingGenerator`/`FakeChatClient` replace Ollama and `FakeVectorSearchService` replaces the pgvector search (`IVectorSearchService`). The app is provider-aware: on a non-relational provider it uses `EnsureCreated` (not migrations), FTS falls back to an in-memory accent-folded AND→OR substring search, and the embedding background service is removed in tests. Postgres-only fidelity (snowball stemming, real pgvector ranking) is therefore not covered by tests. CI runs `dotnet test` as a gating stage in `azure-pipelines.yml` |
| MCP | REST API at `/mcp` (JSON-RPC 2.0 spec-compliant, **NO OAuth**, API Key or JWT auth only, stateless, tool discovery via `initialize` + `tools/list`) |

## Conventions

### Backend (`backend/`)

- **Language**: C# 13, .NET 10, nullable enabled
- **Pattern**: Controllers → Services → EF Core DbContext → PostgreSQL. Shared domain logic lives in `Services/` (ArticleService, TagService, ApiKeyService, UserService, StatsService) — controllers keep only routing, auth scoping, and response shaping. **No duplicated logic across controllers**; if two endpoints need the same behavior, extract it into a service (or a `Helpers/` static for pure functions). Service failures return `ServiceError` (mapped via `ToActionResult()`).
- **Naming**: PascalCase for C# code, snake_case for DB columns (configured in `AppDbContext.OnModelCreating`)
- **Auth**: `[Authorize]` attribute on controllers, `[AllowAnonymous]` for public endpoints
- **RBAC**: `RequirePermission` attribute with permission constants from `Permissions` class
- **API prefix**: All routes under `/api/` (e.g. `/api/articles`, `/api/auth/login`)
- **Entities**: `backend/Models/Entities/` — 14 models: User (with AzureObjectId, Slug), Article, ArticleVersion, ArticleView, Tag, ArticleTag, ArticleVote, ArticleComment, ApiKey, SearchQuery, ArticleAttachment, LookupValue, ArticleEmbedding, IndexJob
- **Enum Validation**: `contentType` is validated server-side against `lookup_values` table (DB-driven, managed via `/api/lookups`)
- **Seed data**: `DbInitializer.SeedAsync()` — admin user + 10 default tags
- **Port**: 5174
- **Rate Limiting**: ASP.NET Core built-in rate limiter on auth + search + MCP endpoints (defaults: auth=10/min, search=30/min, mcp=60/min, configurable via `appsettings.json` → `RateLimiting`). **Partitioned per client**: partition key = `apiKeyId` claim > `id` (user) claim > client IP — one noisy caller can't exhaust everyone's budget; login brute-force throttled per source IP (requires ForwardedHeaders for real IPs behind the proxy)
- **Middleware pipeline**: ForwardedHeaders → HSTS (non-dev) → SecurityHeaders (nosniff/DENY/Referrer-Policy) → GlobalExceptionMiddleware → CORS → ApiKeyMiddleware → Authentication → RateLimiter → Authorization → Controllers. RateLimiter runs after auth so partitioning sees the principal; ForwardedHeaders first so everything sees real client IP/scheme (`ForwardedHeaders:KnownProxies`/`KnownNetworks` config; TLS terminates at the company reverse proxy — no in-app HTTPS redirect)
- **AI/Search**: Ollama integration (optional, `Ollama:Enabled` in appsettings.json). Embedding model: bge-m3 (1024 dims; dimension guard before persistence). Chat model: qwen2.5vl:7b. PostgreSQL-backed durable `index_jobs` queue coalesces edits by article with a generation counter; backend workers claim jobs via `FOR UPDATE SKIP LOCKED`, use leases, bounded parallelism, exponential retry, and terminal failure tracking. Each job synchronizes FTS and semantic indexes. Article body and each attachment are embedded as separate provenance-bearing sources, fairly interleaved under per-source/total chunk caps. Hybrid search retrieves a configurable 200-candidate pool, merges with RRF, then applies `ISearchReranker` (`LocalSearchReranker` by default). pgvector HNSW and PostgreSQL GIN/tsvector provide vector and lexical retrieval.
- **Error format**: All errors return `{ "error": "Human-readable message" }`
- **Success response shapes**: List endpoints return `{ articles[], total }` or `{ users[], total }`, mutations return `{ id, slug, title }` or `{ message }`, auth returns `{ token, user }`

### Frontend (`frontend/`)

- **Language**: TypeScript strict
- **State**: React Context (`AuthContext`, `ThemeContext`) — no Redux/Zustand
- **API calls**: `useApi` hook (`src/hooks/useApi.ts`) — auto-attaches JWT, auto-logout on 401
- **Routing**: React Router v7, `ProtectedRoute` + `RoleRoute` wrappers in `App.tsx`
- **Components**: `src/components/layout/` (AppShell, Sidebar, Header), `src/components/editor/` (Milkdown Crepe)
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
├── Controllers/          # API endpoints (15 controllers)
├── Auth/                 # JwtService, RbacService, ApiKeyMiddleware, ApiKeyGenerator, Permissions, ClaimsPrincipalExtensions, RequirePermissionAttribute, RequireSessionAuthAttribute
├── Data/                 # AppDbContext, DbInitializer, SlugQueries (unique slug generation)
├── Middleware/            # GlobalExceptionMiddleware
├── Helpers/              # ContentExtractor, AttachmentTextExtractor, SlugHelper, AttachmentHelper, ArticleQueryExtensions, RrfHelper
├── Mcp/                  # MCP types (McpTypes), tool executor (McpToolExecutor)
├── Services/             # Domain: ArticleService, TagService, ApiKeyService, UserService, StatsService, ServiceError
│                         # AI/Search: EmbeddingService, VectorSearchService (IVectorSearchService), RagService, EmbeddingBackgroundService, IndexJobQueue, FullTextSearchService, SearchReranker
│                         # Observability: PortalMetrics (OpenTelemetry meters)
├── Models/
│   ├── Dtos.cs           # All request/response DTOs (C# records)
│   └── Entities/         # EF Core entity classes (14 models; includes durable IndexJob queue)
├── Migrations/           # EF Core migrations
├── Program.cs            # App configuration & DI
└── appsettings.json      # Connection strings, JWT config, RateLimiting

backend/Tests/
├── Integration/          # WebApplicationFactory integration tests
└── Unit/                 # Unit tests (RbacService, JwtService, etc.)

frontend/
├── src/contexts/         # AuthContext (JWT auth state)
├── src/hooks/            # useApi (fetch wrapper), useArticleImages (deferred upload), useLookups (content types & difficulties)
├── src/types/            # Shared TypeScript API types
├── src/components/       # layout/ + editor/ + attachments/
├── src/pages/            # 18 page components
├── src/lib/utils.ts      # cn() helper
├── src/App.tsx           # Routes
├── auth-popup-callback.html  # Vite multi-page entry: Azure AD popup redirect target
└── vite.config.ts        # Proxy + multi-page build config

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

- **Email**: `admin@finagotech.com.tr`
- **Password**: `1q2w3E*/`

## Seed Data

When the backend starts (`dotnet run`), it automatically seeds the database:
1. **Admin user** created if missing (email: `admin@finagotech.com.tr`, password: `1q2w3E*/`, role: admin)
2. **Default tags** added (project-knowledge-portal, getting-started, tutorial, etc.)
3. **Content types** added (reference, how-to, adr, runbook, faq, policy, onboarding)
4. **Articles** loaded from `backend/SeedData/articles/*.md` if Articles table is empty
   - Files are processed in order by filename
   - Article metadata is stored as JSON-compatible YAML front matter; the body is canonical Markdown for Milkdown
   - Each article's tags are assigned automatically
   - Content is stored as canonical CommonMark/GFM Markdown (`contentMarkdown` in API payloads)
   - Slug is auto-generated from title (with collision detection)
   - Articles marked as "published" get `PublishedAt` and `LastReviewedAt` timestamps

**To reset seed data**: Drop the PostgreSQL database, then restart backend (it recreates and reseeds automatically).

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
| `api_keys:manage` | ✓ | ✓ | ✓ |
| `api_keys:manage_any` | ✓ | | |
| `featured_links:manage` | ✓ | | |

**API key effective permissions**: a `source=api-key` principal carries at most **editor** authority — an admin-owned key acts as editor (no `users:manage`, `articles:edit_any/delete_any`, `api_keys:manage_any`); editor/viewer-owned keys keep their owner's role. On top of the cap, **all delete permissions are removed** (`articles:delete_own/any` always denied) and destructive DELETE endpoints are `[RequireSessionAuth]` (see matrix below). All view/read operations follow the effective role. Exception: `DELETE /api/articles/{id}/vote` (removing one's own vote) stays available to keys — it is an interaction toggle, not content deletion.

## Endpoint Authorization Matrix

| Endpoint | Method | Auth | Permission | Session-Only |
|----------|--------|:----:|-----------|:------------:|
| `/api/health` | GET | ✗ | — | — |
| `/api/health/live` | GET | ✗ | — | — |
| `/metrics` | GET | ✗ | — | — (Prometheus; not proxied by nginx — internal network only) |
| `/api/auth/login` | POST | ✗ | — | — |
| `/api/auth/register` | POST | ✗ | — | — |
| `/api/auth/azure-login` | POST | ✗ | — | — |
| `/api/auth/me` | GET | ✓ | — | ✗ |
| `/api/auth/profile` | PUT | ✓ | — | ✗ |
| `/api/articles` | GET | ✓ | — | ✗ |
| `/api/articles` | POST | ✓ | `articles:create` | ✗ |
| `/api/articles/{idOrSlug}` | GET | ✓ | — | ✗ |
| `/api/articles/{id}` | PUT | ✓ | `articles:edit_own` / `articles:edit_any` + `articles:publish` (for status→published) + `articles:archive` (for status→archived) | ✗ |
| `/api/articles/{id}` | DELETE | ✓ | `articles:delete_own` / `articles:delete_any` | ✓ |
| `/api/articles/{id}/approve` | POST | ✓ | `articles:approve` | ✗ |
| `/api/articles/{id}/approve` | DELETE | ✓ | `articles:approve` | ✗ |
| `/api/articles/{id}/reject` | POST | ✓ | `articles:approve` | ✗ (legacy alias for removing approval) |
| `/api/articles/{id}/versions` | GET | ✓ | — | ✗ |
| `/api/articles/{id}/versions/{versionId}` | GET | ✓ | — | ✗ |
| `/api/articles/{id}/versions/{versionId}/restore` | POST | ✓ | `articles:edit_own` / `articles:edit_any` | ✗ |
| `/api/articles/{id}/vote` | POST | ✓ | — | ✗ |
| `/api/articles/{id}/vote` | DELETE | ✓ | — | ✗ |
| `/api/articles/{id}/votes` | GET | ✓ | — | ✗ |
| `/api/articles/{id}/comments` | GET | ✓ | — | ✗ |
| `/api/articles/{id}/comments` | POST | ✓ | — | ✗ |
| `/api/articles/{id}/comments/{commentId}` | DELETE | ✓ | — | ✓ |
| `/api/articles/{id}/related` | GET | ✓ | — | ✗ |
| `/api/articles/{id}/attachments` | GET | ✓ | — | ✗ |
| `/api/articles/{id}/attachments` | POST | ✓ | `articles:edit_own` / `articles:edit_any` | ✗ |
| `/api/articles/{id}/attachments/{attachmentId}` | DELETE | ✓ | `articles:edit_own` / `articles:edit_any` | ✓ |
| `/api/attachments/{id}/download` | GET | ✓ | — | ✗ |
| `/api/tags` | GET | ✓ | — | ✗ |
| `/api/tags` | POST | ✓ | `tags:manage` | ✗ |
| `/api/tags` | PUT | ✓ | `tags:manage` | ✗ |
| `/api/tags?id={id}` | DELETE | ✓ | `tags:manage` | ✓ |
| `/api/search` | GET | ✓ | — | ✗ |
| `/api/search/authors` | GET | ✓ | — | ✗ |
| `/api/search/click` | POST | ✓ | — | ✗ |
| `/api/search/reindex` | POST | ✓ | `users:manage` | ✓ |
| `/api/search/embedding-status` | GET | ✓ | `users:manage` | ✓ |
| `/api/search/storage-status` | GET | ✓ | `users:manage` | ✓ |
| `/api/analytics` | GET | ✓ | `analytics:view` | ✓ |
| `/api/dashboard` | GET | ✓ | — | ✗ |
| `/api/admin/users` | GET | ✓ | `users:manage` | ✓ |
| `/api/admin/users` | POST | ✓ | `users:manage` | ✓ |
| `/api/admin/users` | PUT | ✓ | `users:manage` | ✓ |
| `/api/admin/users?id={id}` | DELETE | ✓ | `users:manage` | ✓ |
| `/api/keys` | GET | ✓ | `api_keys:manage` | ✓ |
| `/api/keys` | POST | ✓ | `api_keys:manage` | ✓ |
| `/api/keys/{id}/rotate` | POST | ✓ | `api_keys:manage` | ✓ |
| `/api/keys?id={id}` | DELETE | ✓ | `api_keys:manage` | ✓ |
| `/api/admin/keys` | GET | ✓ | `api_keys:manage_any` | ✓ |
| `/api/admin/keys` | POST | ✓ | `api_keys:manage_any` | ✓ |
| `/api/admin/keys` | PUT | ✓ | `api_keys:manage_any` | ✓ |
| `/api/admin/keys?id={id}` | DELETE | ✓ | `api_keys:manage_any` | ✓ |
| `/api/featured-links` | GET | ✓ | — | ✗ |
| `/api/featured-links` | POST | ✓ | `featured_links:manage` | ✗ |
| `/api/featured-links` | PUT | ✓ | `featured_links:manage` | ✗ |
| `/api/featured-links?id={id}` | DELETE | ✓ | `featured_links:manage` | ✓ |
| `/api/lookups` | GET | ✓ | — | ✗ |
| `/api/lookups` | POST | ✓ | `tags:manage` | ✗ |
| `/api/lookups` | PUT | ✓ | `tags:manage` | ✗ |
| `/api/lookups?id={id}` | DELETE | ✓ | `tags:manage` | ✓ |
| `/api/logs` | GET | ✓ | `users:manage` | ✓ |
| `/api/logs/{fileName}` | GET | ✓ | `users:manage` | ✓ |
| `/api/logs/{fileName}` | DELETE | ✓ | `users:manage` | ✓ |
| `/mcp` | GET | ✓ | — | ✗ |
| `/mcp` | POST | ✓ | — | ✗ |

## Validation Rules

| Field | Min | Max | Notes |
|-------|-----|-----|-------|
| `password` | 8 | 128 | Required for register, login, admin user create/update |
| `email` | — | — | Valid email format, unique per user |
| `user.name` | 1 | — | Required |
| `article.title` | 1 | 300 | Required |
| `article.excerpt` | — | — | Optional, trimmed |
| `article.status` | — | — | Enum: draft, published, archived. All roles may publish; approval is independent. |
| `article.contentType` | — | — | DB-driven via `lookup_values` table (category: content_type) |
| `tag.name` | 1 | 50 | Required, unique slug generated |
| `search.q` | 1 | — | Required |
| `search.limit` | 1 | 50 | Default 20 |
| `search.page` | 1 | — | Default 1. Applies to fulltext + tag-browse; semantic/hybrid are top-N only |
| `search.onlyOwnContent` | — | — | Optional, boolean. When true + API key auth → filters to articles created by that API key |
| `search.includeContent` | — | — | Optional, boolean. When true → includes article content as plain text (derived from canonical Markdown) in search results |
| `search.includeAttachments` | — | — | Optional, boolean. When true → includes attachment metadata (id, fileName, contentType, sizeBytes, downloadUrl) per article in search results |
| `search.tag` | — | — | Optional, repeatable, tag slugs (merged with #syntax) |
| `search.author` | — | — | Optional, repeatable, user slugs (merged with @syntax) |
| `search.contentType` | — | — | Optional, repeatable, content type values (merged with ##syntax) |
| `articles.limit` | 1 | 100 | Default 20 |
| `articles.onlyOwnContent` | — | — | Optional, boolean. When true + API key auth → filters to articles created by that API key |
| `articles.includeContent` | — | — | Optional, boolean. When true → includes article content as plain text in list results |
| `articles.includeAttachments` | — | — | Optional, boolean. When true → includes attachment metadata per article in list results |
| `profile.name` | 1 | — | Required for profile update |
| `profile.newPassword` | 8 | 128 | Optional, requires currentPassword (not required for Azure users first-time set) |
| `attachment.file` | 1 byte | 20MB | Required, extension whitelist enforced |
| `attachment.extensions` | — | — | Allowed: .png, .jpg, .jpeg, .gif, .webp, .pdf, .md, .txt, .docx, .xlsx, .yaml, .json, .csv, .svg |
| `attachment.maxPerArticle` | — | 20 | Configurable via appsettings.json |

## Feature Status

| Feature | Status | Notes |
|---------|--------|-------|
| JWT Auth | ✅ Implemented | 24h expiry, HMAC-SHA256 |
| Azure AD Auth | ✅ Implemented | MSAL v5 redirect-bridge popup, auto-creates user from Azure profile |
| API Key Auth | ✅ Implemented | kp_ prefix, BCrypt hash, prefix-indexed lookup |
| Articles CRUD | ✅ Implemented | Full lifecycle with versioning |
| Tags | ✅ Implemented | CRUD + article tagging |
| Search (fulltext) | ✅ Implemented | PostgreSQL tsvector/tsquery (`turkish` config: stemming + stopwords) with GIN index and weighted ranking, content body indexed, Turkish accent folding via C# transliteration. Multi-word queries are AND-first (all terms must match), retrying with OR then ILIKE when empty. Paged (`page` param) with true post-filter `total`/`totalPages` and match-context `snippet` per result |
| Search (tag-based) | ✅ Implemented | @tag prefix syntax, multiple tags with AND logic |
| Search (semantic) | ✅ Implemented | Ollama embedding (bge-m3, 1024 dims) + chunking (~500 words/chunk) + pgvector cosine distance, best-chunk scoring (returns matched chunk index) |
| Search (hybrid) | ✅ Implemented | Reciprocal Rank Fusion (α=0.4 fulltext + β=0.6 semantic, k=60, `Helpers/RrfHelper`) |
| Search (RAG) | ✅ Implemented | Configured Ollama chat model (default qwen2.5vl:7b), top-3 matched-chunk context (configurable via `Ollama:RagSourceLimit`, attachments included), source citations, search filters applied, prompt-injection-hardened `<source>` context blocks |
| Search Click Tracking | ✅ Implemented | POST /api/search/click records which result was clicked |
| Analytics | ✅ Implemented | Session-only endpoint |
| Admin Users | ✅ Implemented | Session-only, self-protection |
| API Key Management | ✅ Implemented | Self-service create/list/rotate/delete (all roles, ProfilePage); admin CRUD over all users' keys (`/api/admin/keys`, `/admin/keys`) |
| Article Feedback | ✅ Implemented | Vote (1 per user/article, toggle) + Comments (independent, multiple). Wilson Score. View count in responses. |
| Related Articles | ✅ Implemented | Tag-overlap based, GET /api/articles/{id}/related |
| Article Versions | ✅ Implemented | Created on content change |
| View Tracking | ✅ Implemented | Deduplicated per user/article/15min window |
| Rate Limiting | ✅ Implemented | Login, register, search, MCP endpoints — partitioned per API key/user/IP |
| Health Check | ✅ Implemented | GET /api/health (readiness: 503 "unhealthy" when DB unreachable, 200 "degraded" when only Ollama down, else "healthy") + GET /api/health/live (liveness, always 200) |
| Metrics | ✅ Implemented | OpenTelemetry → Prometheus at /metrics (not proxied by nginx — internal only): ASP.NET Core instrumentation + `kp_pending_embeddings` gauge + `kp_embedding_failures` counter |
| OpenAPI/Swagger | ✅ Implemented | Available at /swagger in development |
| Read Time Calculation | ✅ Implemented | Auto-calculated from content (~200 wpm) |
| 404 Page | ✅ Implemented | NotFoundPage for unmatched routes |
| Version Diff | ✅ Implemented | Line-based diff comparison between versions |
| Dark Mode Toggle | ✅ Implemented | Light/Dark/System toggle, persisted to localStorage |

| User Profile Page | ✅ Implemented | Name/email update + password change via PUT /api/auth/profile |
| Pagination UI | ✅ Implemented | Articles list + Admin Users have prev/next controls |
| Article Attachments | ✅ Implemented | File upload/download/delete, Milkdown image insert, max 20MB, extension whitelist |
| System Logs | ✅ Implemented | Serilog: console + rolling daily JSON file (CompactJsonFormatter, same log_YYYYMMDD.log naming), retention `Logging:RetainedFileCountLimit` (default 30), min level via `Serilog:MinimumLevel` config. View/delete via admin UI, today's log protected |

## Known Frontend Gaps

No known gaps at this time.

## Key Behaviors

- **Slug regeneration**: When article title changes via PUT, slug is regenerated (if not conflicting)
- **Version creation**: Triggered when `content` field changes (not title-only or metadata-only edits)
- **Version restore**: POST `/api/articles/{id}/versions/{versionId}/restore` copies version content/title back to article, creates a new version with "Restored to version N" summary, recalculates read time
- **Publication and approval**: All roles may publish without review. Approval is an optional trust signal on already-published content and records `ApprovedById`/`ApprovedAt`; removing approval never unpublishes the content.
- **LastReviewedAt**: Set to UTC when an authorized editor/admin approves an article; direct publication does not imply review.
- **Read time calculation**: Auto-calculated from content text (~200 words/min), updated on create and content change
- **Viewer article visibility**: Viewers see published articles + their own (any status)
- **Azure AD login**: Frontend uses MSAL.js v5 redirect-bridge popup flow → popup opens → Azure AD auth → popup calls `broadcastResponseToMainFrame()` via BroadcastChannel → parent receives auth code → PKCE exchange → gets access token → POST `/api/auth/azure-login` → backend validates via Microsoft Graph `/me` → finds/creates local user by AzureObjectId or email → returns local JWT. Popup callback page: `/auth-popup-callback.html` (Vite multi-page entry). If user has active Azure session, login page auto-attempts silent login.
- **Azure AD logout**: `msalInstance.clearCache()` called on logout to prevent auto-silent re-login.
- **Azure AD user linking**: First Azure login links by email if user exists, otherwise creates new viewer user. AzureObjectId stored for future logins. Profile name synced from Azure on each login.
- **Azure AD password set**: Azure users can set a local password via PUT `/api/auth/profile` without providing `currentPassword` (first-time set). After setting, both Azure and email+password login work.
- **`/api/auth/me` response**: Includes `isAzureUser` boolean field (true if user has AzureObjectId linked).
- **API key source**: Claims include `source: "api-key"` — session-only endpoints check this
- **API key permission cap**: RBAC checks are principal-aware (`RbacService.HasPermission(ClaimsPrincipal, …)`, `CanEdit/CanDelete/CanViewArticle(ClaimsPrincipal, …)`). For `source=api-key`: effective role = owner role capped at editor (admin→editor), all delete permissions denied, `CanDeleteArticle` always false. Destructive DELETE endpoints additionally carry `[RequireSessionAuth]`. Vote removal (own vote) remains key-accessible.
- **Article list tags**: GET /api/articles response includes `tags` array per article
- **Tag input flexibility**: `Tags` array in create/update accepts tag ID, tag name, or tag slug — resolved in that priority order. Unknown tags are auto-created for API-key requests and session users with `tags:manage`; creation is deferred to the article save and committed in the same `SaveChanges` call, so abandoning the form cannot leave orphan tags.
- **Search wildcard escaping**: `%` and `_` characters are escaped in LIKE queries
- **Search query semantics**: Multi-word queries are joined with AND (all terms must match, precision-first). When AND yields nothing, the query retries with OR (any term), then falls back to ILIKE on title/excerpt. tsquery meta-characters are stripped from tokens.
- **Search pagination**: `GET /api/search` accepts `page` (default 1). Fulltext and tag-browse responses return the true post-filter `total` plus `page`/`totalPages` (filters are applied to the full ranked candidate set — capped at 1000 FTS candidates — before paging, so filtered searches don't under-return). FTS ordering is deterministic (`rank DESC, Id` tiebreaker) so pages never overlap on rank ties. Semantic/hybrid remain top-N (`page`/`totalPages` fixed at 1, `total` = returned count).
- **Search snippet**: Non-RAG search results include a `snippet` field — a ~240-char match-context window from the article body around the earliest query-term occurrence (accent/case-folded matching mirroring the FTS index, stem-prefix tolerant). `null` when no term occurs in the body (e.g. title-only match) — clients fall back to `excerpt`. Frontend highlights query terms in the snippet.
- **Plain-text extraction scope**: `ContentExtractor` strips Markdown syntax while retaining readable headings, table cells, code, image alt text, and link labels. Link/image URLs and formatting syntax are excluded from read-time calculation, search indexes, embeddings, and `contentText`/`includeContent` output.
- **Search inline syntax**: `@user-slug` for author filter (OR, multiple), `#tag-slug` for tag filter (AND, multiple), `##content-type` for content type filter (OR, multiple). Parsed in order: `##` → `#` → `@` → remaining text. Example: `@ahmet #react ##guide nasıl yapılır`. Inline syntax and query parameters are merged.
- **Search filters**: `GET /api/search` accepts optional query parameters: `onlyOwnContent` (boolean, API key auth only — filters to articles created by that API key), `includeContent` (boolean — includes article content as extracted plain text in results), `includeAttachments` (boolean — includes attachment metadata array per article), `tag` (repeatable, tag slugs), `author` (repeatable, user slugs), `contentType` (repeatable, content type values). Filters apply to all search types (fulltext, semantic, hybrid, rag). Tags from `#syntax` and `tag` param are merged. Authors from `@syntax` and `author` param are merged. Content types from `##syntax` and `contentType` param are merged. If only tags are specified without a text query, returns tag-browse results.
- **Search click tracking**: Search responses include `searchQueryId` — clients POST `/api/search/click` with article clicked
- **Search semantic**: pgvector cosine distance operator (`<=>`) on `vector(1024)` column in `article_embeddings` table, accelerated by an HNSW index (`ix_article_embeddings_embedding_hnsw`). Query over-fetches chunk rows (published-only via JOIN), best chunk per article is picked in memory (its index returned for RAG). MinSimilarityScore=0.5 (configurable via appsettings.json).
- **Search hybrid**: Reciprocal Rank Fusion (α=0.4 fulltext + β=0.6 semantic, k=60). Both legs over-fetch (limit×3, cap 50) so post-merge filters don't starve the final `Take(limit)`. Each result has `matchType` (fulltext/semantic/both). Falls back to fulltext-only if Ollama unavailable.
- **Search RAG**: Chunk-level retrieval uses provenance-bearing article/attachment chunks. Narrow questions use `RagCandidateLimit` and a small distinct-source cap; broad intents use `RagBroadCandidateLimit` and map-reduce over the wider pool. Attachment source names appear in prompt citations. Filters are applied inside vector retrieval and rechecked against article metadata.
- **Search indexing**: Mutations set `IndexedAt=null` when appropriate and upsert one durable `index_jobs` row per article. A generation increment prevents an older in-flight worker from acknowledging a newer edit. Workers claim with `FOR UPDATE SKIP LOCKED`, process with configurable parallelism, recover expired leases, retry with persisted exponential backoff, and expose pending/failed state through diagnostics. Full reindex populates the same durable queue instead of starting an untracked task.
- **Search indexing concurrency**: `IndexedAt` is claimed with an optimistic conditional update (`UPDATE ... WHERE xmin = <captured>`). The queue generation guard independently prevents stale completion; together they cover edits both during embedding and around job acknowledgement.
- **Search responses**: All search types include `indexingPending` boolean (true if any published article has IndexedAt=null). Semantic/hybrid/rag include `warning` string when Ollama unavailable.
- **MCP search parity**: `search_articles` exposes the same fulltext/semantic/hybrid/rag modes, inline and explicit author/tag/content-type filters, API-key `onlyOwnContent` scoping, content/attachment inclusion, indexing state, fallback warnings, search recording, and result shapes as `GET /api/search`. Its default remains `fulltext` for REST and backwards compatibility.
- **MCP structured/provenance results**: Every tool declares `outputSchema` and returns JSON in `structuredContent`, duplicating the serialized JSON in `content[].text` for older clients. Search hits add `evidenceAvailable` plus provenance-bearing `evidence[]` (article ID/slug, canonical API URL, source type, matched passage when one exists, updated timestamp, match type, score); title-only matches never fabricate passages. RAG sources include canonical URL and source type.
- **Content governance for MCP decisions**: Every dynamic `content_type` lookup has configurable `authorityWeight` (0-100, default 50); no content-type value is hard-coded into authority ranking. Approvals are nullable provenance (`ApprovedById`/`ApprovedAt`): approval applies only to an already-published article, while direct/imported publication is valid but reported as `not_recorded`; material changes clear prior approval. Per-article `ReviewIntervalDays` is configurable (1-3650). MCP search/detail derives review state, next review, authority level, reliability score and warnings, with aggregate `decisionSupport` caution counts on search responses.
- **Task-oriented MCP tools**: In addition to primitive search/get/list tools, MCP exposes `get_project_context`, `get_integration_guidance`, `find_authoritative_content`, `compare_sources`, and `get_recent_changes`. These are read-only orchestration layers over the same search/filter/evidence/governance services, not duplicated retrieval implementations. Source comparison explicitly reports `conflictAssessment: not_evaluated`; it never invents contradiction analysis.
- **MCP content security**: Article/search/compare output is scanned for explainable prompt-injection signals and carries `securityAssessment`; source data is always marked untrusted and `allowAutomaticExecution=false`. Common portal keys, bearer tokens, JWTs, AWS access-key IDs and assigned secrets are redacted from both `structuredContent` and compatibility text. RAG also redacts secrets, marks risky chunks, neutralizes source delimiters, and instructs the model never to follow source commands, visit source-requested URLs, invoke tools, change role, or disclose credentials. Detection flags content rather than deleting/blocking it and is a defense-in-depth layer, not a proof of safety.
- **MCP observability/audit**: Every `tools/call` returns `X-Trace-Id`, emits a structured Serilog audit event (trace, bounded client/protocol identity, tool/outcome, auth source, user/API-key IDs, duration, output bytes), and records Prometheus counters/histograms. Audit argument summaries contain field names plus types/string lengths/array counts only—never raw values, queries, content, credentials, or reversible hashes. Metric labels are bounded to tool/outcome/auth source to avoid cardinality and PII leaks.
- **MCP resilience**: POST bodies are capped at 256 KiB; tool results default to a 1 MiB serialized cap. `McpResilienceService` applies configurable tool/mode time budgets, fail-fast instance-local AI concurrency (default 2), and an instance-local Ollama circuit breaker (default 3 failures/30s). Stable structured errors are `tool_timeout`, `server_busy`, `circuit_open`, and `output_too_large`, with retry guidance. Request cancellation propagates through search/AI work and is audited separately as `cancelled`. Horizontal deployments still need gateway/shared-store distributed limits.
- **MCP quality gates**: CI runs and separately publishes release-critical xUnit traits for protocol conformance, schema contracts, deterministic golden retrieval, API-key isolation, prompt-injection/benign security corpora, and concurrent read-only calls. See `specs/mcp-quality-gates.md`. These InMemory/fake-AI gates do not cover PostgreSQL snowball/GIN ranking, pgvector HNSW recall/plans, or production Ollama latency.
- **View deduplication**: Same user viewing same article within 15 minutes counts as 1 view (hardcoded window)
- **Vote toggle**: POST `/api/articles/{id}/vote` with same `isHelpful` value → removes vote. Different value → changes vote. No existing vote → creates vote. One vote per user per article (unique constraint).
- **Vote reason**: `reason` field is only accepted when `isHelpful: false`. Free-text, optional.
- **Wilson Score**: Lower bound of Wilson score confidence interval (95%, z=1.96). Returned in article list and votes endpoint.
- **Comments independent of votes**: Users can leave comments without voting. Multiple comments per user allowed. Own comments can be deleted; admins can delete any.
- **View count in responses**: `GET /api/articles` list and `GET /api/articles/{idOrSlug}` detail both include `viewCount` field.
- **Article detail response**: `GET /api/articles/{idOrSlug}` includes `contentMarkdown` (canonical CommonMark/GFM for Milkdown), `contentText` (derived plain text for API consumers), and `attachments` array (id, fileName, contentType, sizeBytes, downloadUrl).
- **Tag upsert**: POST `/api/tags` returns 200 with existing tag if slug matches, 201 for newly created tag
- **Tag async listing**: GET `/api/tags` preserves its legacy array response without query parameters. Supplying `page`, `limit` (max 100), `q`, or repeatable `ids` returns `{ tags, total, page, totalPages }`; the article editor uses this for debounced server-side search and infinite-scroll loading.
- **Tag update**: PUT `/api/tags` renames tag and regenerates slug; returns 409 if new slug conflicts
- **Tag delete constraint**: DELETE `/api/tags?id=` returns 409 if tag has associated articles; only content-free tags can be deleted
- **Article GET supports slug**: `GET /api/articles/{idOrSlug}` accepts both article ID and slug for lookup
- **Publish/Archive enforcement**: Setting `status: "published"` requires `articles:publish` permission; `status: "archived"` requires `articles:archive`. Checked inline in ArticlesController PUT (not via attribute)
- **RBAC enforcement patterns**: Two patterns coexist: (1) `[RequirePermission("...")]` attribute for simple checks, (2) inline `RbacService.HasPermission(User, …)` / `CanEditArticle(User, …)` for ownership-based or conditional checks (edit/delete/publish/archive). Both are principal-aware and apply the API-key permission cap; the string-based `(role, permission)` overloads remain as the core matrix (no cap — use only when no principal is available)
- **Attachment upload**: Files remain at `data/uploads/{articleId}/{storedFileName}`. Uploads are written to a same-volume temporary file, flushed, SHA-256 hashed, then atomically renamed. Metadata records checksum and extraction status. Deletes move files/directories to `data/uploads/.trash` for recoverability. `/api/search/storage-status` samples checksums and reports missing files, extraction failures, bytes, and free disk.
- **Attachment deferred upload**: Frontend uses deferred upload pattern — files are queued locally and only uploaded when the article is saved. New files show "Kaydedilince yüklenecek" badge with green background.
- **Attachment deferred delete**: In edit mode, deleting a file marks it with strikethrough + "Kaydedilince silinecek" badge. Undo is available. Actual API DELETE happens on save.
- **Image deferred upload**: Images pasted/dropped into Milkdown are represented by temporary blob URLs in Markdown. On save, blob URLs are replaced with real `/api/attachments/{id}/download` URLs after upload.
- **Attachment cascade**: Article deletion removes attachment DB records by cascade and moves the article directory to recoverable local trash.
- **Attachment indexing**: Extracted text remains in PostgreSQL FTS and is embedded as separate attachment chunks carrying attachment id/name/location provenance. Extraction status/failure is persisted. Source chunks are round-robin interleaved so one long file cannot monopolize the total semantic cap. Adding/removing attachments durably queues re-indexing.
- **Attachment download**: Served via controller (auth required), not static file middleware. `PhysicalFile()` streams the file with correct Content-Type and Content-Disposition.
- **Lookup color/icon**: LookupValue entity has optional `Color` (Tailwind color key) and `Icon` (Lucide icon name) fields. Frontend renders content type badges with colored backgrounds and icons via `ContentTypeBadge` component. Color picker supports all 20 Tailwind color keys dynamically. Icon picker dynamically loads all lucide-react icons with search/filter. Utilities in `src/lib/lookup-utils.ts`, picker components in `src/components/lookup-pickers.tsx`.

## MCP Server Behaviors

- **MCP protocol version**: negotiated — supported: 2025-11-25 (default), 2025-06-18, 2025-03-26, 2024-11-05. `initialize` echoes the client's requested version when supported, otherwise answers with the default (JSON-RPC 2.0 spec-compliant)
- **Server info**: name=`knowledge-portal`, version=`2.0.0`
- **Supported methods**: `initialize`, `notifications/initialized`, `tools/list`, `tools/call`, `ping`
- **Server discovery**: POST `/mcp` with `method: "initialize"` returns server capabilities, protocol version, and implementation info
- **Streamable HTTP transport**: POST `/mcp` requires JSON, validates supported response media types and an optional `MCP-Protocol-Version`, rejects cross-origin browser requests and MCP batch payloads, and returns 202/no body for JSON-RPC notifications. GET `/mcp` always returns **405** with `Allow: POST` because the server is stateless and provides no SSE/server-initiated messages.
- **Notifications**: `notifications/initialized` returns **202 Accepted** with empty body (Streamable HTTP spec for response-less messages)
- **Tool discovery**: POST `/mcp` with `method: "tools/list"` returns all available tools with JSON Schema input definitions (queryable by clients)
- **Tool execution**: POST `/mcp` with `method: "tools/call"` + `params: {name, arguments}` executes tool and returns MCP content array
- **Tool result format**: `{ "content": [{"type": "text", "text": "..."}], "isError": false }` — results are JSON-serialized strings inside `text` field
- **Available tools**: `search_articles`, `get_article`, `list_articles`, `list_tags`, `get_portal_info` (all snake_case)
- **Error handling**: JSON-RPC 2.0 error format on protocol errors: `{error: {code, message}, jsonrpc: "2.0"}`. Tool errors use `isError: true` in content result. Unexpected tool exceptions are logged server-side with full detail; the client receives only a generic "Tool execution failed" (no internal detail leakage).
- **search_articles pagination**: accepts `page` (1-based) + `limit` (1-50); returns true post-filter `total`, `page`, `limit`, `totalPages` (same paged pipeline as `GET /api/search`).
- **get_portal_info counts**: `totalAuthors` = distinct owners of published articles; `totalTags` = tags used by ≥1 published article (consistent with the published-only scope of all tools). `list_articles` `sort` is validated against `newest|oldest|most_viewed` — invalid values return `isError`.
- **Authentication**: **NO OAUTH.** All `/mcp` requests require ONE of:
  - **API Key**: `X-API-Key: kp_*` header (BCrypt hashed, prefix-indexed lookup)
  - **JWT Bearer**: `Authorization: Bearer <token>` header (HMAC-SHA256, 24h expiry)
  - Both methods are equivalent; choose based on use case (API keys for long-lived integrations, JWT for user sessions)
- **Stateless execution**: Each request is independent; no session state is maintained between requests
- **Rate limiting**: `/mcp` (GET + POST) is covered by the `mcp` fixed-window rate limit policy (default 60/min, `RateLimiting:McpLimit`)
- **Tool access control**: Tools do not enforce RBAC beyond authentication. All authenticated users can access all tools. Tools only return published articles.

## Placeholder Fields (Not Yet Active)

These entity fields exist in the database but are not yet used in business logic:

| Field | Entity | Purpose | Status |
|-------|--------|---------|--------|
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
- Do NOT add OAuth to MCP — MCP uses only API Key or JWT Bearer token authentication (stateless, no session required)

### CONVENTIONS
- DB column names are snake_case; C# properties are PascalCase
- Viewers CAN create and publish articles; archiving remains restricted to editor/admin.
- Password minimum length is 8 characters everywhere
- All IDs are 21-character truncated GUIDs (hex, lowercase)
- Timestamps are ISO 8601 UTC in API responses
- `useApi` hook returns `{ fetchWithAuth }` — callers chain `.then(r => r.json())` for data
- Query-param based DELETE: Tags (`?id=`), Admin Users (`?id=`), API Keys (`?id=`)
- Admin Users PUT: `userId` is in the request body, not in the URL path
- Import API types from `src/types/api.ts` for type safety
- Rate limits are configurable via `appsettings.json` → `RateLimiting:AuthLimit` / `RateLimiting:SearchLimit`
