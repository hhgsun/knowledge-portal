# AGENTS.md — Knowledge Portal

This file defines specialized agent modes for AI-assisted development on this project.

---

## Backend Agent

**Focus:** API routes, database, authentication, search pipeline, worker.

**Key files:**
- `src/app/api/**` — REST API endpoints
- `src/lib/db/schema.ts` — Database schema (single source of truth)
- `src/lib/auth/` — NextAuth config, RBAC, API key validation
- `src/lib/search/` — Hybrid search, embeddings, Qdrant, RAG
- `src/workers/index.ts` — Background indexer
- `drizzle.config.ts` — Migration config

**Rules:**
- All DB queries must be `await`ed (libsql is async)
- Validate all inputs with Zod schemas
- Check auth/permissions at the top of every handler
- Return consistent JSON: `{ data }` or `{ error: string }`
- Never import Node.js built-ins in middleware

**Common tasks:**
- Add new API endpoint: create `src/app/api/[route]/route.ts`
- Add DB column: update `schema.ts` → `npm run db:generate` → `npm run db:migrate`
- Add new permission: update `rolePermissions` in `src/lib/auth/rbac.ts`

---

## Frontend Agent

**Focus:** Pages, components, styling, client-side interactions.

**Key files:**
- `src/app/*/page.tsx` — Page components
- `src/components/` — Reusable components
- `src/app/globals.css` — Tailwind CSS v4 + custom properties
- `src/app/layout.tsx` — Root layout (sidebar, header)

**Rules:**
- Prefer server components; use `"use client"` only when needed
- Use `cn()` from `src/lib/utils.ts` for conditional classes
- Icons: `lucide-react` only
- TipTap editor: dynamic import with `ssr: false`
- No CSS modules, styled-components, or additional CSS frameworks
- Tailwind CSS v4 utility classes only

**Common tasks:**
- Add new page: create `src/app/[route]/page.tsx`
- Add new component: create `src/components/[domain]/[name].tsx`
- Fetch data client-side: use `fetch('/api/...')` with proper loading/error states

---

## Search/AI Agent

**Focus:** Semantic search, embedding pipeline, RAG, Qdrant, Ollama integration.

**Key files:**
- `src/lib/search/chunker.ts` — Document chunking (TipTap JSON → text segments)
- `src/lib/search/embeddings.ts` — Ollama embedding generation
- `src/lib/search/qdrant.ts` — Qdrant client (collection, upsert, search, delete)
- `src/lib/search/hybrid.ts` — FTS5 + semantic + RRF fusion
- `src/lib/search/rag.ts` — RAG pipeline (retrieve → prompt → generate)
- `src/workers/index.ts` — Background indexer

**Context:**
- Embedding model: `nomic-embed-text` (768 dimensions)
- LLM model: `llama3`
- Qdrant collection: `knowledge_articles`
- Hybrid search: Reciprocal Rank Fusion (k=60)
- FTS5: SQLite full-text search with BM25 scoring

**Rules:**
- Chunks must include articleId in Qdrant payload
- Always deduplicate search results by articleId
- RAG must cite sources using `[Source N]` notation
- Worker must update `indexedAt` after successful indexing

---

## DevOps Agent

**Focus:** Docker, deployment, database operations, infrastructure.

**Key files:**
- `docker-compose.yml` — Full stack (web, worker, qdrant, ollama)
- `Dockerfile` — Web app image
- `Dockerfile.worker` — Worker image
- `drizzle.config.ts` — DB migration config
- `.env.example` — Environment variable template

**Commands:**
```bash
npm run dev          # Dev server
npm run build        # Production build (catches type errors)
npm run db:generate  # Generate migration
npm run db:migrate   # Apply migrations
npm run db:seed      # Seed database
npm run worker       # Start background worker
docker compose up -d # Full stack deployment
```

**Rules:**
- SQLite file goes in `./data/` directory
- Never use `--force` with migrations
- Worker and web share the same database volume in Docker
- Qdrant and Ollama are optional (basic features work without them)
