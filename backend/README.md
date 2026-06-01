# Knowledge Portal — Backend API

ASP.NET Core Web API powering the Knowledge Portal platform.

## Tech Stack

| Component | Version |
|-----------|---------|
| .NET | 10.0 |
| C# | 13 |
| EF Core | 10.0.8 |
| SQLite | via EF Core Sqlite provider |
| Auth | JWT Bearer (HMAC-SHA256) + API Key (`kp_` prefix) |
| Password Hashing | BCrypt (cost factor 12) |

## Quick Start

```bash
cd backend
dotnet build KnowledgePortal.Api.csproj
dotnet run --project KnowledgePortal.Api.csproj
```

Server starts on **http://localhost:5174**. On first run the database is created at `../data/knowledge.db` with seed data.

### Seed Data

| Item | Value |
|------|-------|
| Admin email | `admin@knowledge.local` |
| Admin password | `admin123` |
| Default tags | 10 tags (getting-started, api, deployment, etc.) |

## Project Structure

```
backend/
├── Program.cs                  # App bootstrap, DI, middleware pipeline
├── appsettings.json            # Connection string, JWT config
├── Auth/
│   ├── JwtService.cs           # Token generation (HMAC-SHA256, 24h expiry)
│   ├── ApiKeyMiddleware.cs     # kp_ token verification middleware
│   ├── RbacService.cs          # Static role→permission matrix
│   ├── RequirePermissionAttribute.cs  # [RequirePermission] filter
│   └── ClaimsPrincipalExtensions.cs   # GetUserId(), GetRole(), etc.
├── Controllers/
│   ├── AuthController.cs       # POST login, register; GET me
│   ├── ArticlesController.cs   # CRUD articles + versioning
│   ├── TagsController.cs       # CRUD tags
│   ├── ArticleVersionsController.cs  # GET article versions
│   ├── ArticleFeedbackController.cs  # POST/GET feedback
│   ├── SearchController.cs     # Full-text, tag, RAG search
│   ├── DashboardController.cs  # Dashboard summary stats
│   ├── AnalyticsController.cs  # Detailed analytics
│   ├── AdminUsersController.cs # User management (admin only)
│   └── ApiKeysController.cs    # API key management
├── Data/
│   ├── AppDbContext.cs         # EF Core context, snake_case mapping
│   └── DbInitializer.cs       # Migrations + seed data
├── Models/
│   ├── Entities/               # 9 EF Core entities
│   ├── DTOs/
│   └── Requests/
├── Migrations/                 # EF Core migrations
└── Services/
```

## API Endpoints

All routes are prefixed with `/api/`.

### Authentication

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| POST | `/api/auth/login` | None | Login, returns JWT |
| POST | `/api/auth/register` | None | Register new user (viewer role) |
| GET | `/api/auth/me` | JWT | Current user profile |

### Articles

| Method | Route | Auth | Permission | Description |
|--------|-------|------|------------|-------------|
| GET | `/api/articles` | JWT/Key | — | List articles (paginated) |
| POST | `/api/articles` | JWT/Key | `articles:create` | Create article |
| GET | `/api/articles/{idOrSlug}` | JWT/Key | — | Get article detail |
| PUT | `/api/articles/{id}` | JWT/Key | Owner or `articles:edit_any` | Update article |
| DELETE | `/api/articles/{id}` | JWT/Key | Owner or `articles:delete_any` | Delete article |

### Tags

| Method | Route | Auth | Permission | Description |
|--------|-------|------|------------|-------------|
| GET | `/api/tags` | JWT/Key | — | List all tags |
| POST | `/api/tags` | JWT/Key | `tags:manage` | Create tag |
| DELETE | `/api/tags?id={id}` | JWT/Key | `tags:manage` | Delete tag |

