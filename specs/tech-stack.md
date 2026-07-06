# Technology Stack

> **⚠️ Bu dosya `AGENTS.md`'ye tabidir.** Çelişki durumunda `AGENTS.md` geçerlidir.
> Yeni paket eklendiğinde/kaldırıldığında bu dosya güncellenmelidir.

## Runtime & Language

| Layer | Technology | Version |
|-------|-----------|---------|
| Backend runtime | .NET | 10.0 |
| Backend language | C# | 13 (nullable enabled, implicit usings) |
| Frontend runtime | Node.js | Latest LTS |
| Frontend language | TypeScript | ~6.0 (strict mode, ES2023 target) |

## Frameworks

| Component | Framework | Version | Notes |
|-----------|-----------|---------|-------|
| Web API | ASP.NET Core | 10.0 | Controllers-based, not minimal API |
| ORM | Entity Framework Core | 10.0.8 | Code-first migrations |
| SPA | React | 19.2 | StrictMode enabled |
| Routing | React Router | 7.15 | v7, package: `react-router-dom` with `<Outlet>` pattern |
| Bundler | Vite | 8.0 | Dev server on port 5173, proxy `/api` → 5174 |
| CSS | Tailwind CSS | 4.3 | Utility-first, `@theme inline`, v4 Vite plugin |
| Rich-text editor | TipTap (ProseMirror) | 3.23 | JSON document model |

## Key Libraries

### Backend

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 10.0.8 | JWT token validation |
| `Microsoft.Identity.Web` | 3.8.3 | Azure AD token validation helper |
| `Microsoft.EntityFrameworkCore.Sqlite` | 10.0.8 | SQLite database provider |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.8 | Migration CLI tooling |
| `BCrypt.Net-Next` | 4.2.0 | Password hashing (cost 12) and API key verification |
| `OllamaSharp` | 5.4.25 | Ollama client (implements IChatClient + IEmbeddingGenerator from Microsoft.Extensions.AI) |
| `System.Numerics.Tensors` | 10.0.9 | SIMD-accelerated TensorPrimitives for cosine similarity |
| `UglyToad.PdfPig` | 1.7.0-custom-5 | PDF text extraction for attachment indexing |
| `DocumentFormat.OpenXml` | 3.3.0 | DOCX text extraction for attachment indexing |

### Frontend

| Package | Version | Purpose |
|---------|---------|---------|
| `react`, `react-dom` | 19.2.6 | UI framework |
| `react-router` | 7.15.1 | Client-side routing |
| `@tiptap/react`, `@tiptap/starter-kit` | 3.23.6 | Rich-text editor core |
| TipTap extensions | 3.23.x | code-block-lowlight, heading, highlight, image, link, placeholder, table (row/cell/header), task-list, task-item, text-align, underline |
| `lowlight` | 3.3.0 | Syntax highlighting in code blocks |
| `lucide-react` | 1.16.0 | Icon library (sole icon source) |
| `@azure/msal-browser` | 5.15.0 | MSAL.js v5 browser library for Azure AD auth (redirect-bridge popup pattern) |
| `@azure/msal-react` | 5.5.0 | React hooks wrapper for MSAL.js v5 |
| `clsx` | 2.1.1 | Conditional class names |
| `tailwind-merge` | 3.6.0 | Tailwind class deduplication |

### Dev Tooling

| Tool | Version |
|------|---------|
| `@vitejs/plugin-react` | 6.0 |
| `@tailwindcss/vite` | 4.3 |
| `eslint` | 10.3 |
| `typescript-eslint` | — |
| `eslint-plugin-react-hooks` | — |
| `eslint-plugin-react-refresh` | — |

## Database

| Property | Value |
|----------|-------|
| Engine | SQLite |
| File location | `../data/knowledge.db` (relative to backend project root) |
| ORM strategy | Code-first with EF Core migrations |
| Column naming | `snake_case` (configured in `OnModelCreating`) |
| ID generation | 21-character truncated GUID (hex, lowercase) |

## External APIs & Services

None. The application is fully self-contained with no external service dependencies. Search is local SQL LIKE-based (semantic/RAG endpoints are placeholder stubs).

## Testing Framework

| Component | Technology | Purpose |
|-----------|-----------|---------|
| Test runner | xUnit | Unit + integration tests |
| Integration testing | `Microsoft.AspNetCore.Mvc.Testing` | WebApplicationFactory-based API tests |
| Database (tests) | SQLite temp file | Isolated per test class, auto-migrated |
| Test project | `backend.Tests/` | 46 tests (unit + integration) |

Run tests: `cd backend.Tests && dotnet test`

## Deployment Target

Not formally defined. Current setup is local development only:
- Backend: `dotnet run` on port 5174
- Frontend: `vite dev` on port 5173 with API proxy
- Database: File-based SQLite at `../data/knowledge.db`
