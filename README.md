# Knowledge Portal

Enterprise knowledge management system with WYSIWYG editing, RBAC, content versioning, and analytics.

## Tech Stack

| Layer | Technology |
|-------|-----------|
| **Backend** | ASP.NET Core Web API (.NET 10) |
| **ORM** | Entity Framework Core + SQLite |
| **Auth** | JWT Bearer + API Key (dual auth) |
| **Frontend** | React 19 + Vite + TypeScript |
| **Routing** | React Router v7 |
| **Editor** | TipTap (ProseMirror-based WYSIWYG) |
| **CSS** | Tailwind CSS v4 |
| **Icons** | Lucide React |

## Quick Start

### Backend

```bash
cd backend

# Apply migrations & seed database
dotnet ef database update
dotnet run
```

Backend starts at `http://localhost:5174`

### Frontend

```bash
cd frontend
npm install
npm run dev
```

Frontend starts at `http://localhost:5173` — login with:
- **Email**: `admin@knowledge.local`
- **Password**: `admin123`

## Project Structure

```
backend/                        # ASP.NET Core Web API
├── Controllers/                # 10 API controllers
│   ├── AuthController          # login, register, me
│   ├── ArticlesController      # CRUD with slug, versioning, view tracking
│   ├── ArticleFeedbackController
│   ├── ArticleVersionsController
│   ├── SearchController        # full-text search with @tag syntax
│   ├── TagsController          # CRUD with article count
│   ├── ApiKeysController       # kp_ prefixed API keys
│   ├── AdminUsersController    # user management (admin only)
│   ├── AnalyticsController     # stats, top searches, top articles
│   └── DashboardController     # home page stats
├── Auth/                       # JWT service, RBAC, API key middleware
├── Data/                       # AppDbContext, DbInitializer (seed)
├── Models/Entities/            # EF Core entities (9 models)
├── Migrations/                 # EF Core migrations
└── Program.cs                  # App configuration & pipeline

frontend/                       # React SPA (Vite)
├── src/
│   ├── contexts/AuthContext     # JWT auth state, login/logout/register
│   ├── hooks/useApi             # fetchWithAuth, auto-logout on 401
│   ├── components/
│   │   ├── layout/             # AppShell, Sidebar, Header
│   │   └── editor/             # TipTap editor, renderer, tag selector
│   ├── pages/                  # 12 page components
│   └── App.tsx                 # Routes + ProtectedRoute wrapper
└── vite.config.ts              # API proxy → localhost:5174
```

## API Endpoints

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| POST | `/api/auth/login` | — | Login, returns JWT |
| POST | `/api/auth/register` | — | Register new user |
| GET | `/api/auth/me` | JWT | Current user info |
| GET | `/api/articles` | JWT | List articles |
| POST | `/api/articles` | JWT | Create article |
| GET | `/api/articles/{id}` | JWT | Get article by ID |
| PUT | `/api/articles/{id}` | JWT | Update article |
| DELETE | `/api/articles/{id}` | JWT | Delete article |
| GET | `/api/articles/{id}/versions` | JWT | Version history |
| POST | `/api/articles/{id}/feedback` | JWT | Submit feedback |
| GET | `/api/search` | JWT | Search articles |
| GET | `/api/tags` | JWT | List tags |
| GET | `/api/keys` | JWT | List API keys |
| POST | `/api/keys` | JWT | Create API key |
| GET | `/api/analytics` | JWT | Analytics data |
| GET | `/api/dashboard` | JWT | Dashboard stats |
| GET | `/api/admin/users` | JWT (admin) | User management |

## Available Scripts

### Backend

| Command | Description |
|---------|-------------|
| `dotnet run` | Start API server |
| `dotnet ef database update` | Apply migrations |
| `dotnet ef migrations add <Name>` | Create new migration |
| `dotnet build` | Build project |

### Frontend

| Command | Description |
|---------|-------------|
| `npm run dev` | Start dev server (port 5173) |
| `npm run build` | Production build |
| `npm run preview` | Preview production build |
| `npm run lint` | ESLint check |

## Features

- **WYSIWYG Editor** — TipTap with headings, code blocks, tables, task lists, highlights, images
- **RBAC** — Admin / Editor / Viewer roles with granular permissions
- **Version History** — Full audit trail of article changes
- **Analytics** — Views, search queries, content gaps, feedback tracking
- **Dual Auth** — JWT Bearer tokens + API key (`kp_` prefix) for integrations
- **Full-Text Search** — SQL LIKE search with `@tag` syntax

## Configuration

### Backend (`backend/appsettings.json`)

| Key | Default | Description |
|-----|---------|-------------|
| `ConnectionStrings:DefaultConnection` | `Data Source=../data/knowledge2.db` | SQLite database path |
| `Jwt:Secret` | *(configured)* | JWT signing secret |
| `Jwt:Issuer` | `KnowledgePortal` | JWT issuer |
| `Jwt:ExpirationMinutes` | `1440` | Token expiration (24h) |

### Frontend (`frontend/vite.config.ts`)

API requests (`/api/*`) are proxied to `http://localhost:5174` in development.
