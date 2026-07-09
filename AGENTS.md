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
| Editor | TipTap (ProseMirror) |
| Tests | xUnit + WebApplicationFactory (backend only) |
| MCP | REST API at `/mcp` (JSON-RPC 2.0 spec-compliant, **NO OAuth**, API Key or JWT auth only, stateless, tool discovery via `initialize` + `tools/list`) |

## Conventions

### Backend (`backend/`)

- **Language**: C# 13, .NET 10, nullable enabled
- **Pattern**: Controllers → EF Core DbContext → PostgreSQL (service layer for AI/embedding only)
- **Naming**: PascalCase for C# code, snake_case for DB columns (configured in `AppDbContext.OnModelCreating`)
- **Auth**: `[Authorize]` attribute on controllers, `[AllowAnonymous]` for public endpoints
- **RBAC**: `RequirePermission` attribute with permission constants from `Permissions` class
- **API prefix**: All routes under `/api/` (e.g. `/api/articles`, `/api/auth/login`)
- **Entities**: `backend/Models/Entities/` — 13 models: User (with AzureObjectId, Slug), Article, ArticleVersion, ArticleView, Tag, ArticleTag, ArticleVote, ArticleComment, ApiKey, SearchQuery, ArticleAttachment, LookupValue, ArticleEmbedding
- **Enum Validation**: `contentType` is validated server-side against `lookup_values` table (DB-driven, managed via `/api/lookups`)
- **Seed data**: `DbInitializer.SeedAsync()` — admin user + 10 default tags
- **Port**: 5174
- **Rate Limiting**: ASP.NET Core built-in rate limiter on auth + search endpoints (defaults: auth=10/min, search=30/min, configurable via `appsettings.json` → `RateLimiting`)
- **Middleware pipeline**: GlobalExceptionMiddleware → CORS → RateLimiter → ApiKeyMiddleware → Authentication → Authorization → Controllers
- **AI/Search**: Ollama integration (optional, `Ollama:Enabled` in appsettings.json). Embedding model: nomic-embed-text (768 dims). Chat model: llama3.2 (RAG). Background service polls for dirty articles. pgvector extension for vector storage (`vector(768)`) and cosine distance search in PostgreSQL. PostgreSQL tsvector/tsquery for full-text search with GIN index and weighted ranking (rebuilt on startup).
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
├── Controllers/          # API endpoints (14 controllers)
├── Auth/                 # JwtService, RbacService, ApiKeyMiddleware, Permissions, ClaimsPrincipalExtensions, RequirePermissionAttribute
├── Data/                 # AppDbContext, DbInitializer
├── Middleware/            # GlobalExceptionMiddleware
├── Helpers/              # ContentExtractor, AttachmentTextExtractor, SlugHelper, VectorMath
├── Logging/              # FileLoggerProvider (date-based file logging)
├── Mcp/                  # MCP tools (KnowledgePortalMcpTools) & REST API wrapper (McpController)
├── Services/             # EmbeddingService, VectorSearchService, RagService, EmbeddingBackgroundService, FullTextSearchService
├── Models/
│   ├── Dtos.cs           # All request/response DTOs (C# records)
│   └── Entities/         # EF Core entity classes (13 models: User, Article, ArticleVersion, ArticleView, Tag, ArticleTag, ArticleVote, ArticleComment, ApiKey, SearchQuery, ArticleAttachment, LookupValue, ArticleEmbedding)
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
├── src/pages/            # 17 page components
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
- **Password**: `1q2w3e*/`

## Seed Data

When the backend starts (`dotnet run`), it automatically seeds the database:
1. **Admin user** created if missing (email: `admin@finagotech.com.tr`, password: `1q2w3e*/`, role: admin)
2. **Default tags** added (project-knowledge-portal, getting-started, tutorial, etc.)
3. **Content types** added (reference, how-to, adr, runbook, faq, policy, onboarding)
4. **Articles** loaded from `backend/SeedData/articles/*.json` if Articles table is empty
   - Files are processed in order by filename
   - Each article's tags are assigned automatically
   - Content is stored as JSON (ProseMirror doc format)
   - Slug is auto-generated from title (with collision detection)
   - Articles marked as "published" get `PublishedAt` and `LastReviewedAt` timestamps

