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
     │  │  Auth · content · Search · Assistant RAG · MCP      │  │
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
| **Services** | `backend/Services/` | Domain logic + shared REST/MCP search (`SearchExecutionService`) + shared analytics reporting + isolated assistant router/policy/orchestrator + AI/indexing + observability |
| **Auth** | `backend/Auth/` | JWT issuance, token validation, API key middleware, RBAC (principal-aware, API-key cap) |
| **Data** | `backend/Data/` | EF Core DbContext, seed data, migrations |
| **RAG query/context** | `RagQueryUnderstandingService`, `HybridRagRetriever`, `RagContextExpansionService`, `IRagContextBuilder` | Deterministic rewrite/filter/decomposition → hybrid child retrieval → rerank/ranking signals → ACL-safe child→parent resolution → bounded evidence context |
| **Document parsing** | `AttachmentProcessingService`, `AttachmentTextExtractor` | Native layout/table extraction → optional versioned Unstructured hi_res → bounded local-VLM visual OCR/description for image/PDF/DOCX/XLSX/PPTX assets → cached provenance segments shared by FTS/vector; parser/vision budgets and extraction limit participate in index freshness |
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
| `/assistant` | AssistantPage | Protected; build-time flag plus authenticated runtime capability discovery |
| `/profile` | ProfilePage | Protected |
| `/analytics` | AnalyticsPage | Protected (admin/editor) |
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
│ • Assistant / Search   │                 │
│ • Analytics            │                 │
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
2. **Provider-aware hierarchical hybrid retrieval** — PostgreSQL FTS and pgvector supply lexical and semantic child candidates; Ollama provides optional embeddings/chat. Structure-bounded parents preserve Markdown sections and parser page/sheet/slide provenance, while smaller overlapping children provide precise retrieval. Both paths reuse child→parent identity after ACL recheck. Parent/child targets, overlap and version are configurable. When Ollama is unavailable, lexical search remains available and semantic modes report an explicit fallback warning.
   Attachment preprocessing is canonical and shared: native OpenXML/CSV tables remain GFM Markdown, visual assets are described/OCR'd by the bounded local multimodal model, and complex scanned layouts may use an explicitly enabled Unstructured `hi_res` endpoint. External document transfer is off by default.
3. **Centralized DTOs** — Request/response shapes are C# records defined in `backend/Models/Dtos.cs`.
4. **21-char truncated GUIDs** — Entity IDs are `Guid.NewGuid().ToString("N")[..21]`. Not globally unique in the mathematical sense but collision-resistant for a single-database deployment.
5. **Cascade deletes** — Deleting an article cascades to versions, tags, feedback, and views. Deleting a user cascades to API keys. API key deletion sets `created_via_api_key_id` to null on articles.
6. **UTC timestamps** — All `DateTime` values stored and transmitted in UTC.
7. **Private attachment delivery** — Attachment downloads and inline images use authenticated bearer requests and apply the same article-visibility policy as the article itself; credentials are never placed in URLs.
8. **Durable indexing with eager lexical visibility** — Article changes invalidate separate lexical (`FtsIndexedAt`) and semantic (`IndexedAt`) state and first enqueue a generation-guarded, leased PostgreSQL job. The request then best-effort refreshes local PostgreSQL FTS (savepoint-isolated inside wider import transactions), while semantic embedding remains asynchronous. The worker claims no more jobs than it can run, enforces a configurable per-article timeout, and always re-runs FTS before embedding. Routine admin repair targets only dirty missing/delayed/failed/lease-expired jobs; corpus-wide reindex remains a separate maintenance operation.
9. **Strict Search/Assistant boundary** — `SearchExecutionService` returns documents only (`fulltext`, `semantic`, `hybrid`). `KnowledgeAnswerService` is the sole grounded-RAG pipeline for REST Assistant and MCP `ask_knowledge`; neither surface falls back into the other. `KnowledgeQueryScopeService` shares only filter semantics. Owned conversations provide bounded follow-up context, and the grounded semantic cache is isolated by principal scope and corpus/runtime versions.

## Future Consideration: Controlled Dynamic Metadata Facets

