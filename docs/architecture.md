# Architecture — Knowledge Portal

## System Overview

```
┌────────────────────────────────────────────────────────────────────┐
│                        Next.js 16 App                              │
│                                                                    │
│  ┌──────────┐  ┌──────────────┐  ┌────────────┐  ┌────────────┐  │
│  │  Pages   │  │  API Routes  │  │ Middleware │  │ Components │  │
│  │(App Router)│ │(Route Handler)│  │  (Edge)    │  │  (React)   │  │
│  └─────┬────┘  └──────┬───────┘  └─────┬──────┘  └────────────┘  │
│        │               │                │                          │
│        ▼               ▼                ▼                          │
│  ┌─────────────────────────────────────────────┐                  │
│  │              src/lib/ (Shared)               │                  │
│  │  ┌──────┐  ┌──────┐  ┌────────┐  ┌──────┐  │                  │
│  │  │  db  │  │ auth │  │ search │  │utils │  │                  │
│  │  └──┬───┘  └──┬───┘  └───┬────┘  └──────┘  │                  │
│  └─────┼─────────┼──────────┼──────────────────┘                  │
└────────┼─────────┼──────────┼──────────────────────────────────────┘
         │         │          │
         ▼         │          ▼
┌────────────┐     │   ┌────────────┐     ┌──────────────┐
│   SQLite   │     │   │   Qdrant   │     │    Ollama    │
│ (@libsql)  │     │   │ (Vectors)  │     │ (LLM + Emb) │
└────────────┘     │   └────────────┘     └──────────────┘
                   │
         ┌─────────────────┐
         │  NextAuth v5    │
         │  (JWT Strategy) │
         └─────────────────┘

┌────────────────────────────────────────┐
│        Background Worker (tsx)         │
│  • Embedding indexer (every 5 min)     │
│  • Staleness checker (daily)           │
│  • Reads DB + writes to Qdrant         │
└────────────────────────────────────────┘
```

## Data Flow

### Article Creation
```
Client → POST /api/articles → Zod validation → Auth check → DB insert
                                                            → Version 1 created
                                                            → Response to client
```

### Article Indexing (Background Worker)
```
Worker polls DB (every 5 min)
  → Finds articles where updatedAt > indexedAt
  → Extracts plain text from TipTap JSON (chunker.ts)
  → Generates embeddings via Ollama (nomic-embed-text)
  → Upserts vectors to Qdrant collection
  → Updates indexedAt timestamp in DB
```

### Search Pipeline
```
User query → /api/search?q=...&type=hybrid
  ├─ FTS5 branch: SQLite full-text search (BM25 scoring)
  ├─ Semantic branch: Ollama embedding → Qdrant similarity search
  └─ Hybrid: Reciprocal Rank Fusion (RRF) merges both result sets
     → Fetch full articles from DB → Return ranked results
```

### RAG (Retrieval-Augmented Generation)
```
User query → /api/search?q=...&type=rag
  → Generate query embedding (Ollama)
  → Retrieve top-5 chunks from Qdrant
  → Build context prompt with sources
  → Generate answer via Ollama (llama3)
  → Return answer + cited sources
```

## Authentication Flow

```
Login → NextAuth credentials provider → bcrypt verify
  → JWT issued (contains: id, role, name, email)
  → Stored in httpOnly cookie

Request → Middleware (Edge) → getToken() validates JWT
  → Redirect to /login if missing/expired
  → Pass through if valid

API Route → auth() → Full session object
  → hasPermission(role, permission) → Allow/Deny
```

## Database Schema (SQLite)

| Table | Purpose |
|-------|---------|
| `users` | User accounts (id, name, email, passwordHash, role) |
| `sessions` | Auth sessions (unused — JWT strategy) |
| `api_keys` | External API authentication (hashed keys) |
| `tags` | Tag taxonomy (name, slug) |
| `articles` | Main content (TipTap JSON, metadata, status) |
| `article_versions` | Version history (full content snapshots) |
| `article_tags` | Many-to-many article↔tag relation |
| `article_feedback` | User feedback (helpful/not helpful + comments) |
| `article_views` | View tracking for analytics |
| `search_queries` | Search analytics (query, type, response time) |

## RBAC Permission Matrix

| Permission | Admin | Editor | Viewer |
|-----------|-------|--------|--------|
| `articles:create` | ✅ | ✅ | ❌ |
| `articles:edit_own` | ✅ | ✅ | ❌ |
| `articles:edit_any` | ✅ | ❌ | ❌ |
| `articles:delete_own` | ✅ | ✅ | ❌ |
| `articles:delete_any` | ✅ | ❌ | ❌ |
| `articles:publish` | ✅ | ✅ | ❌ |
| `articles:archive` | ✅ | ✅ | ❌ |
| `tags:manage` | ✅ | ✅ | ❌ |
| `users:manage` | ✅ | ❌ | ❌ |
| `analytics:view` | ✅ | ✅ | ❌ |
| `api_keys:manage` | ✅ | ❌ | ❌ |

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| SQLite over PostgreSQL | Single-file DB, zero-config, WAL for concurrency |
| @libsql over better-sqlite3 | Pure JS — no native compilation, works everywhere |
| JWT over DB sessions | Edge-compatible middleware, stateless scaling |
| Ollama over OpenAI | Self-hosted, data stays on-premise |
| Hybrid search (FTS5 + Qdrant) | Best of keyword + semantic matching via RRF |
| TipTap JSON storage | Structured content, no HTML parsing needed |
| Background worker (separate process) | Keeps web server fast, heavy embedding work offloaded |
| Drizzle over Prisma | Lighter, SQL-like API, better Edge support |

## Directory Responsibilities

```
src/
├── app/                    # Routes & API (Next.js App Router)
│   ├── api/                # REST endpoints (stateless handlers)
│   ├── (pages)/            # UI pages (server + client components)
│   ├── layout.tsx          # Root layout (sidebar + header)
│   └── globals.css         # Tailwind CSS v4 + custom properties
├── components/
│   ├── editor/             # TipTap editor, renderer, tag selector
│   └── layout/             # Header, sidebar (shell UI)
├── lib/
│   ├── db/                 # Database: schema, connection, seed
│   ├── auth/               # NextAuth config, RBAC, API key validation
│   ├── search/             # Search: chunker, embeddings, qdrant, hybrid, RAG
│   └── utils.ts            # cn() helper
├── types/                  # TypeScript augmentations
└── workers/                # Background processes (not part of Next.js)
```

## External Dependencies

| Service | Purpose | Default URL | Required |
|---------|---------|-------------|----------|
| Qdrant | Vector storage & similarity search | `http://localhost:6333` | For semantic/hybrid/RAG search |
| Ollama | Embedding generation + LLM inference | `http://localhost:11434` | For semantic/hybrid/RAG search |

Both are optional for basic functionality (articles, FTS, auth work without them).
