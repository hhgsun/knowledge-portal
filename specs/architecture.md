# System Architecture

> **⚠️ Bu dosya `AGENTS.md`'ye tabidir.** Çelişki durumunda `AGENTS.md` geçerlidir.
> Conventions, File Locations, RBAC Matrix → `AGENTS.md`

## Topology

```
┌─────────────────────────────────────────────────────────────────────┐
│  Browser                                                            │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │  React 19 SPA (port 5173)                                    │  │
│  │  ┌──────────┐ ┌──────────┐ ┌────────────┐ ┌──────────────┐  │  │
│  │  │AuthContext│ │ useApi() │ │ React Rtr7 │ │ Milkdown    │  │  │
│  │  └────┬─────┘ └────┬─────┘ └──────┬─────┘ └──────────────┘  │  │
│  └───────┼─────────────┼──────────────┼─────────────────────────┘  │
│          │  JWT Bearer  │  /api/*      │                            │
└──────────┼─────────────┼──────────────┼────────────────────────────┘
           │             │              │
     ┌─────▼─────────────▼──────────────▼──────────────────────┐
     │  Vite Dev Proxy  /api/* → http://localhost:5174         │
     └─────────────────────────┬───────────────────────────────┘
                               │
     ┌─────────────────────────▼───────────────────────────────┐
     │  ASP.NET Core Web API (port 5174)                       │
     │                                                         │
     │  Ordered security/auth middleware (detailed below)       │
     │                           │                             │
     │                           │                             │
     │  ┌────────────────────────▼──────────────────────────┐  │
     │  │  API Controllers                                   │  │
     │  │  Auth · content · search/RAG · admin · MCP         │  │
     │  └──────────────────────┬────────────────────────────┘  │
     │                         │                               │
     │  ┌──────────────────────▼────────────────────────────┐  │
     │  │  Services → EF Core DbContext                     │  │
     │  └──────────────────────┬────────────────────────────┘  │
     └─────────────────────────┼───────────────────────────────┘
                               │
     ┌─────────────────────────▼───────────────────────────────┐
     │  PostgreSQL + pgvector                                  │
     └─────────────────────────────────────────────────────────┘
```

## Layering

The system is a **split monorepo** with a shared service layer between controllers and data:

| Layer | Location | Responsibility |
|-------|----------|---------------|
| **Presentation** | `frontend/src/` | React SPA — routing, state, UI rendering |
| **API** | `backend/Controllers/` | HTTP endpoint mapping, request validation, auth scoping, response shaping |
| **Services** | `backend/Services/` | Domain logic (`ArticleMutationService`, `ContentTypeService`, Article/Tag/API-key/User/Stats services) + shared REST/MCP search orchestration (`SearchExecutionService`) + AI/indexing + observability |
| **Auth** | `backend/Auth/` | JWT issuance, token validation, API key middleware, RBAC (principal-aware, API-key cap) |
| **Data** | `backend/Data/` | EF Core DbContext, seed data, migrations |
| **RAG query/context** | `RagQueryUnderstandingService`, `HybridRagRetriever`, `RagContextExpansionService`, `IRagContextBuilder` | Deterministic rewrite/filter/decomposition → hybrid multi-query fusion → rerank/ranking signals → ACL-safe parent-neighbor expansion → bounded evidence context |
| **Domain** | `backend/Models/` | Entity classes, DTO records (`Models/Dtos.cs`) |
| **Storage** | PostgreSQL + pgvector | Relational data + vector embeddings (FTS + semantic search) |

## Middleware Pipeline

Request processing order in ASP.NET Core:

```
Request → ForwardedHeaders → HSTS (non-dev) → SecurityHeaders → GlobalExceptionMiddleware → CORS → ApiKeyMiddleware → JwtBearerAuth → UsageTrackingMiddleware → RateLimiter → Authorization → Controller
```

