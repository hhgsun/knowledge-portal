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
- **Entities**: `backend/Models/Entities/` — 9 models: User, Article, ArticleVersion, ArticleView, Tag, ArticleTag, ArticleFeedback, ApiKey, SearchQuery
- **Seed data**: `DbInitializer.SeedAsync()` — admin user + 10 default tags
- **Port**: 5174
- **Rate Limiting**: ASP.NET Core built-in rate limiter on auth + search endpoints
- **Error format**: All errors return `{ "error": "Human-readable message" }`

### Frontend (`frontend/`)

- **Language**: TypeScript strict
- **State**: React Context (`AuthContext`) — no Redux/Zustand
- **API calls**: `useApi` hook (`src/hooks/useApi.ts`) — auto-attaches JWT, auto-logout on 401
- **Routing**: React Router v7, `ProtectedRoute` + `RoleRoute` wrappers in `App.tsx`
- **Components**: `src/components/layout/` (AppShell, Sidebar, Header), `src/components/editor/` (TipTap)
- **Notifications**: `sonner` toast library — use `toast.success()` / `toast.error()` for user feedback
- **Error Boundary**: `src/components/error-boundary.tsx` wraps the app
- **Pages**: `src/pages/` — flat directory, one file per page
- **CSS**: Tailwind CSS v4, utility-first, `cn()` helper from `src/lib/utils.ts`
- **Icons**: `lucide-react` only
- **Port**: 5173, API proxy `/api/*` → `http://localhost:5174`

## File Locations

```
backend/
├── Controllers/          # API endpoints (10 controllers)
├── Auth/                 # JwtService, RbacService, ApiKeyMiddleware, Permissions
├── Data/                 # AppDbContext, DbInitializer
├── Models/Entities/      # EF Core entity classes
├── Migrations/           # EF Core migrations
├── Program.cs            # App configuration & DI
└── appsettings.json      # Connection strings, JWT config

backend.Tests/
├── Integration/          # WebApplicationFactory integration tests
└── Unit/                 # Unit tests (RbacService, JwtService, etc.)

frontend/
├── src/contexts/         # AuthContext (JWT auth state)
├── src/hooks/            # useApi (fetch wrapper)
├── src/components/       # layout/ + editor/
├── src/pages/            # 13 page components
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
├── tech-stack.md         # All packages & versions
└── validation.md         # Smoke test checklist
```

## Default Credentials

- **Email**: `admin@knowledge.local`
- **Password**: `admin123`

## Commands

| Task | Command |
|------|---------|
| Run backend | `cd backend && dotnet run` |
| Run frontend | `cd frontend && npm run dev` |
| Run backend tests | `cd backend.Tests && dotnet test` |
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

## Feature Status

| Feature | Status | Notes |
|---------|--------|-------|
| JWT Auth | ✅ Implemented | 24h expiry, HMAC-SHA256 |
| API Key Auth | ✅ Implemented | kp_ prefix, BCrypt hash, prefix-indexed lookup |
| Articles CRUD | ✅ Implemented | Full lifecycle with versioning |
| Tags | ✅ Implemented | CRUD + article tagging |
| Search (fulltext) | ✅ Implemented | SQL LIKE on title/excerpt with wildcard escaping |
| Search (tag-based) | ✅ Implemented | @tag prefix syntax |
| Search (semantic) | ⏳ Placeholder | Returns fulltext results, needs embedding model |
| Search (hybrid) | ⏳ Placeholder | Returns fulltext results, needs embedding model |
| Search (RAG) | ⏳ Placeholder | Returns stub message |
| Analytics | ✅ Implemented | Session-only endpoint |
| Admin Users | ✅ Implemented | Session-only, self-protection |
| API Key Management | ✅ Implemented | Create/list/delete |
| Article Feedback | ✅ Implemented | Helpful/not-helpful + comments |
| Article Versions | ✅ Implemented | Created on content change |
| View Tracking | ✅ Implemented | Deduplicated per user/article/15min window |
| Rate Limiting | ✅ Implemented | Login, register, search endpoints |
| Health Check | ✅ Implemented | GET /api/health |
| Dark Mode Toggle | ❌ Not implemented | System preference only |
| Notifications | ❌ Not implemented | Bell icon is cosmetic only |
| User Profile Page | ❌ Not implemented | Profile button non-functional |
| Pagination UI | ❌ Not implemented | Backend supports it, frontend doesn't show controls |

## Key Behaviors

- **Slug regeneration**: When article title changes via PUT, slug is regenerated (if not conflicting)
- **Version creation**: Only triggered when `content` field changes (not title-only edits)
- **Viewer article visibility**: Viewers see published articles + their own (any status)
- **API key source**: Claims include `source: "api-key"` — session-only endpoints check this
- **Article list tags**: GET /api/articles response includes `tags` array per article
- **Search wildcard escaping**: `%` and `_` characters are escaped in LIKE queries
- **View deduplication**: Same user viewing same article within 15 minutes counts as 1 view

## Rules for AI Agents

### MUST DO
- Reference this file as the primary source of truth
- Use `Permissions` constants (not magic strings) for RBAC checks
- Use `useApi` hook for all authenticated frontend API calls
- Use `toast` from `sonner` for all user feedback (success/error)
- Use `[Authorize]` + `RequirePermission` for backend RBAC
- Use `RoleRoute` wrapper for frontend role-restricted pages
- Keep pages in `frontend/src/pages/` as flat files
- Return `{ "error": "..." }` for all error responses
- Write xUnit integration tests for new backend features
- Update this file's "Feature Status" table after implementing a feature

### MUST NOT
- Do NOT add Next.js, SSR, or server components — pure SPA + REST API
- Do NOT use ASP.NET Identity — auth is custom JWT via `JwtService`
- Do NOT add Redux, Zustand, or other state managers — use React Context
- Do NOT add component libraries (MUI, Chakra, etc.) — use Tailwind CSS + lucide-react + sonner
- Do NOT use magic permission strings — use `Permissions` class constants
- Do NOT store JWT in httpOnly cookies — current design uses localStorage (accepted trade-off for SPA)

### CONVENTIONS
- DB column names are snake_case; C# properties are PascalCase
- Viewers CAN create articles (status limited to draft/pending)
- Password minimum length is 8 characters everywhere
- All IDs are 21-character truncated GUIDs (hex, lowercase)
- Timestamps are ISO 8601 UTC in API responses
