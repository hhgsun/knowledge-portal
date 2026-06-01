# Knowledge Portal — AI-First Development Guide

## Architecture

Split monorepo: `backend/` (ASP.NET Core Web API) + `frontend/` (React SPA).

| Layer | Stack |
|-------|-------|
| Backend | ASP.NET Core (.NET 10), EF Core, SQLite |
| Auth | JWT Bearer + API Key (kp_ prefix) |
| Frontend | React 19, Vite, React Router v7, Tailwind CSS v4 |
| Editor | TipTap (ProseMirror) |

## Conventions

### Backend (`backend/`)

- **Language**: C# 13, .NET 10, nullable enabled
- **Pattern**: Controllers → EF Core DbContext → SQLite
- **Naming**: PascalCase for C# code, snake_case for DB columns (configured in `AppDbContext.OnModelCreating`)
- **Auth**: `[Authorize]` attribute on controllers, `[AllowAnonymous]` for public endpoints
- **RBAC**: `RequirePermission` attribute with permission strings (e.g. `"articles:create"`)
- **API prefix**: All routes under `/api/` (e.g. `/api/articles`, `/api/auth/login`)
- **Entities**: `backend/Models/Entities/` — 9 models: User, Article, ArticleVersion, ArticleView, Tag, ArticleTag, ArticleFeedback, ApiKey, SearchQuery
- **Seed data**: `DbInitializer.Initialize()` — admin user + 10 default tags
- **Port**: 5174

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
├── Auth/                 # JwtService, RbacService, ApiKeyMiddleware
├── Data/                 # AppDbContext, DbInitializer
├── Models/Entities/      # EF Core entity classes
├── Migrations/           # EF Core migrations
├── Program.cs            # App configuration & DI
└── appsettings.json      # Connection strings, JWT config

frontend/
├── src/contexts/         # AuthContext (JWT auth state)
├── src/hooks/            # useApi (fetch wrapper)
├── src/components/       # layout/ + editor/
├── src/pages/            # 12 page components
├── src/lib/utils.ts      # cn() helper
├── src/App.tsx           # Routes
└── vite.config.ts        # Proxy config
```

## Default Credentials

- **Email**: `admin@knowledge.local`
- **Password**: `admin123`

## Commands

| Task | Command |
|------|---------|
| Run backend | `cd backend && dotnet run` |
| Run frontend | `cd frontend && npm run dev` |
| Apply migrations | `cd backend && dotnet ef database update` |
| New migration | `cd backend && dotnet ef migrations add <Name>` |
| Build frontend | `cd frontend && npm run build` |
| Build backend | `cd backend && dotnet build` |

## Rules for AI Agents

- Do NOT add Next.js, SSR, or server components — this is a pure SPA + REST API architecture
- Do NOT use ASP.NET Identity — auth is custom JWT via `JwtService`
- Do NOT add Redux, Zustand, or other state managers — use React Context
- Do NOT add component libraries (MUI, Chakra, etc.) — use Tailwind CSS + lucide-react + sonner
- Keep pages in `frontend/src/pages/` as flat files (no nested folders)
- Use `useApi` hook for all authenticated API calls
- Use `toast` from `sonner` for all user feedback (success/error)
- Use `[Authorize]` + `RequirePermission` for backend RBAC
- Use `RoleRoute` wrapper for frontend role-restricted pages
- DB column names are snake_case; C# properties are PascalCase
- Viewers CAN create articles (status limited to draft/pending)
- Password minimum length is 8 characters everywhere