1. **ForwardedHeaders** — rewrites client IP/scheme from `X-Forwarded-For`/`X-Forwarded-Proto` (trusted proxies via `ForwardedHeaders:KnownProxies`/`KnownNetworks` config; TLS terminates at the company reverse proxy).
2. **HSTS + SecurityHeaders** — `Strict-Transport-Security` (non-dev, https requests), `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer` on every response.
3. **GlobalExceptionMiddleware** — catches unhandled exceptions, logs them, returns `{ "error": "An unexpected error occurred." }` with HTTP 500.
4. **CORS** — configured origins with any header/method and credentials.
5. **ApiKeyMiddleware** — intercepts `X-API-Key: kp_*` headers. Extracts the 8-char prefix after `kp_`, performs a prefix-indexed database lookup, BCrypt-verifies the raw key against matched candidates, and sets `HttpContext.User` with claims (including `source: "api-key"`). Non-matching requests pass through unmodified.
6. **JWT Bearer Authentication** — validates standard JWT tokens against configured issuer, audience, and signing key.
7. **UsageTrackingMiddleware** — records authenticated REST usage outcome, latency, user/API-key identity and bounded operation metadata.
8. **RateLimiter** — partitioned fixed-window limiting (auth: 10/min, search: 30/min, mcp: 60/min; partition key = API key id > user id > client IP). Runs after auth so partitioning sees the principal. Returns 429 when exceeded.
9. **Authorization** — enforces `[Authorize]`, `[RequirePermission("...")]`, and `[RequireSessionAuth]` attributes.

## Authentication Model

Two parallel authentication mechanisms share the same `ClaimsPrincipal` shape:

| Mechanism | Token format | Lifetime | Storage | Use case |
|-----------|-------------|----------|---------|----------|
| **JWT Session** | Standard JWT | 24 hours (configurable) | `localStorage` | Interactive browser sessions |
| **API Key** | `kp_` + 32 hex chars | 1–365 days (configurable) | BCrypt hash in DB | Automated/programmatic access |

Both produce identical claim sets (`id`, `email`, `name`, `role`) plus a discriminator claim (`source: "session"` or `source: "api-key"`). Certain endpoints (user management, analytics, API key management) reject `source: "api-key"` explicitly.

## RBAC Model

Static role-permission matrix with three roles (admin, editor, viewer).

> **Full permission matrix**: See `AGENTS.md` → "RBAC Permission Matrix"

Viewers can **create, edit, publish, and delete their own articles**. They cannot archive, approve, or manage tags. Approval is an optional editor/admin trust signal on an already-published article; it is not a publication gate.

## Frontend Architecture

### State Management

Two focused React contexts manage global authentication and theme state; feature/page state stays local:

| State | Type | Persistence |
|-------|------|-------------|
| `user` | `{ id, name, email, role } \| null` | Derived from JWT; validated on mount via `GET /api/auth/me` |
| `token` | `string \| null` | `localStorage` key `"token"` |
| `loading` | `boolean` | Transient |
| `theme` | `light \| dark \| system` | `localStorage` via `ThemeContext` |

No additional state management libraries (Redux, Zustand, etc.). All page-level state is component-local via `useState`.

### API Communication

All authenticated API calls go through the `useApi()` hook which:
1. Injects `Authorization: Bearer {token}` header
2. Auto-sets `Content-Type: application/json` for string bodies
3. Triggers `logout()` on any 401 response (auto-redirect to `/login`)
4. Returns the raw `Response` object for caller-side parsing

### Routing

React Router v7 with a single `<BrowserRouter>`:

