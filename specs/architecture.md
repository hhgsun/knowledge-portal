# System Architecture

## Topology

```
┌─────────────────────────────────────────────────────────────────────┐
│  Browser                                                            │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │  React 19 SPA (port 5173)                                    │  │
│  │  ┌──────────┐ ┌──────────┐ ┌────────────┐ ┌──────────────┐  │  │
│  │  │AuthContext│ │ useApi() │ │ React Rtr7 │ │ TipTap Editor│  │  │
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
     │  Middleware Pipeline (ordered):                          │
     │  ┌──────┐ ┌──────────────────┐ ┌──────┐ ┌───────────┐  │
     │  │ CORS │→│ ApiKeyMiddleware │→│ Auth │→│ Authorize │  │
     │  └──────┘ └──────────────────┘ └──────┘ └─────┬─────┘  │
     │                                               │         │
     │  ┌────────────────────────────────────────────▼──────┐  │
     │  │  10 Controllers                                   │  │
     │  │  Auth · Articles · Tags · Search · Dashboard      │  │
     │  │  Analytics · AdminUsers · ApiKeys · Feedback       │  │
     │  │  ArticleVersions                                   │  │
     │  └──────────────────────┬────────────────────────────┘  │
     │                         │                               │
     │  ┌──────────────────────▼────────────────────────────┐  │
     │  │  EF Core DbContext (9 DbSets)                     │  │
     │  └──────────────────────┬────────────────────────────┘  │
     └─────────────────────────┼───────────────────────────────┘
                               │
     ┌─────────────────────────▼───────────────────────────────┐
     │  SQLite  ../data/knowledge.db                           │
     └─────────────────────────────────────────────────────────┘
```

## Layering

The system is a **two-tier split monorepo** with no intermediate service layer:

| Layer | Location | Responsibility |
|-------|----------|---------------|
| **Presentation** | `frontend/src/` | React SPA — routing, state, UI rendering |
| **API** | `backend/Controllers/` | HTTP endpoint mapping, request validation, response shaping |
| **Auth** | `backend/Auth/` | JWT issuance, token validation, API key middleware, RBAC |
| **Data** | `backend/Data/` | EF Core DbContext, seed data, migrations |
| **Domain** | `backend/Models/` | Entity classes, request records (inline DTOs) |
| **Storage** | `data/knowledge.db` | SQLite database file |

There is **no dedicated service/business-logic layer** (`backend/Services/` exists but is empty). All business logic resides in controller action methods.

## Middleware Pipeline

Request processing order in ASP.NET Core:

```
Request → CORS → ApiKeyMiddleware → JwtBearerAuth → Authorization → Controller
```

1. **CORS** — allows `localhost:5173` and `localhost:3000` with any header/method and credentials.
2. **ApiKeyMiddleware** — intercepts `Authorization: Bearer kp_*` headers. Iterates all API keys in the database, BCrypt-verifies the raw key, and sets `HttpContext.User` with claims (including `source: "api-key"`). Non-matching requests pass through unmodified.
3. **JWT Bearer Authentication** — validates standard JWT tokens against configured issuer, audience, and signing key.
4. **Authorization** — enforces `[Authorize]` and `[RequirePermission("...")]` attributes.

## Authentication Model

Two parallel authentication mechanisms share the same `ClaimsPrincipal` shape:

| Mechanism | Token format | Lifetime | Storage | Use case |
|-----------|-------------|----------|---------|----------|
| **JWT Session** | Standard JWT | 24 hours (configurable) | `localStorage` | Interactive browser sessions |
| **API Key** | `kp_` + 32 hex chars | 1–365 days (configurable) | BCrypt hash in DB | Automated/programmatic access |

Both produce identical claim sets (`id`, `email`, `name`, `role`) plus a discriminator claim (`source: "session"` or `source: "api-key"`). Certain endpoints (user management, analytics, API key management) reject `source: "api-key"` explicitly.

## RBAC Model

Static role-permission matrix with three roles:

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

Viewers can **create, edit, and delete their own articles** but are restricted to `draft` or `pending` status only. They cannot publish, archive, or manage tags. Publishing requires editor/admin approval via the `approve` workflow.

## Frontend Architecture

### State Management

Single React Context (`AuthContext`) manages all global state:

| State | Type | Persistence |
|-------|------|-------------|
| `user` | `{ id, name, email, role } \| null` | Derived from JWT; validated on mount via `GET /api/auth/me` |
| `token` | `string \| null` | `localStorage` key `"token"` |
| `loading` | `boolean` | Transient |

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
| `/analytics` | AnalyticsPage | Protected |
| `/admin/users` | AdminUsersPage | Protected |
| `/settings/keys` | SettingsKeysPage | Protected |

Protected routes use a `<ProtectedRoute>` wrapper that redirects to `/login` if `user` is null.

### Layout

```
┌─────────────────────────────────────────────────────┐
│ Sidebar (left, sticky)  │  Header (top, sticky)     │
│                         │───────────────────────────│
│ • Home                  │  Search bar    🔔 👤 Exit │
│ • Articles              │───────────────────────────│
│   └ New Article         │                           │
│ • Search                │  <Page Content>           │
│ • Analytics             │  (via <Outlet/>)          │
│ ─── Admin ───           │                           │
│ • Users (admin only)    │                           │
│ • API Keys (admin/ed.)  │                           │
└─────────────────────────┴───────────────────────────┘
```

Auth pages (`/login`, `/register`) render without the sidebar/header shell.

## Content Model

Articles store content as **TipTap JSON** (ProseMirror document model). The JSON is:
- Stored as a text column in the `articles.content` and `article_versions.content` fields
- Serialized/deserialized via `System.Text.Json` on the backend
- Rendered client-side by a custom `TiptapRenderer` component (not the editor itself)
- Editable via the `TiptapEditor` component with a formatting toolbar

Supported node types: paragraph, heading (1–3), bulletList, orderedList, listItem, taskList, taskItem, blockquote, codeBlock, horizontalRule, hardBreak, image, table (row/cell/header).

Supported marks: bold, italic, strikethrough, code, link, highlight.

## Key Design Decisions

1. **No service layer** — Business logic lives in controllers. Acceptable for current complexity; becomes a liability if controller methods exceed ~80 lines.
2. **No external dependencies** — Fully self-contained. Search is SQL LIKE; semantic/RAG endpoints are stubs returning placeholder responses.
3. **Inline DTOs** — Request/response shapes are C# records defined inside controller files. No shared DTO library.
4. **21-char truncated GUIDs** — Entity IDs are `Guid.NewGuid().ToString("N")[..21]`. Not globally unique in the mathematical sense but collision-resistant for single-node SQLite.
5. **Cascade deletes** — Deleting an article cascades to versions, tags, feedback, and views. Deleting a user cascades to API keys. API key deletion sets `created_via_api_key_id` to null on articles.
6. **UTC timestamps** — All `DateTime` values stored and transmitted in UTC.
