# Contributing

## Development Setup

1. Clone the repository
2. Copy `.env.example` to `.env.local` and fill in values
3. Install dependencies: `npm install`
4. Run migrations: `npx drizzle-kit migrate`
5. Seed database: `npm run db:seed`
6. Start dev server: `npm run dev`

## Default Admin Credentials
- Email: `admin@knowledge.local`
- Password: `admin123`

## Code Conventions

### TypeScript
- Strict mode enabled
- Use interfaces for object shapes, types for unions/intersections
- Prefer `const` over `let`

### Database
- Schema defined in `src/lib/db/schema.ts`
- All queries are async (libsql driver)
- Use Drizzle query builder, not raw SQL
- After schema changes: `npm run db:generate` → `npm run db:migrate`

### API Routes
- Validate all input with Zod schemas
- Return consistent JSON: `{ data }` or `{ error: string }`
- Check auth/permissions at the top of each handler

### Components
- Use `cn()` for conditional classes
- Prefer server components; use `"use client"` only when needed
- Dynamic import for heavy client components (e.g., TipTap editor)

### Styling
- Tailwind CSS v4 utility classes
- No CSS modules or styled-components
- Design tokens as CSS custom properties in `globals.css`

## Git Workflow

- Feature branches off `main`
- Conventional commits: `feat:`, `fix:`, `refactor:`, `docs:`
- Run `npm run build` before pushing (catches type errors)

## Architecture Decisions

| Decision | Rationale |
|----------|-----------|
| SQLite over PostgreSQL | Single-file DB, no server needed, WAL for concurrency |
| libsql over better-sqlite3 | Pure JS, no native compilation required |
| JWT over DB sessions | Edge-compatible middleware, stateless scaling |
| Ollama over OpenAI | Self-hosted, no data leaves the network |
| TipTap over Slate/Draft | ProseMirror-based, extensible, good schema support |
| Drizzle over Prisma | Lighter, SQL-like API, better Edge support |
