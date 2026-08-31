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
| Tests | xUnit + WebApplicationFactory (backend only). The default suite is **Docker-free** and runs on EF Core InMemory (isolated DB per test class) with deterministic fakes: `FakeEmbeddingGenerator`/`FakeChatClient` replace Ollama and `FakeVectorSearchService` replaces the pgvector search (`IVectorSearchService`). The app is provider-aware: on a non-relational provider it uses `EnsureCreated` (not migrations), FTS falls back to an in-memory accent-folded AND→OR substring search, and the embedding background service is removed in tests. `backend/Tests.Postgres` uses `RAG_FIDELITY_CONNECTION_STRING` against real PostgreSQL/pgvector to gate migrations, cosine ranking/filtering, Turkish stemming, and HNSW/GIN query plans. Local runs skip when that variable is absent, but CI treats the variable and fidelity gate as mandatory. CI also runs a mandatory live-Ollama golden-dataset evaluation through `backend/scripts/run-rag-live-quality-gate.ps1`. |
| MCP | Official `ModelContextProtocol.AspNetCore` v2 Streamable HTTP server at `/mcp` (**NO OAuth**, API Key or JWT auth only, stateless, modern discovery plus legacy initialize negotiation) |

## Accepted Operational Decisions (2026-08-20)

The following items are explicit owner decisions and are not open findings unless the owner asks to revisit them:

- **Tracked deployment secrets**: The real connection/JWT values in `backend/appsettings.json` are intentionally stored in the repository for this controlled deployment. Do not remove, rotate, relocate, redact, or report them as a defect by default.
- **Single backend instance**: The supported production topology has exactly one backend instance. Distributed bulkheads, shared circuit-breaker state, cross-instance AI concurrency controls, and horizontal-scaling work are out of scope; do not report their absence as a defect.
- **Embedding-time data governance**: Pre-embedding secret/PII redaction and related embedding-provider governance are intentionally deferred. Keep prompt-time/output redaction intact, but do not change or report the embedding-time behavior unless this decision is explicitly reopened.
- **Azure local-password reset**: An authenticated Azure-linked user may set or replace a local password without supplying the current local password. This is not limited to the first set.

## Conventions

### Backend (`backend/`)

- **Language**: C# 13, .NET 10, nullable enabled
- **Pattern**: Controllers → Services → EF Core DbContext → PostgreSQL. Shared domain logic lives in `Services/`; `ArticleMutationService` owns article write invariants, `ClassificationService` owns DB-driven category/default/value resolution, `ContentTypeService` validates the legacy compatibility field, `KnowledgeQueryScopeService` owns shared inline/explicit scope parsing, `SearchExecutionService` owns document retrieval, and `KnowledgeAnswerService` owns grounded RAG generation for Assistant/MCP. Search never generates an answer; Assistant never returns a search-result or analytics route. Controllers keep only routing, auth scoping, and response shaping. **No duplicated logic across controllers**; if two endpoints need the same behavior, extract it into a service (or a `Helpers/` static for pure functions). Service failures return `ServiceError` (mapped via `ToActionResult()`).
- **Naming**: PascalCase for C# code; `snake_case` for application-owned PostgreSQL tables, columns, keys, indexes and foreign keys (configured in `AppDbContext.OnModelCreating`). The EF migration-history table is named `__ef_migrations_history`; its two provider-owned columns retain EF's standard names.
- **Auth**: `[Authorize]` attribute on controllers, `[AllowAnonymous]` for public endpoints
- **RBAC**: `RequirePermission` attribute with permission constants from `Permissions` class
- **API prefix**: All routes under `/api/` (e.g. `/api/articles`, `/api/auth/login`)
- **Entities**: `backend/Models/Entities/` — User (with AzureObjectId and Slug), SystemSetting, Article, ArticleVersion, ArticleView, Tag, ArticleTag, ArticleVote, ArticleComment, ApiKey, SearchQuery, ArticleAttachment, LookupCategory, LookupValue, ArticleLookupValue, FeaturedLink, ArticleChunkParent, ArticleEmbedding, IndexJob, UsageEvent, RagEvaluationDataset/Run, AssistantInteraction, AssistantConversation/Message, AssistantAnswerCacheEntry
- **Controlled classifications**: `lookup_categories` defines generic DB-driven dimensions (single/multiple, optional/required with default, active state, and `none|filter` RAG behavior defaulting to `filter`); `lookup_values` stores controlled canonical options; `article_lookup_values` stores assignments. `content_type` and its initial values are seed data, remain fully administrator-configurable like other categories, and mirror their primary selection to `articles.content_type`/legacy `contentType` APIs while active.
- **Seed data**: `DbInitializer.SeedAsync()` — admin user + 11 default tags + content types + project documentation articles
- **Port**: 5174
- **Rate Limiting**: ASP.NET Core built-in rate limiter with distinct auth, search, Assistant, and MCP policies (defaults: auth=10/min, search=30/min, Assistant=20/min, MCP=60/min, configurable via `appsettings.json` → `RateLimiting`). **Partitioned per client**: partition key = `apiKeyId` claim > `id` (user) claim > client IP — one noisy caller can't exhaust everyone's budget; login brute-force throttled per source IP (requires ForwardedHeaders for real IPs behind the proxy)
- **Middleware pipeline**: ForwardedHeaders → HSTS (non-dev) → SecurityHeaders (nosniff/DENY/Referrer-Policy) → GlobalExceptionMiddleware → CORS → ApiKeyMiddleware → Authentication → UsageTrackingMiddleware → RateLimiter → Authorization → Controllers. RateLimiter runs after auth so partitioning sees the principal; ForwardedHeaders first so everything sees real client IP/scheme (`ForwardedHeaders:KnownProxies`/`KnownNetworks` config; TLS terminates at the company reverse proxy — no in-app HTTPS redirect)
- **AI/Search**: Ollama integration (optional, `Ollama:Enabled` in appsettings.json). Embedding model: bge-m3 (1024 dims; dimension guard before persistence). Chat/vision model: qwen2.5vl:7b. PostgreSQL-backed durable `index_jobs` queue coalesces edits by article with a generation counter; backend workers claim at most their available parallelism via `FOR UPDATE SKIP LOCKED`, use leases, a configurable per-article timeout (`Indexing:JobTimeoutSeconds`, default 600), exponential retry, and terminal failure tracking. Before FTS/embedding, `AttachmentProcessingService` creates one cached canonical extraction for attachments with `include_in_index=true`: DOCX/XLSX/PPTX/CSV tables become GFM Markdown, PDF/Office layout keeps page/sheet/slide provenance, and raster or embedded visuals receive bounded local-VLM description/OCR/table/diagram enrichment. A privacy-explicit optional Unstructured `hi_res` HTTP provider handles complex/scanned layouts; it is disabled by default and falls back to native parsing unless marked required. The extraction profile participates in semantic freshness, so parser/model/config changes durably re-index affected content. Article writes enqueue the durable job first, then best-effort refresh PostgreSQL FTS in the request path for immediate lexical visibility; the worker re-synchronizes FTS after canonical attachment processing before semantic indexing. Article body and each index-included attachment are indexed as separate provenance-bearing sources, fairly interleaved under per-source/total child caps. True hierarchical chunking stores structure-bounded parents (~1000 words) once and embeds compact overlapping children (~220/40 words); Markdown headings, GFM tables and attachment page/sheet/slide boundaries are preserved. Dense and lexical RAG candidates reuse the same child→parent identity, and only ACL-rechecked parent content enters generation. Parent/child targets, overlap, chunking version, attachment-inclusion policy and extraction profile are included in the embedding freshness hash. Hybrid search retrieves a configurable candidate pool, merges with RRF, then applies `ISearchReranker` (`LocalSearchReranker` by default). pgvector HNSW and PostgreSQL GIN/tsvector provide vector and lexical retrieval. RAG uses a model-calibrated Unicode token estimator with explicit model/output/prompt reserves, query-complexity plus marginal-score adaptive source selection, typed claim roles (`summary|explanation|step|constraint|exception|conflict`), dynamic `lookup_values.authority_weight` governance, deterministic numeric/polarity conflict screening, and separate cited versus consulted source lists.
- **Dynamic chat model selection**: Chat-capable models are discovered from `Ollama:BaseUrl` through `GET /api/tags` and, when available, verified through `POST /api/show`; the configured embedding model and embedding-only model names are excluded. The catalog is cached with last-known-good/configured fallback behavior. A session admin persists the default in `system_settings`. Interactive users select an available model on the Assistant screen; that choice is kept only in browser storage and sent with each Assistant request, never persisted in the database. A missing choice uses the admin default, then `Ollama:ChatModel`/first discovered model. MCP and system jobs use the admin default. This affects chat generation and cache identity, not the fixed embedding or attachment-vision profiles.
- **Error format**: All errors return `{ "error": "Human-readable message" }`
- **Success response shapes**: List endpoints return `{ articles[], total }` or `{ users[], total }`, mutations return `{ id, slug, title }` or `{ message }`, auth returns `{ token, user }`

