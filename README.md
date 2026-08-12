# Knowledge Portal

Enterprise knowledge management system with WYSIWYG editing, RBAC, content versioning, and analytics.

> **Full documentation**: See [`AGENTS.md`](AGENTS.md) — the single source of truth for this project.
> Detailed specs are in `specs/`.

## Tech Stack

| Layer | Technology |
|-------|-----------|
| **Backend** | ASP.NET Core Web API (.NET 10), EF Core, PostgreSQL + pgvector |
| **Frontend** | React 19, Vite, TypeScript, Tailwind CSS v4 |
| **Auth** | JWT Bearer + API Key (kp_ prefix) |
| **Editor** | Milkdown Crepe (ProseMirror, CommonMark/GFM) |

## Quick Start

### Backend

```bash
cd backend
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

Frontend starts at `http://localhost:5173`

### Default Login

- **Email**: `admin@finagotech.com.tr`
- **Password**: `1q2w3E*/`

## Commands

| Task | Command |
|------|---------|
| Run backend | `cd backend && dotnet run` |
| Run frontend | `cd frontend && npm run dev` |
| Run tests | `cd backend/Tests && dotnet test` |
| Apply migrations | `cd backend && dotnet ef database update` |
| Build frontend | `cd frontend && npm run build` |

## Key Features

- Markdown-first WYSIWYG editor (Milkdown Crepe) with headings, code blocks, tables, task lists
- RBAC: Admin / Editor / Viewer with 12 granular permissions
- Article versioning with diff comparison
- Full-text search with `@tag` syntax
- Analytics: views, searches, content gaps, feedback
- Dual auth: JWT tokens + API keys for integrations
- Rate limiting on auth and search endpoints