**To reset seed data**: Delete `data/knowledge-portal.db` (SQLite) or drop the database (PostgreSQL), then restart backend.

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
| `/api/auth/azure-login` | POST | ✗ | — | — |
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
| `/api/search/authors` | GET | ✓ | — | ✗ |
| `/api/search/click` | POST | ✓ | — | ✗ |
| `/api/search/reindex` | POST | ✓ | `users:manage` | ✓ |
| `/api/search/embedding-status` | GET | ✓ | `users:manage` | ✓ |
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
| `/api/lookups` | GET | ✓ | — | ✗ |
| `/api/lookups` | POST | ✓ | `tags:manage` | ✗ |
| `/api/lookups` | PUT | ✓ | `tags:manage` | ✗ |
| `/api/lookups?id={id}` | DELETE | ✓ | `tags:manage` | ✗ |
| `/api/logs` | GET | ✓ | `users:manage` | ✓ |
| `/api/logs/{fileName}` | GET | ✓ | `users:manage` | ✓ |
| `/api/logs/{fileName}` | DELETE | ✓ | `users:manage` | ✓ |
| `/mcp` | POST | ✓ | — | ✗ |

## Validation Rules

| Field | Min | Max | Notes |
|-------|-----|-----|-------|
| `password` | 8 | 128 | Required for register, login, admin user create/update |
| `email` | — | — | Valid email format, unique per user |
| `user.name` | 1 | — | Required |
| `article.title` | 1 | 300 | Required |
| `article.excerpt` | — | — | Optional, trimmed |
| `article.status` | — | — | Enum: draft, pending, published, archived |
| `article.contentType` | — | — | DB-driven via `lookup_values` table (category: content_type) |
| `tag.name` | 1 | 50 | Required, unique slug generated |
| `search.q` | 1 | — | Required |
| `search.limit` | 1 | 50 | Default 20 |
| `search.onlyOwnContent` | — | — | Optional, boolean. When true + API key auth → filters to articles created by that API key |
| `search.includeContent` | — | — | Optional, boolean. When true → includes article content as plain text (extracted from TipTap JSON) in search results |
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
| Search (fulltext) | ✅ Implemented | PostgreSQL tsvector/tsquery with GIN index and weighted ranking (fallback to ILIKE), content body indexed |
| Search (tag-based) | ✅ Implemented | @tag prefix syntax, multiple tags with AND logic |
| Search (semantic) | ✅ Implemented | Ollama embedding + chunking (~500 words/chunk) + SIMD cosine similarity, best-chunk scoring |
| Search (hybrid) | ✅ Implemented | Reciprocal Rank Fusion (α=0.4 fulltext + β=0.6 semantic, k=60) |
| Search (RAG) | ✅ Implemented | Ollama llama3.2, top-5 context, source citations |
| Search Click Tracking | ✅ Implemented | POST /api/search/click records which result was clicked |
| Analytics | ✅ Implemented | Session-only endpoint |
| Admin Users | ✅ Implemented | Session-only, self-protection |
| API Key Management | ✅ Implemented | Create/list/rotate/delete |
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

| User Profile Page | ✅ Implemented | Name/email update + password change via PUT /api/auth/profile |
| Pagination UI | ✅ Implemented | Articles list + Admin Users have prev/next controls |
| Article Attachments | ✅ Implemented | File upload/download/delete, TipTap image insert, max 20MB, extension whitelist |
| System Logs | ✅ Implemented | Date-based file logging (log_YYYYMMDD.log), view/delete via admin UI, today's log protected |

## Known Frontend Gaps

No known gaps at this time.

## Key Behaviors