### Frontend (`frontend/`)

- **Language**: TypeScript strict
- **State**: React Context (`AuthContext`, `ThemeContext`) — no Redux/Zustand
- **API calls**: `useApi` hook (`src/hooks/useApi.ts`) — auto-attaches JWT, auto-logout on 401
- **Routing**: React Router v7, `ProtectedRoute` + `RoleRoute` wrappers in `App.tsx`
- **Components**: `src/components/layout/` (AppShell, Sidebar), `src/components/editor/` (Milkdown Crepe)
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
├── Controllers/          # API endpoints
├── Auth/                 # JwtService, RbacService, ApiKeyMiddleware, ApiKeyGenerator, Permissions, ClaimsPrincipalExtensions, RequirePermissionAttribute, RequireSessionAuthAttribute
├── Data/                 # AppDbContext, DbInitializer, SlugQueries (unique slug generation)
├── Middleware/            # GlobalExceptionMiddleware
├── Helpers/              # ContentExtractor, AttachmentTextExtractor, SlugHelper, AttachmentHelper, ArticleQueryExtensions, RrfHelper
├── Mcp/                  # Official-SDK adapter (KnowledgePortalMcpServer), internal tool DTOs, tool executor
├── Services/             # Domain: ArticleService, TagService, ApiKeyService, UserService, StatsService, AnalyticsReportService, ServiceError
│                         # AI/Search: EmbeddingService, VectorSearchService (IVectorSearchService), RagService, SearchExecutionService, EmbeddingBackgroundService, IndexJobQueue, FullTextSearchService, SearchReranker
│                         # Assistant: AssistantRouterService, AssistantPolicyService, AssistantOrchestratorService (isolated, read-only, feature-flagged composition over existing services)
│                         # Observability: PortalMetrics (OpenTelemetry meters)
├── Models/
│   ├── Dtos.cs           # All request/response DTOs (C# records)
│   └── Entities/         # EF Core entity classes (includes durable IndexJob and RAG evaluation models)
├── Migrations/           # Squashed snake_case EF Core schema; legacy databases must be recreated
├── Program.cs            # App configuration & DI
└── appsettings.json      # Connection strings, JWT config, RateLimiting

backend/Tests/
├── Integration/          # WebApplicationFactory integration tests
└── Unit/                 # Unit tests (RbacService, JwtService, etc.)

frontend/
├── src/contexts/         # AuthContext (JWT auth state)
├── src/hooks/            # useApi (fetch wrapper), useArticleImages (deferred upload), useLookups (classification categories/values)
├── src/types/            # Shared TypeScript API types
├── src/components/       # layout/ + editor/ + attachments/
├── src/pages/            # Flat page components
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
   - Articles marked as "published" get `PublishedAt`; seed/import does not claim a human review, so `LastReviewedAt` remains null until approval

> **Seed documentation is part of the product documentation, not disposable sample data.** Whenever a feature, architecture, workflow, configuration, security control, or operational behavior changes, the relevant files in `backend/SeedData/articles/` must be updated in the same change. If the subject is substantial and has no suitable article, add one; for example, implementing or materially changing RAG requires a seed article that explains the actual RAG architecture, retrieval/generation flow, safeguards, configuration, observability, and operational behavior. Seed articles must remain mutually consistent and must not contradict `AGENTS.md`, `specs/`, or the implementation.

**To reset seed data**: Drop the PostgreSQL database, then restart backend (it recreates and reseeds automatically).

## Commands

| Task | Command |
|------|---------|
| Run backend | `cd backend && dotnet run` |
| Run frontend | `cd frontend && npm run dev` |
| Run backend tests | `cd backend/Tests && dotnet test` |
| Run PostgreSQL RAG fidelity tests | Set `RAG_FIDELITY_CONNECTION_STRING`, then `dotnet test backend/Tests.Postgres/KnowledgePortal.Api.PostgresTests.csproj --configuration Release` |
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
| `articles:delete_any` | ✓ | | |
| `articles:publish` | ✓ | ✓ | ✓ |
| `articles:archive` | ✓ | ✓ | |
| `articles:approve` | ✓ | ✓ | |
| `tags:manage` | ✓ | ✓ | |
| `users:manage` | ✓ | | |
| `analytics:view` | ✓ | ✓ | |
| `api_keys:manage` | ✓ | ✓ | ✓ |
| `api_keys:manage_any` | ✓ | | |
| `featured_links:manage` | ✓ | | |

