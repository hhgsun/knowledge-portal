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
| Admin email | `admin@finagotech.com.tr` |
| Admin password | `1q2w3E*/` |
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

`ApiKeyMiddleware` intercepts `X-API-Key: kp_*` header before standard JWT auth, sets `ClaimsPrincipal` with an `api-key` source discriminator. Session-only endpoints reject API key auth via `User.GetSource() == "api-key"` checks.

## MCP Server (Model Context Protocol)

The Knowledge Portal includes a **JSON-RPC 2.0 compliant MCP server** at `POST /mcp` for programmatic access to knowledge base tools.

### Authentication

**No OAuth.** MCP uses only simple authentication:

| Method | Header | Example |
|--------|--------|---------|
| API Key | `X-API-Key: kp_*` | `X-API-Key: kp_7944228bfb1ff77f7dfa40edd4025074` |
| JWT Bearer | `Authorization: Bearer <token>` | `Authorization: Bearer eyJhbGc...` |

Both authentication methods are **required** for all MCP requests (no anonymous access).

### Protocol & Methods

```
POST http://localhost:5174/mcp
Content-Type: application/json
X-API-Key: kp_<your-api-key>

{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "<method>",
  "params": { ... }
}
```

**Standard Methods** (JSON-RPC 2.0 spec):

| Method | Purpose | Response |
|--------|---------|----------|
| `initialize` | Get server capabilities and protocol version | `{ protocolVersion, capabilities, serverInfo }` |
| `tools/list` | Discover available tools with JSON schemas | `{ tools: [...] }` |
| `tools/call` | Execute a tool with parameters | `{ result: "..." }` or `{ error: {...} }` |

### Available Tools

1. **searchArticles** — Full-text search articles  
   - Params: `query` (required), `limit` (1-50), `tags`, `authors`, `contentType`, `includeContent`
   - Returns: JSON array of matching articles

2. **listArticles** — List published articles with pagination  
   - Params: `page`, `limit`, `contentType`, `tags`
   - Returns: JSON object `{ articles: [...], total: number }`

3. **getArticle** — Get article details by ID or slug  
   - Params: `idOrSlug` (required)
   - Returns: Article JSON with full content (TipTap format) + metadata

4. **listTags** — List all tags in the portal  
   - No params required
   - Returns: JSON array of tags

5. **getPortalStats** — Get portal statistics  
   - No params required
   - Returns: `{ totalArticles, totalAuthors, totalTags, recentActivity }`

### Example: VSCode MCP Integration

To use this MCP server in VSCode (e.g., with Claude extension):

```json
{
  "mcpServers": {
    "knowledge-portal": {
      "command": "curl",
      "args": [
        "-X", "POST",
        "http://localhost:5174/mcp",
        "-H", "X-API-Key: kp_<your-api-key>"
      ]
    }
  }
}
```

**Note**: Replace `kp_<your-api-key>` with an actual API key from `/api/keys`. Create one via the admin panel or API.

### Stateless & Secure

- **No OAuth**: API Key (BCrypt hashed) or JWT Bearer auth only
- **Stateless**: Each request is independent; no session state maintained
- **Published articles only**: All tools filter to `status: "published"`
- **RBAC-free**: Tools don't enforce permission checks beyond authentication
- **Rate limiting**: None applied (unlike `/api/search` which has rate limits)