- **Slug regeneration**: When article title changes via PUT, slug is regenerated (if not conflicting)
- **Version creation**: Triggered when `content` field changes (not title-only or metadata-only edits)
- **Version restore**: POST `/api/articles/{id}/versions/{versionId}/restore` copies version content/title back to article, creates a new version with "Restored to version N" summary, recalculates read time
- **LastReviewedAt**: Automatically set to UTC now when article status becomes `published` (via direct update or approve action)
- **Read time calculation**: Auto-calculated from content text (~200 words/min), updated on create and content change
- **Viewer article visibility**: Viewers see published articles + their own (any status)
- **Azure AD login**: Frontend uses MSAL.js v5 redirect-bridge popup flow → popup opens → Azure AD auth → popup calls `broadcastResponseToMainFrame()` via BroadcastChannel → parent receives auth code → PKCE exchange → gets access token → POST `/api/auth/azure-login` → backend validates via Microsoft Graph `/me` → finds/creates local user by AzureObjectId or email → returns local JWT. Popup callback page: `/auth-popup-callback.html` (Vite multi-page entry). If user has active Azure session, login page auto-attempts silent login.
- **Azure AD logout**: `msalInstance.clearCache()` called on logout to prevent auto-silent re-login.
- **Azure AD user linking**: First Azure login links by email if user exists, otherwise creates new viewer user. AzureObjectId stored for future logins. Profile name synced from Azure on each login.
- **Azure AD password set**: Azure users can set a local password via PUT `/api/auth/profile` without providing `currentPassword` (first-time set). After setting, both Azure and email+password login work.
- **`/api/auth/me` response**: Includes `isAzureUser` boolean field (true if user has AzureObjectId linked).
- **API key source**: Claims include `source: "api-key"` — session-only endpoints check this
- **Article list tags**: GET /api/articles response includes `tags` array per article
- **Tag input flexibility**: `Tags` array in create/update accepts tag ID, tag name, or tag slug — resolved in that priority order. When request comes via API key, unknown tags are auto-created.
- **Search wildcard escaping**: `%` and `_` characters are escaped in LIKE queries
- **Search inline syntax**: `@user-slug` for author filter (OR, multiple), `#tag-slug` for tag filter (AND, multiple), `##content-type` for content type filter (OR, multiple). Parsed in order: `##` → `#` → `@` → remaining text. Example: `@ahmet #react ##guide nasıl yapılır`. Inline syntax and query parameters are merged.
- **Search filters**: `GET /api/search` accepts optional query parameters: `onlyOwnContent` (boolean, API key auth only — filters to articles created by that API key), `includeContent` (boolean — includes article content as extracted plain text in results), `includeAttachments` (boolean — includes attachment metadata array per article), `tag` (repeatable, tag slugs), `author` (repeatable, user slugs), `contentType` (repeatable, content type values). Filters apply to all search types (fulltext, semantic, hybrid, rag). Tags from `#syntax` and `tag` param are merged. Authors from `@syntax` and `author` param are merged. Content types from `##syntax` and `contentType` param are merged. If only tags are specified without a text query, returns tag-browse results.
- **Search click tracking**: Search responses include `searchQueryId` — clients POST `/api/search/click` with article clicked
- **Search semantic**: pgvector cosine distance operator (`<=>`) on `vector(768)` column in `article_embeddings` table. Best-chunk scoring per article (MIN distance → highest similarity). MinSimilarityScore=0.3 (configurable via appsettings.json).
- **Search hybrid**: Reciprocal Rank Fusion (α=0.4 fulltext + β=0.6 semantic, k=60). Each result has `matchType` (fulltext/semantic/both). Falls back to fulltext-only if Ollama unavailable.
- **Search RAG**: Top-5 semantic results → article context (max 3000 words) → Ollama llama3.2 → answer with source citations. Response includes `sources: [{articleId, title, slug, score}]`.
- **Search indexing**: Dirty flag pattern — controllers set `IndexedAt=null` on publish/content-change/approve/attachment-upload/attachment-delete. EmbeddingBackgroundService polls every 5s, batch size 10. On startup invalidates stale model embeddings. Articles are chunked (~500 words, 50-word overlap) before embedding. Full-text search vector synced on publish/update/delete/approve/attachment-change.
- **Search responses**: All search types include `indexingPending` boolean (true if any published article has IndexedAt=null). Semantic/hybrid/rag include `warning` string when Ollama unavailable.
- **View deduplication**: Same user viewing same article within 15 minutes counts as 1 view (hardcoded window)
- **Vote toggle**: POST `/api/articles/{id}/vote` with same `isHelpful` value → removes vote. Different value → changes vote. No existing vote → creates vote. One vote per user per article (unique constraint).
- **Vote reason**: `reason` field is only accepted when `isHelpful: false`. Free-text, optional.
- **Wilson Score**: Lower bound of Wilson score confidence interval (95%, z=1.96). Returned in article list and votes endpoint.
- **Comments independent of votes**: Users can leave comments without voting. Multiple comments per user allowed. Own comments can be deleted; admins can delete any.
- **View count in responses**: `GET /api/articles` list and `GET /api/articles/{idOrSlug}` detail both include `viewCount` field.
- **Article detail response**: `GET /api/articles/{idOrSlug}` includes `content` (TipTap JSON for editor), `contentText` (extracted plain text for API consumers), and `attachments` array (id, fileName, contentType, sizeBytes, downloadUrl).
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
- **Attachment indexing**: Text content of attachments (.pdf, .docx, .txt, .md, .csv, .json, .yaml) is extracted and included in both FTS5 and embedding indexes. Max 50K chars per attachment. Unsupported/corrupted files are silently skipped. Adding/removing attachments on published articles triggers re-indexing.
- **Attachment download**: Served via controller (auth required), not static file middleware. `PhysicalFile()` streams the file with correct Content-Type and Content-Disposition.
- **Lookup color/icon**: LookupValue entity has optional `Color` (Tailwind color key) and `Icon` (Lucide icon name) fields. Frontend renders content type badges with colored backgrounds and icons via `ContentTypeBadge` component. Color picker supports all 20 Tailwind color keys dynamically. Icon picker dynamically loads all lucide-react icons with search/filter. Utilities in `src/lib/lookup-utils.ts`, picker components in `src/components/lookup-pickers.tsx`.