| Route | Page | Protection |
|-------|------|-----------|
| `/login` | LoginPage | Public |
| `/register` | RegisterPage | Public |
| `/` | HomePage | Protected |
| `/articles` | ArticlesPage | Protected |
| `/articles/new` | NewArticlePage | Protected |
| `/articles/:slug` | ArticleViewPage | Protected |
| `/articles/:slug/edit` | EditArticlePage | Protected |
| `/articles/:slug/versions` | VersionsPage | Protected |
| `/search` | SearchPage | Protected |
| `/profile` | ProfilePage | Protected |
| `/analytics` | AnalyticsPage | Protected |
| `/tags` | TagsPage | Protected (admin/editor) |
| `/settings/bulk-transfer` | BulkTransferPage | Protected (admin/editor) |
| `/articles/import` | KnowledgeImportPage | Protected |
| `/admin/users` | AdminUsersPage | Protected |
| `/admin/keys` | AdminApiKeysPage | Protected |
| `/settings/lookups` | LookupsPage | Protected (admin/editor) |
| `/settings/featured-links` | FeaturedLinksPage | Protected (admin) |
| `/settings/logs` | LogsPage | Protected (admin) |
| `/settings/search` | SearchDiagnosticsPage | Protected (admin) |
| `/settings/rag-evaluations` | RagEvaluationsPage | Protected (admin) |

Protected routes use a `<ProtectedRoute>` wrapper that redirects to `/login` if `user` is null.

### Layout

```
┌──────────────────────────────────────────┐
│ Sidebar (left, sticky) │ <Page Content>  │
│                        │ (via <Outlet/>) │
│ • Home                 │                 │
│ • Articles / Import    │                 │
│ • Search / Analytics   │                 │
│ • Role-based settings  │                 │
└────────────────────────┴─────────────────┘
```

Auth pages (`/login`, `/register`) render only the outlet; protected pages render the sidebar plus outlet.

## Content Model

Articles store **canonical CommonMark/GFM Markdown**. The content is:
- Stored verbatim in the `articles.content` and `article_versions.content` text columns
- Exposed as `contentMarkdown` in API request and response payloads
- Edited by `MilkdownEditor`, using Milkdown Crepe's ProseMirror-based WYSIWYG experience
- Rendered read-only with `react-markdown` and `remark-gfm`
- Converted to derived plain text by `ContentExtractor` for read time, search, embeddings, and `contentText`

GFM features include headings, lists and task lists, blockquotes, fenced code, links, images, and tables.

## Key Design Decisions

1. **Shared service layer** — Controllers retain routing, authorization scoping, and response shaping; reusable domain behavior lives in `backend/Services/`.
2. **Provider-aware hybrid retrieval** — PostgreSQL FTS and pgvector supply lexical and semantic candidates; Ollama provides optional embeddings/chat. Structure-aware chunking preserves Markdown sections and parser page/sheet/slide provenance, with configurable target/overlap/version settings. When Ollama is unavailable, lexical search remains available and semantic modes report an explicit fallback warning.
3. **Centralized DTOs** — Request/response shapes are C# records defined in `backend/Models/Dtos.cs`.
4. **21-char truncated GUIDs** — Entity IDs are `Guid.NewGuid().ToString("N")[..21]`. Not globally unique in the mathematical sense but collision-resistant for a single-database deployment.
5. **Cascade deletes** — Deleting an article cascades to versions, tags, feedback, and views. Deleting a user cascades to API keys. API key deletion sets `created_via_api_key_id` to null on articles.
6. **UTC timestamps** — All `DateTime` values stored and transmitted in UTC.
7. **Private attachment delivery** — Attachment downloads and inline images use authenticated bearer requests and apply the same article-visibility policy as the article itself; credentials are never placed in URLs.
8. **Durable indexing with eager lexical visibility** — Article changes invalidate separate lexical (`FtsIndexedAt`) and semantic (`IndexedAt`) state and first enqueue a generation-guarded, leased PostgreSQL job. The request then best-effort refreshes local PostgreSQL FTS (savepoint-isolated inside wider import transactions), while semantic embedding remains asynchronous. The worker claims no more jobs than it can run, enforces a configurable per-article timeout, and always re-runs FTS before embedding. Routine admin repair targets only dirty missing/delayed/failed/lease-expired jobs; corpus-wide reindex remains a separate maintenance operation.
