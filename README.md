# Knowledge Portal

Enterprise knowledge management system with AI-powered semantic search, WYSIWYG editing, RBAC, content governance, and analytics.

## Tech Stack

- **Frontend**: Next.js 16 (App Router, Turbopack) + React 19 + Tailwind CSS v4
- **Editor**: TipTap (ProseMirror-based WYSIWYG)
- **Auth**: NextAuth.js v5 beta (JWT strategy, credentials provider)
- **Database**: SQLite (WAL mode) via Drizzle ORM + @libsql/client (pure JS)
- **Vector DB**: Qdrant (semantic search)
- **LLM**: Ollama (embeddings via nomic-embed-text + RAG via llama3)
- **Deployment**: Docker Compose

## Quick Start

```bash
# Install dependencies
npm install

# Create database + run migrations
npx drizzle-kit migrate

# Seed with admin user
npm run db:seed

# Start development server
npm run dev
```

Open [http://localhost:3000](http://localhost:3000) — login with:
- **Email**: `admin@knowledge.local`
- **Password**: `admin123`

## Docker Compose (Full Stack)

```bash
# Start all services (web, qdrant, ollama, worker)
docker compose up -d

# Pull embedding model into Ollama
docker compose exec ollama ollama pull nomic-embed-text

# Pull LLM for RAG
docker compose exec ollama ollama pull llama3
```

## Project Structure

```
src/
├── app/                  # Next.js App Router (pages + API routes)
│   ├── api/              # REST API (articles, search, auth, analytics, tags, keys)
│   ├── articles/         # Article browse, create, view, edit
│   ├── search/           # Search page (fulltext, semantic, hybrid, RAG)
│   ├── analytics/        # Analytics dashboard (admin/editor only)
│   ├── admin/            # User management (admin only)
│   ├── settings/         # API key management
│   └── login/register/   # Auth pages
├── components/           # React components (editor, layout)
├── lib/
│   ├── db/               # Drizzle ORM schema + connection + seed
│   ├── auth/             # NextAuth config + RBAC + API key validation
│   └── search/           # Qdrant, embeddings, chunker, hybrid search, RAG
├── types/                # TypeScript type extensions (next-auth.d.ts)
└── workers/              # Background embedding indexer + staleness checker
```

## Available Scripts

| Command | Description |
|---------|-------------|
| `npm run dev` | Start dev server |
| `npm run build` | Production build |
| `npm run db:generate` | Generate new migration from schema changes |
| `npm run db:migrate` | Apply pending migrations |
| `npm run db:seed` | Seed database with initial data |
| `npm run worker` | Start background worker (embeddings + staleness) |

## Features

- **WYSIWYG Editor** — TipTap with headings, code blocks, tables, task lists, highlights
- **AI Semantic Search** — Qdrant vector search + SQLite FTS5 hybrid via Reciprocal Rank Fusion
- **RAG Q&A** — Ask questions, get answers with cited sources from the knowledge base
- **RBAC** — Admin / Editor / Viewer roles with granular permissions
- **Version History** — Full audit trail of article changes with diff support
- **Content Freshness** — Automatic staleness detection, owner notifications
- **Analytics** — Views, search queries, content gaps, feedback tracking
- **External API** — API key-authenticated REST endpoints for integrations

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `DATABASE_URL` | `./data/knowledge.db` | SQLite database path |
| `NEXTAUTH_URL` | `http://localhost:3000` | App URL for auth |
| `NEXTAUTH_SECRET` | (required) | JWT signing secret |
| `QDRANT_URL` | `http://localhost:6333` | Qdrant vector DB URL |
| `OLLAMA_URL` | `http://localhost:11434` | Ollama LLM server URL |
