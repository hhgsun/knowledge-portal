# Copilot Instructions — Knowledge Portal

## Project Overview
Enterprise knowledge management system with AI-powered semantic search, WYSIWYG editing, RBAC, and analytics.

## Tech Stack
- Next.js 16 (App Router, Turbopack)
- React 19, TypeScript strict
- SQLite via @libsql/client (pure JS, no native bindings)
- Drizzle ORM (async only — all queries return Promises)
- NextAuth v5 beta (JWT strategy, credentials provider)
- Qdrant (vector DB), Ollama (LLM/embeddings)
- TipTap (WYSIWYG editor)
- Tailwind CSS v4, lucide-react icons

## Critical Patterns

### Database Queries (Drizzle + libsql)
All Drizzle queries are **async**. Always `await` and use array destructuring for single results:
```ts
// ✅ Correct
const [user] = await db.select().from(users).where(eq(users.id, id)).limit(1);

// ❌ Wrong — .get() doesn't exist, queries are not sync
const user = db.select().from(users).where(eq(users.id, id)).get();
```

### Middleware (Edge Runtime)
- `src/middleware.ts` runs in Edge — cannot import `fs`, `path`, or DB modules
- Use `getToken` from `next-auth/jwt` with explicit `secret: process.env.NEXTAUTH_SECRET`
- Cannot use `auth()` from NextAuth (requires DB access)

### Authentication
- NextAuth v5 with JWT strategy (no DB sessions)
- Roles: `admin`, `editor`, `viewer`
- RBAC helpers in `src/lib/auth/rbac.ts`: `requireAuth()`, `requirePermission()`, `hasPermission()`
- JWT contains `id` and `role` fields (extended in `src/types/next-auth.d.ts`)

### API Routes
- All in `src/app/api/` using Next.js Route Handlers
- Always validate input with Zod
- Use `NextResponse.json()` for responses
- Check permissions via RBAC helpers

### Components
- Use `cn()` from `src/lib/utils.ts` for conditional classNames
- Icons from `lucide-react`
- Editor is dynamically imported (`next/dynamic` with `ssr: false`)

### File Structure Convention
```
src/app/[route]/page.tsx      → Page component
src/app/api/[route]/route.ts  → API handler
src/components/[domain]/      → Domain components
src/lib/[module]/             → Shared utilities
```

## Environment Variables
- `DATABASE_URL` — SQLite path (relative, e.g. `./data/knowledge.db`)
- `NEXTAUTH_SECRET` — JWT signing secret (required)
- `NEXTAUTH_URL` — App base URL
- `QDRANT_URL` — Vector DB endpoint
- `OLLAMA_URL` — LLM server endpoint

## Commands
```bash
npm run dev          # Start dev server (Turbopack)
npm run build        # Production build
npm run db:generate  # Generate migration from schema changes
npm run db:migrate   # Apply migrations
npm run db:seed      # Seed initial data
npm run worker       # Background indexing worker
```

## Do NOT
- Use `better-sqlite3` (requires native compilation)
- Import Node.js modules (`fs`, `path`, `crypto`) in middleware
- Use synchronous DB patterns
- Add `"use server"` to API route files
- Use `auth()` in middleware (not Edge-compatible)