### Versions & Feedback

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/articles/{id}/versions` | JWT | List versions |
| GET | `/api/articles/{id}/versions/{vid}` | JWT | Get version detail |
| POST | `/api/articles/{id}/feedback` | JWT | Submit feedback |
| GET | `/api/articles/{id}/feedback` | JWT | Get feedback stats |

### Search

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| GET | `/api/search?q=...&type=...&limit=...` | JWT/Key | Search articles |

Search types: `fulltext` (default), `semantic`, `hybrid`, `rag`. Prefix query with `@tag-slug` for tag-based search.

### Dashboard & Analytics

| Method | Route | Auth | Permission | Description |
|--------|-------|------|------------|-------------|
| GET | `/api/dashboard` | JWT | — | Summary stats |
| GET | `/api/analytics` | JWT (session only) | `analytics:view` | Detailed analytics |

### Admin

| Method | Route | Auth | Permission | Description |
|--------|-------|------|------------|-------------|
| GET | `/api/admin/users` | JWT (session only) | `users:manage` | List users |
| POST | `/api/admin/users` | JWT (session only) | `users:manage` | Create user |
| PUT | `/api/admin/users` | JWT (session only) | `users:manage` | Update user |
| DELETE | `/api/admin/users?id={id}` | JWT (session only) | `users:manage` | Delete user |
| GET | `/api/keys` | JWT (session only) | `api_keys:manage` | List API keys |
| POST | `/api/keys` | JWT (session only) | `api_keys:manage` | Create API key |
| DELETE | `/api/keys?id={id}` | JWT (session only) | `api_keys:manage` | Delete API key |

## RBAC Permission Matrix

| Permission | admin | editor | viewer |
|-----------|:-----:|:------:|:------:|
| `articles:read` | ✓ | ✓ | ✓ |
| `articles:create` | ✓ | ✓ | ✓ |
| `articles:edit_own` | ✓ | ✓ | ✓ |
| `articles:edit_any` | ✓ | — | — |
| `articles:delete_own` | ✓ | ✓ | ✓ |
| `articles:delete_any` | ✓ | — | — |
| `articles:publish` | ✓ | ✓ | — |
| `articles:archive` | ✓ | ✓ | — |
| `articles:approve` | ✓ | ✓ | — |
| `tags:manage` | ✓ | ✓ | — |
| `users:manage` | ✓ | — | — |
| `analytics:view` | ✓ | ✓ | — |
| `api_keys:manage` | ✓ | — | — |

> **Note**: Viewers can create articles but their status is restricted to `draft` or `pending` only. Viewers can only edit/delete their own articles.

## Database

SQLite database at `../data/knowledge.db`. Column names use **snake_case** (configured in `AppDbContext.OnModelCreating`). C# properties use **PascalCase**.

### Entity Model

9 entities: `User`, `Article`, `ArticleVersion`, `ArticleView`, `Tag`, `ArticleTag`, `ArticleFeedback`, `ApiKey`, `SearchQuery`.

### Migrations

```bash
# Apply existing migrations
dotnet ef database update --project KnowledgePortal.Api.csproj

# Create a new migration
dotnet ef migrations add <Name> --project KnowledgePortal.Api.csproj
```

## Configuration

Key settings in `appsettings.json`:

| Key | Description | Default |
|-----|-------------|---------|
| `ConnectionStrings:DefaultConnection` | SQLite path | `Data Source=../data/knowledge.db` |
| `Jwt:Secret` | HMAC signing key (≥32 chars) | Dev key (change in production) |
| `Jwt:Issuer` | Token issuer | `KnowledgePortal` |
| `Jwt:Audience` | Token audience | `KnowledgePortal` |
| `Jwt:ExpirationInMinutes` | Token lifetime | `1440` (24 hours) |

## Middleware Pipeline

```
Request → CORS → ApiKeyMiddleware → Authentication → Authorization → Controllers
```

`ApiKeyMiddleware` intercepts `Bearer kp_*` tokens before standard JWT auth, sets `ClaimsPrincipal` with an `api-key` source discriminator. Session-only endpoints reject API key auth via `User.GetSource() == "api-key"` checks.