**API key effective permissions**: a `source=api-key` principal carries at most **editor** authority — an admin-owned key acts as editor (no `users:manage`, `articles:edit_any/delete_any`, `api_keys:manage_any`); editor/viewer-owned keys keep their owner's role. On top of the cap, `articles:delete_any` is always denied and destructive DELETE endpoints are `[RequireSessionAuth]` (see matrix below). All view/read operations follow the effective role. Exception: `DELETE /api/articles/{id}/vote` (removing one's own vote) stays available to keys — it is an interaction toggle, not content deletion.

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
| `/api/llm-models` | GET | ✓ | — | ✗ |
| `/api/admin/llm-settings` | GET/PUT | ✓ | `users:manage` | ✓ |
| `/api/articles` | GET | ✓ | — | ✗ |
| `/api/articles` | POST | ✓ | `articles:create` | ✗ |
| `/api/articles/{idOrSlug}` | GET | ✓ | — | ✗ |
| `/api/articles/{id}` | PUT | ✓ | `articles:edit_own` / `articles:edit_any` + `articles:publish` (for status→published) + `articles:archive` (for status→archived) | ✗ |
| `/api/articles/{id}` | DELETE | ✓ | `articles:delete_any` (admin only) | ✓ |
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
| `/api/capabilities` | GET | ✓ | — | ✗ |
| `/api/assistant` | POST | ✓ | — | ✗ |
| `/api/assistant/stream` | POST | ✓ | — | ✗ |
| `/api/assistant/feedback` | POST | ✓ | — | ✗ |
| `/api/assistant/source-click` | POST | ✓ | owner | ✗ |
| `/api/assistant/conversations` | GET/POST/DELETE | ✓ | — | ✓ |
| `/api/assistant/conversations/{id}` | DELETE | ✓ | owner | ✓ |
| `/api/assistant/conversations/{id}/messages` | GET | ✓ | owner | ✓ |
| `/api/search/authors` | GET | ✓ | — | ✗ |
| `/api/search/click` | POST | ✓ | — | ✗ |
| `/api/search/reindex` | POST | ✓ | `users:manage` | ✓ |
| `/api/search/repair-indexing` | POST | ✓ | `users:manage` | ✓ |
| `/api/search/diagnostics` | GET | ✓ | `users:manage` | ✓ |
| `/api/search/embedding-status` | GET | ✓ | `users:manage` | ✓ |
| `/api/search/storage-status` | GET | ✓ | `users:manage` | ✓ |
| `/api/admin/rag/observability` | GET | ✓ | `users:manage` | ✓ |
| `/api/admin/rag/debug` | GET | ✓ | `users:manage` | ✓ |
| `/api/analytics` | GET | ✓ | `analytics:view` | ✓ |
| `/api/dashboard` | GET | ✓ | — | ✗ |
| `/api/admin/users` | GET | ✓ | `users:manage` | ✓ |
| `/api/admin/users` | POST | ✓ | `users:manage` | ✓ |
| `/api/admin/users` | PUT | ✓ | `users:manage` | ✓ |
| `/api/admin/users?id={id}` | DELETE | ✓ | `users:manage` | ✓ |
| `/api/admin/rag-evaluations/datasets` | GET/POST | ✓ | `users:manage` | ✓ |
| `/api/admin/rag-evaluations/datasets/{id}` | GET/PUT/DELETE | ✓ | `users:manage` | ✓ |
| `/api/admin/rag-evaluations/datasets/{id}/runs` | POST | ✓ | `users:manage` | ✓ |
| `/api/admin/rag-evaluations/runs` | GET | ✓ | `users:manage` | ✓ |
| `/api/admin/rag-evaluations/runs/{runId}` | GET | ✓ | `users:manage` | ✓ |
| `/api/admin/rag-evaluations/feedback-summary` | GET | ✓ | `users:manage` | ✓ |
| `/api/keys` | GET | ✓ | `api_keys:manage` | ✓ |
| `/api/keys` | POST | ✓ | `api_keys:manage` | ✓ |
| `/api/keys` | PUT | ✓ | `api_keys:manage` | ✓ |
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
| `/api/lookups/categories` | GET | ✓ | — | ✗ |
| `/api/lookups/categories` | POST/PUT | ✓ | `tags:manage` | ✗ |
| `/api/lookups/categories?id={id}` | DELETE | ✓ | `tags:manage` | ✓ |
| `/api/logs` | GET | ✓ | `users:manage` | ✓ |
| `/api/logs/{fileName}` | GET | ✓ | `users:manage` | ✓ |
| `/api/logs/{fileName}` | DELETE | ✓ | `users:manage` | ✓ |
| `/api/bulk/templates/{format}` | GET | ✓ | — | ✗ |
| `/api/bulk/import-schema` | GET | ✓ | — | ✗ |
| `/api/bulk/import` | POST | ✓ | `articles:create` | ✗ |
| `/api/bulk/export` | GET | ✓ | — | ✗ |
| `/api/source-imports/analyze` | POST | ✓ | `articles:create` | ✗ |
| `/api/source-imports/commit` | POST | ✓ | `articles:create` | ✗ |
| `/mcp` | GET | ✓ | — | ✗ (always 405; POST-only transport) |
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
| `article.classifications` | — | — | Optional object of category keys to value arrays; active values only, category cardinality/required/default rules enforced |
| `tag.name` | 1 | 50 | Required, unique slug generated |
| `search.q` | 1 | — | Required |
| `search.limit` | 1 | 50 | Default 20 |
| `search.page` | 1 | — | Default 1. Applies to fulltext + tag-browse; semantic/hybrid are top-N only |
| `search.onlyOwnContent` | — | — | Optional, boolean. When true + API key auth → filters to articles created by that API key |
| `search.includeContent` | — | — | Optional, boolean. When true → includes canonical CommonMark/GFM as the string field `contentMarkdown` |
| `search.includeAttachments` | — | — | Optional, boolean. When true → includes attachment metadata (id, fileName, contentType, sizeBytes, downloadUrl) per article in search results |
| `search.tag` | — | — | Optional, repeatable, tag slugs (merged with #syntax) |
| `search.author` | — | — | Optional, repeatable, user slugs (merged with @syntax) |
| `search.contentType` | — | — | Optional, repeatable legacy content type values; generic inline filtering uses `+category:value` |
| `search.facet` | — | — | Optional, repeatable `category:value`; values within a category use OR and categories use AND; inline form is `+category:value` |
| `assistant.message` / MCP `ask_knowledge.question` | 1 | 4000 | Shared transport-independent limit; configurable via `Assistant:MaxMessageCharacters` |
| `assistant.model` | — | — | Optional Ollama-discovered chat model; validated server-side. The browser stores the Assistant UI selection locally; the database does not persist it. |
| MCP knowledge scope collections | — | 50 values/category | Shared REST/MCP guard; configurable via `KnowledgeInput:MaxScopeItems`, with each value bounded by `MaxScopeValueCharacters` (default 200) |
| `assistant.conversationId` | — | 21 | Optional; session-only and ownership checked; bounded recent user/assistant turns are rewritten into a standalone follow-up query, with fail-open deterministic fallback and optional retrieval-only HyDE |
| `assistant.onlyOwnContent` | — | — | Optional, boolean. When true + API key auth → limits RAG evidence to articles created by that API key |
| `assistant.tags/authors/contentTypes` | — | — | Optional evidence-scope arrays; share the same parser and ACL-safe filter semantics as Search |
| `assistant.facets` | — | — | Optional classification object `{ category: [values] }`; unknown/inactive categories or values fail closed before RAG context construction |
| `assistant.feedback.reason` | — | — | Optional: `incorrect`, `incomplete`, `wrong_source`, `outdated`, `no_answer`, or `other` |
| `articles.limit` | 1 | 100 | Default 20 |
| `articles.onlyOwnContent` | — | — | Optional, boolean. When true + API key auth → filters to articles created by that API key |
| `articles.includeContent` | — | — | Optional, boolean. When true → includes canonical CommonMark/GFM as the string field `contentMarkdown` |
| `articles.includeAttachments` | — | — | Optional, boolean. When true → includes attachment metadata per article in list results |
| `profile.name` | 1 | — | Required for profile update |
| `profile.newPassword` | 8 | 128 | Optional; local users require currentPassword, Azure-linked users do not |
| `attachment.file` | 1 byte | 20MB | Required, extension whitelist enforced |
| `attachment.extensions` | — | — | Allowed: .png, .jpg, .jpeg, .gif, .webp, .pdf, .md, .txt, .docx, .xlsx, .pptx, .yaml, .json, .csv, .svg |
| `attachment.includeInIndex` | — | — | Optional boolean, default true; false keeps the file downloadable but excludes it from FTS, semantic search and RAG sources |
| `attachment.maxPerArticle` | — | 20 | Configurable via appsettings.json |
| `attachment.maxExtractedCharacters` | 1000 | 5000000 | Default 50000; persisted per extraction so limit changes trigger re-extraction |
| `attachment.extractionProfile` | — | — | Native/external parser, strategy and vision-model profile; changes trigger durable re-extraction/re-indexing |

## Feature Status

| Feature | Status | Notes |
|---------|--------|-------|
| JWT Auth | ✅ Implemented | 24h expiry, HMAC-SHA256 |
| Azure AD Auth | ✅ Implemented | MSAL v5 redirect-bridge popup, auto-creates user from Azure profile |
| API Key Auth | ✅ Implemented | kp_ prefix, BCrypt hash, prefix-indexed lookup |
| Articles CRUD | ✅ Implemented | Full lifecycle with versioning |
| Tags | ✅ Implemented | CRUD + article tagging |
| Search (fulltext) | ✅ Implemented | PostgreSQL tsvector/tsquery (`turkish` config: stemming + stopwords) with GIN index and weighted ranking, content body indexed, Turkish accent folding via C# transliteration. Article writes eagerly refresh FTS for immediate lexical visibility while the durable job revalidates it. Multi-word queries are AND-first (all terms must match), retrying with OR then ILIKE when empty. Paged (`page` param) with true post-filter `total`/`totalPages` and match-context `snippet` per result |
| Search (tag-based) | ✅ Implemented | `#tag` prefix syntax, multiple tags with AND logic |
| Search (semantic) | ✅ Implemented | Ollama embedding (bge-m3, 1024 dims) + heading/layout-bounded parent (~1000 words) and searchable child (~220 words, 40 overlap) hierarchy + pgvector cosine distance, best-child scoring |
| Search (hybrid) | ✅ Implemented | Reciprocal Rank Fusion (α=0.4 fulltext + β=0.6 semantic, k=60, `Helpers/RrfHelper`) |
| Knowledge Assistant (RAG) | ✅ Implemented | Dedicated AI/RAG surface at `/api/assistant` and MCP `ask_knowledge`: multi-turn standalone-query contextualization with retrieval-only HyDE, deterministic query understanding, hybrid/RRF evidence retrieval, production-bounded optional cross-encoder reranking, dynamic authority/freshness ranking, adaptive source breadth, calibrated token budgeting, per-response aggregated input/output token usage display (provider counts with explicit estimation fallback; cache hits report zero generation tokens), ACL-safe child→parent context, typed grounded synthesis, cited/consulted separation, conflict screening, fail-closed map-reduce, verified SSE, one active owned session conversation per user (a new session permanently replaces the previous conversation), feedback, and principal/corpus/runtime-scoped semantic answer cache. It exposes no document-search, analytics, general-chat, write, or free-form SQL route. |
| RAG Quality Evaluation | ✅ Implemented | Admin-only dynamic golden datasets and thresholds, lease-recoverable durable background runs, immutable dataset/config/model/prompt/retrieval snapshots, Recall/MRR/NDCG/fact/citation/grounding/refusal/safety/latency metrics, production feedback summary at `/settings/rag-evaluations`, and a mandatory CI live-model gate |
| Search Click Tracking | ✅ Implemented | POST /api/search/click records which result was clicked |
| Analytics | ✅ Implemented | Session-only endpoint; persisted authenticated usage events with calendar-day trend plus per-user, per-API-key integration, REST/MCP, read/write, error, latency, and top-operation breakdowns |
| Admin Users | ✅ Implemented | Session-only, self-protection |
| API Key Management | ✅ Implemented | Self-service create/list/rotate/delete (all roles, ProfilePage); admin CRUD over all users' keys (`/api/admin/keys`, `/admin/keys`) |
| Article Feedback | ✅ Implemented | Vote (1 per user/article, toggle) + Comments (independent, multiple). Wilson Score. View count in responses. |
| Related Articles | ✅ Implemented | Tag-overlap based, GET /api/articles/{id}/related |
| Article Versions | ✅ Implemented | Created on content change |
| View Tracking | ✅ Implemented | Deduplicated per user/article/15min window |
| Rate Limiting | ✅ Implemented | Distinct login/register, Search, Assistant, and MCP policies — partitioned per API key/user/IP |
| Health Check | ✅ Implemented | GET /api/health (readiness: 503 "unhealthy" when DB unreachable, 200 "degraded" when only Ollama down, else "healthy"; Ollama probe is separately timeout-bounded and cached) + GET /api/health/live (liveness, always 200) |
| Metrics | ✅ Implemented | OpenTelemetry → Prometheus at /metrics (not proxied by nginx — internal only): ASP.NET Core instrumentation + `kp_pending_embeddings` gauge + `kp_embedding_failures` counter |
| RAG Observability | ✅ Implemented | `kp_rag_*` request/stage/latency/candidate/context/LLM/refusal/partial/failure/citation/active metrics, optional OTLP export for `KnowledgePortal.Rag` traces (`OpenTelemetry:OtlpEndpoint` or `OTEL_EXPORTER_OTLP_ENDPOINT`), Prometheus alerts under `ops/prometheus/rag-alerts.yml`, Grafana dashboard under `ops/grafana/rag-overview.json`, measurable objectives/error-budget policy in `specs/rag-slo.md`, privacy-safe query fingerprints, and admin runtime snapshot at `/api/admin/rag/observability` |
| OpenAPI/Swagger | ✅ Implemented | Available at /swagger in development |
| Read Time Calculation | ✅ Implemented | Auto-calculated from content (~200 wpm) |
| 404 Page | ✅ Implemented | NotFoundPage for unmatched routes |
| Version Diff | ✅ Implemented | Line-based diff comparison between versions |
| Dark Mode Toggle | ✅ Implemented | Light/Dark/System toggle, persisted to localStorage |

| User Profile Page | ✅ Implemented | Name/email update + password change via PUT /api/auth/profile |
| Pagination UI | ✅ Implemented | Articles list + Admin Users have prev/next controls |
| Article Attachments | ✅ Implemented | File upload/download/delete, per-attachment index-inclusion flag, Milkdown image insert, max 20MB, extension whitelist |
| System Logs | ✅ Implemented | Serilog: console + rolling daily JSON file (CompactJsonFormatter, same log_YYYYMMDD.log naming), retention `Logging:RetainedFileCountLimit` (default 30), min level via `Serilog:MinimumLevel` config. View/delete via admin UI, today's log protected |

## Known Frontend Gaps

No known gaps at this time.

## Key Behaviors

- **Search / Assistant boundary**: `GET /api/search` and MCP `search_articles` return documents only and accept `fulltext|semantic|hybrid`; they never invoke chat generation. `POST /api/assistant`, verified-response SSE `/stream`, and MCP `ask_knowledge` use `KnowledgeAnswerService` and return grounded RAG answers/evidence only; there is no mode/router, search-result fallback, analytics route, general chat, write tool, or Text-to-SQL. `KnowledgeQueryScopeService` is the only shared interpretation layer for inline/explicit filters; retrieval and answer execution remain separate. Search analytics live only in `search_queries`; Assistant audit/feedback/RAG reproducibility and source clicks live only in `assistant_interactions`. Owned session-only conversations retain bounded messages, and semantic answers cache only fully grounded responses isolated by user/role/auth/API-key plus corpus/governance/runtime fingerprints.
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
- **Azure AD password set/reset**: An authenticated Azure-linked user can set or replace the local password via PUT `/api/auth/profile` without providing `currentPassword`. After setting, both Azure and email+password login work. This is an explicit owner-approved behavior, not a first-time-only exemption.
- **`/api/auth/me` response**: Includes `isAzureUser` boolean field (true if user has AzureObjectId linked).
- **API key source**: Claims include `source: "api-key"` — session-only endpoints check this
- **API key permission cap**: RBAC checks are principal-aware (`RbacService.HasPermission(ClaimsPrincipal, …)`, `CanEditArticle`/`CanViewArticle`). For `source=api-key`: effective role = owner role capped at editor (admin→editor), and `articles:delete_any` is always denied. Article deletion is session-admin-only; other destructive DELETE endpoints additionally carry `[RequireSessionAuth]`. Vote removal (own vote) remains key-accessible.
- **Article list tags**: GET /api/articles response includes `tags` array per article
- **Tag input flexibility**: `Tags` array in create/update accepts tag ID, tag name, or tag slug — resolved in that priority order. Any caller authorized to create or edit the article, including viewer sessions, may supply a new tag name in that article mutation; unknown tags are created and linked atomically with the article save. This contextual capability does not grant access to standalone tag management (`POST`/`PUT`/`DELETE /api/tags`), which still requires `tags:manage`. Creation is deferred to the article save, so abandoning the form cannot leave orphan tags.
- **Search wildcard escaping**: `%` and `_` characters are escaped in LIKE queries
- **Search query semantics**: Multi-word queries are joined with AND (all terms must match, precision-first). When AND yields nothing, the query retries with OR (any term), then falls back to ILIKE on title/excerpt. tsquery meta-characters are stripped from tokens.
- **Search pagination**: `GET /api/search` accepts `page` (default 1). Fulltext and tag-browse responses return the true post-filter `total` plus `page`/`totalPages` (filters are applied to the full ranked candidate set — capped at 1000 FTS candidates — before paging, so filtered searches don't under-return). FTS ordering is deterministic (`rank DESC, Id` tiebreaker) so pages never overlap on rank ties. Semantic/hybrid remain top-N (`page`/`totalPages` fixed at 1, `total` = returned count).
- **Search snippet**: Search results include a `snippet` field — a ~240-char match-context window from the article body around the earliest query-term occurrence (accent/case-folded matching mirroring the FTS index, stem-prefix tolerant). `null` when no term occurs in the body (e.g. title-only match) — clients fall back to `excerpt`. Frontend highlights query terms in the snippet.
- **Plain-text extraction scope**: `ContentExtractor` strips Markdown syntax while retaining readable headings, table cells, code, image alt text, and link labels. Link/image URLs and formatting syntax are excluded from read-time calculation, search indexes, embeddings, and the detail response's derived `contentText`. `includeContent` intentionally returns the canonical Markdown string in `contentMarkdown`.
- **Search inline syntax**: `@user-slug` filters authors (OR), `#tag-slug` filters tags (AND), and `+category:value` filters any active lookup category dynamically. Values within a category use OR; categories combine with AND. Example: `@ahmet #react +department:finance +content_type:guide nasıl yapılır`. Inline syntax and query parameters are merged; `##` is not a filter syntax.
- **Search filters**: `GET /api/search` accepts optional query parameters: `onlyOwnContent` (boolean, API key auth only — filters to articles created by that API key), `includeContent` (boolean — includes canonical Markdown in `contentMarkdown`), `includeAttachments` (boolean — includes attachment metadata array per article), `tag` (repeatable, tag slugs), `author` (repeatable, user slugs), legacy `contentType` (repeatable content-type values), and generic `facet` (repeatable `category:value`). Filters apply to `fulltext`, `semantic`, and `hybrid`. Tags from `#syntax` and `tag`, authors from `@syntax` and `author`, and classifications from `+category:value` and `facet` are merged. If only filters are specified without text, Search returns browse results. Assistant accepts equivalent scope arrays but requires a question.
- **Search click tracking**: Search responses include `searchQueryId` — clients POST `/api/search/click` with article clicked
- **Search semantic**: pgvector cosine distance operator (`<=>`) on searchable child rows in the `vector(1024)` `article_embeddings` column, accelerated by HNSW (`ix_article_embeddings_embedding_hnsw`). Filter metadata is denormalized onto the child rows so the vector scan remains single-table; article visibility is rechecked by callers. List search collapses to the best child per article. MinSimilarityScore=0.5 (configurable).
- **Search hybrid**: Reciprocal Rank Fusion (α=0.4 fulltext + β=0.6 semantic, k=60). Both legs over-fetch (limit×3, cap 50) so post-merge filters don't starve the final `Take(limit)`. Each result has `matchType` (fulltext/semantic/both). Falls back to fulltext-only if Ollama unavailable.
- **Assistant RAG**: For an owned conversation, `AssistantQueryContextualizer` uses bounded recent user/assistant turns to rewrite an anaphoric follow-up into a standalone query; malformed output or timeout falls back to a deterministic topic rewrite. Its optional hypothetical document is used only for a second dense lookup and can never become FTS input, evidence, or generation context. `RagQueryUnderstandingService` then extracts explicit metadata filters, expands configured synonyms/acronyms, and decomposes only compound questions up to `QueryUnderstanding:MaxQueries`; every subquery reuses the same effective ACL filter. `HybridRagRetriever` fuses lexical/vector child ranks, then adds freshness, approval and generic lookup authority signals. After published/filter recheck, strong child hits resolve through their persisted FK to one deduplicated structure-bounded parent; parent lookup is constrained to the authorized article set. Legacy parentless rows use bounded same-section neighbors only during rolling re-index. `IRagContextBuilder` performs exact-duplicate suppression, source diversity, calibrated token budgeting, prompt hardening, and evidence-ID preservation.
- **RAG reranking**: Local deterministic reranking is always available. `Reranking:External:Enabled` optionally sends the locally ranked, bounded candidate passages to an HTTPS/loopback cross-encoder endpoint. Request format/API-key header profiles, query/candidate/text/response limits, total timeout, bounded retry, score-coverage validation, arbitrary-logit normalization, process-local bulkhead/circuit breaker, disabled redirects, metrics, and fail-open local fallback apply. Enabling it is an explicit data-governance decision because candidate text leaves the process.
- **RAG debug**: Session-admin `GET /api/admin/rag/debug?q=...` runs the real query-understanding/retrieval/reranking/ACL/expansion/context pipeline without an LLM call and returns only post-authorization candidates plus exact selected context.
- **Assistant feedback**: The Assistant UI records helpful/not-helpful plus optional bounded reason codes against the caller's owned `interactionId`. `assistant_interactions` stores trace, prompt/retrieval/index/reranker/grounding versions, tool/timing metadata, source click, and only SHA-256 query/answer fingerprints. No Assistant activity is written to `search_queries`. The RAG evaluation page aggregates this signal by reason, grounding, and configuration.
- **Search indexing**: Mutations set lexical `FtsIndexedAt=null` and semantic `IndexedAt=null`, then upsert one durable `index_jobs` row per article before attempting eager FTS. Eager FTS is best-effort, savepoint-isolated inside bulk/import transactions, and makes normal creates/updates/attachment changes immediately visible to lexical search. Semantic embedding always remains asynchronous. A generation increment prevents an older in-flight worker from acknowledging a newer edit; workers still recompute FTS before embedding, so eager success never bypasses durable retry or concurrent-edit recovery. Workers claim with `FOR UPDATE SKIP LOCKED`, renew leases during long processing, use configurable parallelism, recover expired leases, retry with persisted exponential backoff, and expose pending/failed state through diagnostics. Full reindex populates the same durable queue instead of starting an untracked task.
- **Search indexing concurrency**: `IndexedAt` is claimed with an optimistic conditional update (`UPDATE ... WHERE xmin = <captured>`). The queue generation guard independently prevents stale completion; together they cover edits both during embedding and around job acknowledgement.
- **Search responses**: Search responses include `indexingPending` plus mode-aware, filter-scoped `indexCoverage`. Fulltext considers only `FtsIndexedAt`; semantic considers only `IndexedAt`; hybrid considers both without double-counting articles. Filter-only browsing needs neither index and returns no coverage warning. Semantic/hybrid include `warning` when Ollama is unavailable.
- **MCP capability split**: `search_articles` mirrors `GET /api/search` with `fulltext|semantic|hybrid`, filters, API-key `onlyOwnContent`, content/attachment inclusion, indexing state, warnings, search recording, and document result shapes. `ask_knowledge` mirrors the grounded Assistant execution boundary and returns answer, claims, cited/consulted sources, evidence, grounding metadata, and trace information without creating a search record. The two MCP AI lanes have separate concurrency slots and circuit state.
- **MCP scope model**: Search/list/task tools that retrieve multiple articles accept one optional `scope` object: `{ tags: string[], contentTypes: string[] }`. Tags are free semantic tag slugs with no team/project/module prefix convention and combine with AND; content types are active lookup values and combine with OR; both dimensions combine with AND. Missing scope means general retrieval. Unknown values must match nothing, never silently widen the search. Legacy flat `tags`, `content_type`, and `project_tag` inputs are normalized into this same model for backwards compatibility. `get_project_context` requires a non-empty effective scope; the other scoped tools keep scope optional.
- **MCP structured/provenance results**: Every tool declares `outputSchema` and returns JSON in `structuredContent`, duplicating the serialized JSON in `content[].text` for older clients. Search hits add `evidenceAvailable` plus provenance-bearing `evidence[]` (article ID/slug, canonical API URL, source type, matched passage when one exists, updated timestamp, match type, score); title-only matches never fabricate passages. RAG sources include canonical URL and source type.
- **Content governance for MCP decisions**: Every dynamic lookup value has configurable `authorityWeight` (0-100, default 50); no lookup category or value is hard-coded into authority ranking. An article's authority is the highest weight among its active assignments in active categories; legacy rows without generic assignments fall back to their `content_type` lookup weight. Approvals are nullable provenance (`ApprovedById`/`ApprovedAt`): approval applies only to an already-published article, while direct/imported publication is valid but reported as `not_recorded`; material changes clear prior approval. Per-article `ReviewIntervalDays` is configurable (1-3650). MCP search/detail derives review state, next review, authority level, reliability score and warnings, with aggregate `decisionSupport` caution counts on search responses.
- **Task-oriented MCP tools**: In addition to primitive search/get/list tools, MCP exposes `get_project_context`, `get_integration_guidance`, `find_authoritative_content`, `compare_sources`, and `get_recent_changes`. These are read-only orchestration layers over the same search/filter/evidence/governance services and shared `scope` normalization, not duplicated retrieval implementations. The legacy tool name `get_project_context` does not impose project semantics on tags. Source comparison explicitly reports `conflictAssessment: not_evaluated`; it never invents contradiction analysis.
- **MCP content security**: Article/search/compare output is scanned for explainable prompt-injection signals and carries `securityAssessment`; source data is always marked untrusted and `allowAutomaticExecution=false`. Common portal keys, bearer tokens, JWTs, AWS access-key IDs and assigned secrets are redacted from both `structuredContent` and compatibility text. RAG also redacts secrets, marks risky chunks, neutralizes source delimiters, and instructs the model never to follow source commands, visit source-requested URLs, invoke tools, change role, or disclose credentials. Detection flags content rather than deleting/blocking it and is a defense-in-depth layer, not a proof of safety.
- **MCP observability/audit**: Every `tools/call` returns `X-Trace-Id`, emits a structured Serilog audit event (trace, bounded client/protocol identity, tool/outcome, auth source, user/API-key IDs, duration, output bytes), and records Prometheus counters/histograms. Audit argument summaries contain field names plus types/string lengths/array counts only—never raw values, queries, content, credentials, or reversible hashes. Metric labels are bounded to tool/outcome/auth source to avoid cardinality and PII leaks.
- **MCP resilience**: POST bodies are capped at 256 KiB; tool results default to a 1 MiB serialized cap. `McpResilienceService` applies configurable tool/mode time budgets and separate fail-fast instance-local semantic-search versus grounded-answer bulkhead/circuit lanes (defaults 2 slots each, 3 failures/30s). Only classified transient provider failures affect circuit state; validation, not-found, capacity and caller errors never open it. Every tool failure uses the advertised structured `{ error: { code, message, retryable, retryAfterSeconds, details } }` contract and mirrors it into compatibility text. Request cancellation propagates through search/AI work and is audited separately as `cancelled`.
- **MCP quality gates**: CI runs and separately publishes release-critical xUnit traits for protocol conformance, schema/result contracts, deterministic golden retrieval, API-key isolation, prompt-injection/benign security corpora, and concurrent read-only calls. Post-deploy gates separately exercise REST Assistant routing, the RAG golden dataset and a modern `/mcp` `ask_knowledge` request through the live Ollama runtime. See `specs/mcp-quality-gates.md`. InMemory/fake-AI gates alone do not cover PostgreSQL snowball/GIN ranking, pgvector HNSW recall/plans, or production Ollama latency.
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
- **Publish/Archive enforcement**: All roles carry `articles:publish`; `status: "archived"` requires `articles:archive`. `ArticleMutationService` applies the same rule to REST, bulk import, and source import.
- **Classification invariant**: Article writes accept only active category/value definitions, require canonical lookup values, enforce single/multiple cardinality and required/default rules, and synchronize assignments through `ClassificationService`/`ArticleMutationService`. Category defaults are DB-driven and applied on create. The seeded `content_type` category is not protected; while active, legacy `contentType` is validated against it and mirrored into generic assignments, with the first selected value mirrored back to the compatibility column. REST, bulk import/export and source import share the same mutation rules.
- **RBAC enforcement patterns**: `[RequirePermission("...")]` handles simple endpoint checks; `ArticleMutationService` and `RbacService.CanEditArticle(User, …)` handle ownership-based or conditional article writes. Article deletion requires both session authentication and `articles:delete_any`, so it is admin-only.
- **Attachment upload**: Files remain at `data/uploads/{articleId}/{storedFileName}`. Uploads are written to a same-volume temporary file, flushed, SHA-256 hashed, then atomically renamed. Metadata records checksum, extraction status, extracted character count, the configured extraction limit, and whether text was truncated. Deletes move files/directories to `data/uploads/.trash` for recoverability. `/api/search/storage-status` samples checksums and reports missing files, extraction failures/truncations, bytes, and free disk.
- **Attachment deferred upload**: Frontend uses deferred upload pattern — files are queued locally and only uploaded when the article is saved. New files show "Kaydedilince yüklenecek" badge with green background.
- **Attachment deferred delete**: In edit mode, deleting a file marks it with strikethrough + "Kaydedilince silinecek" badge. Undo is available. Actual API DELETE happens on save.
- **Image deferred upload**: Images pasted/dropped into Milkdown are represented by temporary blob URLs in Markdown. On save, blob URLs are replaced with real `/api/attachments/{id}/download` URLs after upload.
- **Attachment cascade**: Article deletion removes attachment DB records by cascade and moves the article directory to recoverable local trash.
- **Attachment indexing**: Each attachment persists `include_in_index` (API `includeInIndex`, default true). False keeps the file downloadable but excludes it from extraction work, FTS, semantic search, and RAG sources. Source-import review exposes per-draft original-file and per-additional-attachment inclusion checkboxes; parsed originals default false, unparsed originals and supporting attachments default true, and users may override every choice before commit. `AttachmentProcessingService` runs before both FTS and embedding and persists one canonical extraction for included attachments. Native OpenXML parsing preserves DOCX/XLSX/PPTX/CSV tables as GFM Markdown (including sparse XLSX coordinates and formula/value pairs); PDF/Office segments keep page/sheet/slide provenance. Bounded qwen2.5vl enrichment converts standalone and supported PDF/DOCX/XLSX/PPTX embedded raster visuals into factual descriptions, literal OCR, Markdown tables and graph/diagram relationships. Optional `DocumentParsing:External` routes complex files to an explicitly configured Unstructured `hi_res` endpoint; external transfer is disabled by default. Status/failure/truncation, limit, segment kind/location and extraction profile are cached; the semantic index profile also includes the attachment-inclusion policy version, so changes invalidate both lexical and semantic indexes through the durable queue. Source children are round-robin interleaved so one long file cannot monopolize the semantic cap.
- **Attachment download**: Served via controller (auth required), not static file middleware. `PhysicalFile()` streams the file with correct Content-Type and Content-Disposition.
- **Lookup color/icon**: LookupValue entity has optional `Color` (Tailwind color key) and `Icon` (Lucide icon name) fields. Frontend renders content type badges with colored backgrounds and icons via `ContentTypeBadge` component. Color picker supports all 20 Tailwind color keys dynamically. Icon picker dynamically loads all lucide-react icons with search/filter. Utilities in `src/lib/lookup-utils.ts`, picker components in `src/components/lookup-pickers.tsx`.

## MCP Server Behaviors

- **MCP implementation/protocol**: wire transport, JSON-RPC validation, capability discovery, version negotiation and response framing are owned by the official `ModelContextProtocol.AspNetCore` 2.2 SDK. Preferred revision is `2026-07-28`; the SDK also negotiates its supported initialize-capable legacy revisions. Modern requests use `server/discover`, per-request `_meta`, `MCP-Protocol-Version`, `Mcp-Method`, and (for `tools/call`) `Mcp-Name`.
- **Server info**: name=`knowledge-portal`, version=`3.0.0`
- **Supported methods**: modern `server/discover`; legacy `initialize`/`notifications/initialized`; common `tools/list`, `tools/call`, and `ping`
- **Server discovery**: modern POST `/mcp` with `method: "server/discover"` returns the modern supported version and capabilities; legacy clients use `initialize`.
- **Streamable HTTP transport**: POST `/mcp` requires JSON and clients advertise both `application/json` and `text/event-stream` in `Accept`. The official SDK validates protocol metadata/routing headers, versions, request shapes and notification semantics; responses may use JSON or SSE framing according to the negotiated revision. Portal HTTP guards retain the 256 KiB ceiling and same-origin browser check. The server is stateless, has no standalone server-initiated GET stream, and GET `/mcp` returns **405**.
- **Notifications**: `notifications/initialized` returns **202 Accepted** with empty body (Streamable HTTP spec for response-less messages)
- **Tool discovery**: POST `/mcp` with `method: "tools/list"` returns all available tools with JSON Schema input definitions (queryable by clients)
- **Tool execution**: POST `/mcp` with `method: "tools/call"` + `params: {name, arguments}` executes tool and returns MCP content array
- **Tool result format**: success results include machine-readable `structuredContent` and the same serialized JSON in `content[0].text`; tool failures set `isError: true`. Modern wire responses additionally carry `resultType: "complete"` and server identity in `_meta`.
- **Available tools**: `search_articles`, `ask_knowledge`, `get_article`, `list_articles`, `list_tags`, `get_portal_info`, `get_project_context`, `get_integration_guidance`, `find_authoritative_content`, `compare_sources`, `get_recent_changes` (all snake_case)
- **Error handling**: JSON-RPC 2.0 error format on protocol errors: `{error: {code, message}, jsonrpc: "2.0"}`. Tool errors set `isError: true`, return the stable structured error object declared in `outputSchema`, and mirror that JSON in `content[0].text`. Unexpected tool exceptions are logged server-side with full detail; the client receives only a generic `tool_execution_failed` result without internal detail leakage.
- **search_articles pagination**: accepts `page` (1-based) + `limit` (1-50); returns true post-filter `total`, `page`, `limit`, `totalPages` (same paged pipeline as `GET /api/search`).
- **Shared MCP scope**: `search_articles`, `ask_knowledge`, `list_articles`, `get_project_context`, `get_integration_guidance`, `find_authoritative_content`, `compare_sources`, and `get_recent_changes` accept `scope.tags[]` (AND), `scope.contentTypes[]` (OR), and generic `scope.facets[]` values in `category:value` form. Values within one category use OR; categories combine with AND. Legacy flat scope fields remain accepted and are merged/deduplicated with the object form.
- **get_portal_info counts**: `totalAuthors` = distinct owners of published articles; `totalTags` = tags used by ≥1 published article (consistent with the published-only scope of all tools). `list_articles` `sort` is validated against `newest|oldest|most_viewed` — invalid values return `isError`.
- **Authentication**: **NO OAUTH.** All `/mcp` requests require ONE of:
  - **API Key**: `X-API-Key: kp_*` header (BCrypt hashed, prefix-indexed lookup)
  - **JWT Bearer**: `Authorization: Bearer <token>` header (HMAC-SHA256, 24h expiry)
  - Both methods are equivalent; choose based on use case (API keys for long-lived integrations, JWT for user sessions)
- **Stateless execution**: Each request is independent; no session state is maintained between requests
- **Rate limiting**: `/mcp` (GET + POST) is covered by the `mcp` fixed-window rate limit policy (default 60/min, `RateLimiting:McpLimit`)
- **Tool access control**: Tools do not enforce RBAC beyond authentication. All authenticated users can access all tools. Tools only return published articles.

## Future Enhancements (Not Implemented)

### Content-Aware Attachment Indexing and Parser Routing

- Prevent the same source text from being indexed twice when an imported Markdown/document is copied into `articles.content` and also retained as an attachment. Introduce an explicit per-attachment indexing mode such as `archive_only`, `text_only`, `visual_only`, or `full`; a retained original Markdown file should default to `archive_only` after its content becomes the canonical article body.
- Add normalized content/chunk fingerprints across the article body and attachments. Exact duplicates must be excluded before FTS weighting and embedding; near-duplicate handling must be evaluation-driven so boilerplate removal does not discard legitimate repeated policy clauses. Preserve the original attachment for authorized download and provenance even when it is not searchable.
- Keep document conversion behind a provider interface and evaluate Microsoft MarkItDown as an optional parser/sidecar, not an automatic replacement for the deterministic native .NET/OpenXML path. Route by file type and measured extraction confidence: native for born-digital Markdown/Office, optional MarkItDown for broader formats, and Unstructured `hi_res` or another approved layout service for scanned/complex documents. Parser selection must be decided by a representative golden corpus measuring table-cell accuracy, reading order, provenance, retrieval metrics, latency, resource cost, and failure rate.
- Replace unconditional visual processing with an adaptive policy: perceptual-hash duplicate logos/icons, reject low-information/tiny assets, prefer inexpensive OCR/layout extraction first, and invoke the multimodal model only for likely tables, diagrams, charts, scanned pages, or low-confidence OCR. Add a cross-document visual-description cache and configurable per-type/page budgets. Continue persisting the derived result so vision is ingestion-time work, never repeated per RAG query.
- Any rollout must preserve ACL/provenance, extraction-profile invalidation, durable retries, source citations, and native fallback. It must add golden-dataset cases that compare Recall/MRR/NDCG, table fact accuracy, citation correctness, ingestion latency, vision calls per document, and GPU/provider cost before enabling a new route in production.

## Generic Article Classifications (Implemented)

- Admin/editor users define categories and controlled values through `/api/lookups/categories` and `/api/lookups`; examples include Department, Team, System, Product, or Location. Category keys are stable after creation.
- Each category independently controls active state, required/default behavior, `single|multiple` cardinality, display order, and `none|filter` RAG behavior (default `filter`). Classification inputs require canonical lookup values. Arbitrary text/number/date fields remain out of scope.
- `content_type` is seeded initially but is not protected: administrators may change its cardinality, required/default state, RAG behavior and active state, and may remove it after its values/assignments are cleared. Existing `articles.content_type`, `contentType`, and `scope.contentTypes` contracts remain as compatibility surfaces while the category is adopted generically; inline search has no content-type-specific token.
- Values within a category use OR and different categories use AND. Explicit facets are enforced before article lists, bulk exports, full-text, vector, hybrid, or RAG candidates enter result/context sets. REST uses repeatable `facet=category:value`; query text uses `+category:value`; MCP uses `scope.facets[]`.
- Dynamic article fields, Articles/Search filters, and bulk-export classification filters come from active category definitions. Category and value `sortOrder` control their display order across these surfaces and can be edited in Lookup Management. JSONL/Markdown/CSV bulk transfers preserve classifications; CSV stores the classification object as JSON.
- Metadata-only changes update relational assignments and invalidate scoped answer-cache fingerprints without placing metadata into embedding text or forcing content re-embedding.

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
| Feature/mimari/iş akışı/config/güvenlik davranışı değişti | İlgili `AGENTS.md` + `specs/` bölümleri ve `backend/SeedData/articles/` içindeki kullanıcıya dönük proje makalesi |
| Yeni kapsamlı teknik yetenek eklendi | Uygun seed makalesi yoksa yeni bir `backend/SeedData/articles/NN-*.md` makalesi oluştur |

#### Kurallar

1. **Ekleme**: Yeni bir özellik/endpoint/entity eklendiğinde, ilgili tüm dokümantasyon bölümleri güncellenir. Eğer dosya yoksa oluşturulur.
2. **Kaldırma**: Bir özellik/endpoint/entity kaldırıldığında, ilgili dokümantasyon satırları/bölümleri silinir. Ölü referans bırakılmaz.
3. **Değişiklik**: Mevcut davranış değiştiğinde (ör. validation kuralı, enum değeri), eski bilgi güncellenir.
4. **Tutarlılık**: `specs/` dosyaları AGENTS.md ile çelişemez. Çelişki durumunda AGENTS.md geçerlidir ve `specs/` düzeltilir.
5. **types/api.ts senkronizasyonu**: Backend DTO değişikliği → `frontend/src/types/api.ts` aynı commit'te güncellenir.
6. **Ölü kod/field temizliği**: Kullanılmayan entity field, component veya endpoint kaldırıldığında hem kod hem dokümantasyondan silinir.
7. **Feature Status doğruluğu**: Yarım kalan iş "✅ Implemented" olarak işaretlenmez. Sadece tam çalışan özellikler işaretlenir.
8. **Seed makale senkronizasyonu**: `backend/SeedData/articles/*.md` örnek içerik değil, ürünle birlikte sunulan proje dokümantasyonudur. Davranışsal veya teknik bir değişiklik ilgili seed makalesine aynı change/commit içinde yansıtılır; uygun makale yoksa yenisi eklenir.
9. **Seed makale güncelliği**: Seed makalelerde kaldırılmış teknoloji, eski model adı, eski endpoint, eski yetki veya geçersiz operasyon adımı bırakılamaz. Seed makaleler kendi aralarında ve `AGENTS.md`/`specs/` ile tutarlı olmalıdır; çelişkide bu dosya esas alınır ve diğer dokümanlar düzeltilir.
10. **Mimari anlatım kapsamı**: RAG gibi kapsamlı bir özellik eklendiğinde veya önemli ölçüde değiştiğinde seed dokümantasyonu yalnızca özelliğin varlığını söylemekle yetinmez; uygulanmış mimariyi, veri akışını, önemli yapılandırmayı, güvenlik/dayanıklılık kontrollerini ve işletim/gözlemlenebilirlik noktalarını açıklar.

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
- [ ] Değişen özellik/mimari/iş akışı/config/güvenlik davranışı → ilgili seed makale aynı change içinde güncellendi mi?
- [ ] Yeni kapsamlı teknik yetenek → uygulanmış mimariyi anlatan bir seed makalesi var mı?
- [ ] Seed makalelerde eski teknoloji/model/endpoint/yetki/operasyon bilgisi veya dokümanlar arası çelişki kaldı mı?

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