`lookup_values` currently supports only the `content_type` category in application behavior. A future iteration may generalize it into a controlled metadata taxonomy for bounded facets that users genuinely search by, such as `product`, `system`, `business_domain`, `owning_team`, `audience`, or `environment`. This is a retrieval and governance improvement, not an increase in the corpus or the model's intrinsic knowledge: embeddings and article content remain the primary semantic evidence.

The implementation must not merely allow arbitrary category strings. It should introduce explicit category definitions (label, single/multiple cardinality, required/default behavior, active state, and RAG use such as `none`, `filter`, or `boost`), controlled values with aliases, and article-to-value assignments. Existing first-class fields such as status, author, approval state, and free-form tags must not be duplicated as lookup facets.

RAG integration must be delivered end to end:

- Persist and validate article assignments, including safe deactivation and in-use protections.
- Expose relevant facets consistently through REST, MCP, imports, exports, article forms, and search UI.
- Extend query understanding to resolve category/value aliases. Explicit user-selected facets may be hard filters; metadata inferred from natural language should normally be a ranking boost unless confidence is high, so incomplete classification does not unnecessarily reduce recall.
- Make filterable metadata available to PostgreSQL lexical/vector candidate retrieval with appropriate denormalization and indexes. Metadata-only edits should refresh retrieval metadata without requiring content re-embedding unless metadata is intentionally included in embedding text.
- Include selected metadata in RAG evidence/context only when it improves disambiguation or governance; metadata must never be treated as a substitute for cited article evidence.
- Ranking authority is centrally governed by dynamic content-type `lookup_values.authority_weight` and bounded to 0-100. Hybrid and RAG rerankers receive that value from the database; per-content-type `Ollama:Ranking:Authority:*` configuration is not used. `Ollama:Ranking:AuthorityWeight` remains only the bounded contribution multiplier.
- Add golden-dataset cases for facet-bearing and unclassified content, then compare Recall, MRR, NDCG, grounding/citation coverage, refusal rate, and latency before enabling a category in production.

Rollout should start with one or two low-cardinality, high-value facets backed by real query patterns. Additional categories should be added only after metadata coverage and evaluation results demonstrate a retrieval benefit; uncontrolled or high-cardinality categories should remain tags or ordinary article content.

## Future Consideration: Content-Aware Attachment Indexing and Parser Routing

Retaining an imported source file as an attachment is useful for provenance and authorized download, but indexing the same Markdown/text from both `articles.content` and that attachment wastes FTS/vector capacity and can crowd RAG context with equivalent evidence. A future iteration should add a per-attachment indexing policy (`archive_only`, `text_only`, `visual_only`, `full`) plus normalized body/attachment chunk fingerprints. Imported Markdown copied into the canonical article body should normally retain its original as `archive_only`; documents whose body text is already canonical may use `visual_only` to contribute only non-duplicate table/diagram/image evidence. Exact duplicates should be removed before FTS weighting and embedding while keeping the stored file and provenance intact. Near-duplicate suppression must be gated by corpus evaluation.

Document conversion should remain provider-based. The deterministic native .NET/OpenXML extractor stays the fast path for born-digital supported formats; Microsoft MarkItDown may be evaluated as an optional Python sidecar for broader conversion, while Unstructured `hi_res` or another approved layout service remains the escalation path for scanned/complex layouts. Routing must use file type and extraction-confidence signals and must be selected from a representative golden corpus measuring reading order, table-cell accuracy, provenance, retrieval quality, latency, resource consumption, and failure behavior—not library breadth alone.

Visual processing should become adaptive rather than sending every eligible asset to the multimodal model: suppress repeated logos/icons with perceptual hashes, skip tiny/low-information assets, try inexpensive OCR/layout extraction first, and escalate likely tables, charts, diagrams, scanned pages, or low-confidence OCR. Cache visual descriptions across documents and enforce per-format/page budgets. Derived descriptions remain ingestion-time cached data and must never trigger a vision call during ordinary search/RAG queries. The rollout must preserve ACL, durable retry/fallback, extraction-profile invalidation and citations, and add quality/cost gates including table fact accuracy and vision calls per document.