## MCP Server Behaviors

- **MCP protocol version**: 2024-11-05 (JSON-RPC 2.0 spec-compliant)
- **Server discovery**: POST `/mcp` with `method: "initialize"` returns server capabilities, protocol version, and implementation info
- **Tool discovery**: POST `/mcp` with `method: "tools/list"` returns all available tools with JSON Schema input definitions (queryable by clients)
- **Tool execution**: POST `/mcp` with `method: "tools/call"` + `params: {name, arguments}` executes tool and returns result as JSON string
- **Error handling**: All methods return JSON-RPC 2.0 error format on failure: `{error: {code, message}, jsonrpc: "2.0"}`
- **Authentication**: **NO OAUTH.** All `/mcp` requests require ONE of:
  - **API Key**: `X-API-Key: kp_*` header (BCrypt hashed, prefix-indexed lookup)
  - **JWT Bearer**: `Authorization: Bearer <token>` header (HMAC-SHA256, 24h expiry)
  - Both methods are equivalent; choose based on use case (API keys for long-lived integrations, JWT for user sessions)
- **Stateless execution**: Each request is independent; no session state is maintained between requests
- **Tool access control**: Tools do not enforce RBAC beyond authentication. All authenticated users can access all tools. Tools only return published articles.

## Placeholder Fields (Not Yet Active)

These entity fields exist in the database but are not yet used in business logic:

| Field | Entity | Purpose | Status |
|-------|--------|---------|--------|

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
- Viewers CAN create articles (status limited to draft/pending on create; publish/archive blocked by permission on update)
- Password minimum length is 8 characters everywhere
- All IDs are 21-character truncated GUIDs (hex, lowercase)
- Timestamps are ISO 8601 UTC in API responses
- `useApi` hook returns `{ fetchWithAuth }` — callers chain `.then(r => r.json())` for data
- Query-param based DELETE: Tags (`?id=`), Admin Users (`?id=`), API Keys (`?id=`)
- Admin Users PUT: `userId` is in the request body, not in the URL path
- Import API types from `src/types/api.ts` for type safety
- Rate limits are configurable via `appsettings.json` → `RateLimiting:AuthLimit` / `RateLimiting:SearchLimit`
