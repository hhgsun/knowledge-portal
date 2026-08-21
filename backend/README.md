# Knowledge Portal — Backend API

ASP.NET Core Web API for the Knowledge Portal platform.

> `../AGENTS.md` is the project’s single source of truth. The complete endpoint contract is in `../specs/api-surface.md`; this README is a concise backend quick reference.

## Tech Stack

| Component | Technology |
|-----------|------------|
| Runtime | .NET 10 / C# 13 / ASP.NET Core |
| Data | EF Core 10 + Npgsql + PostgreSQL |
| Search | PostgreSQL Turkish FTS (`tsvector`/GIN) + pgvector HNSW |
| AI | Ollama-compatible embedding/chat services (`bge-m3`, `qwen2.5vl:7b`) |
| Auth | JWT Bearer + `kp_` API keys + Azure AD |
| Observability | Serilog + OpenTelemetry + Prometheus |
| Tests | xUnit + WebApplicationFactory; EF Core InMemory by default |

## Quick Start

Configure `ConnectionStrings:DefaultConnection` in `appsettings.json` for a PostgreSQL server with the `pgvector` extension available, then run:

```bash
cd backend
dotnet ef database update
dotnet run
```

The API starts at `http://localhost:5174`; Swagger is available at `/swagger` in Development.

On startup, the application applies relational migrations and seeds:

- The default admin user.
- 11 default tags.
- Seven `content_type` lookup values.
- `SeedData/articles/*.md` project documentation when the articles table is empty.

Seed articles are product documentation. They must be updated in the same change as the feature, architecture, configuration, security control, or operational behavior they describe.

## Architecture

```text
Controllers
    ↓
Domain/Search services
    ↓
EF Core AppDbContext
    ↓
PostgreSQL + pgvector

Search/RAG services ──→ Ollama-compatible embedding/chat endpoint
Background workers  ──→ durable index_jobs and RAG evaluation queues
```

Controllers handle routing, authentication scope, authorization, and response shaping. Shared business logic belongs in `Services/`; database access uses `AppDbContext`. Service failures are mapped to the standard `{ "error": "..." }` response.

## Project Structure

```text
backend/
├── Auth/             # JWT, API keys, RBAC, session-only authorization
├── Controllers/      # REST endpoints
├── Data/             # AppDbContext, DbInitializer, slug queries
├── Helpers/          # Markdown, attachment, search, slug, and RRF helpers
├── Mcp/              # MCP protocol types and tool execution
├── Middleware/       # Global exception handling
├── Models/
│   ├── Dtos.cs       # Request/response records
│   └── Entities/     # EF Core entities
├── Migrations/       # PostgreSQL migrations
├── SeedData/articles # Product documentation loaded on an empty database
├── Services/         # Domain, indexing, search/RAG, governance, observability
├── Tests/            # Docker-free default test suite
└── Tests.Postgres/   # PostgreSQL/pgvector fidelity gate
```

## Authentication and Authorization

- JWT Bearer tokens represent interactive sessions.
- API keys use the `X-API-Key: kp_...` header and carry `source=api-key`.
- Azure AD login is exchanged for a local JWT through the frontend MSAL redirect-bridge flow.
- Permission names come from `Auth/Permissions.cs`; do not use magic permission strings.
- API-key principals are capped at editor authority, never receive delete permissions, and cannot call session-only endpoints.
- Destructive endpoints use `RequireSessionAuth`; removing one’s own article vote is the documented exception.

The authoritative RBAC and endpoint authorization matrices are in `../AGENTS.md`.

## Search and RAG

`GET /api/search` supports `fulltext`, `semantic`, `hybrid`, and `rag` modes:

- Fulltext uses PostgreSQL Turkish FTS with AND → OR → escaped `ILIKE` fallback.
- Semantic search uses `bge-m3` 1024-dimensional embeddings stored in pgvector.
- Hybrid search merges a wide lexical/semantic pool with RRF and applies a deterministic reranker.
- RAG performs hybrid chunk retrieval, provenance-aware reranking, narrow or bounded-parallel map-reduce generation, and fail-closed claim/citation validation.

Inline filters are `@author`, `#tag`, and `##content-type`. Equivalent repeatable query parameters are also supported.

Published article and attachment indexing is coordinated through the durable PostgreSQL `index_jobs` queue. Workers use leases, `FOR UPDATE SKIP LOCKED`, bounded parallelism, exponential retry, and terminal failure tracking.

## Configuration

Important `appsettings.json` sections:

| Section | Purpose |
|---------|---------|
| `ConnectionStrings` | PostgreSQL connection |
| `Jwt`, `AzureAd` | Authentication |
| `RateLimiting` | Per-client auth/search/MCP limits |
| `Ollama` | Models, dimensions, retrieval and context limits |
| `Indexing` | Durable queue workers, leases and retry |
| `RagResilience` | Bulkhead, budgets, timeouts, retry and circuit breaker |
| `FileStorage` | Upload path, size/type limits and integrity sampling |
| `OpenTelemetry` | Optional OTLP export |
| `Logging`, `Serilog` | File retention and log levels |

Tracked deployment connection/JWT values are an explicit repository-owner decision documented in `../AGENTS.md`; do not relocate or redact them unless that decision is reopened.

## Middleware Pipeline

```text
ForwardedHeaders
→ HSTS (non-development)
→ SecurityHeaders
→ GlobalExceptionMiddleware
→ CORS
→ ApiKeyMiddleware
→ Authentication
→ RateLimiter
→ Authorization
→ Controllers
```

Authentication runs before rate limiting so partitions can use API-key or user identity. Forwarded Headers run first so rate limiting and scheme handling see the proxy-provided client information.

## MCP

The stateless JSON-RPC 2.0 endpoint is available at `/mcp` through GET and POST. It uses API-key or JWT authentication only—no OAuth—and is rate-limited per caller.

Clients discover capabilities with `initialize` and `tools/list`, then invoke tools with `tools/call`. Tools return an `outputSchema`, structured JSON in `structuredContent`, and compatibility text. Article/search output includes provenance and content-security assessment; known secret patterns are redacted.

See `../specs/api-surface.md` for the current tool catalog and schemas.

## Tests

```bash
# Docker-free default suite
dotnet test backend/Tests/KnowledgePortal.Api.Tests.csproj

# Real PostgreSQL/pgvector fidelity suite
$env:RAG_FIDELITY_CONNECTION_STRING = "Host=localhost;Database=knowledge_portal_fidelity;Username=postgres;Password=..."
dotnet test backend/Tests.Postgres/KnowledgePortal.Api.PostgresTests.csproj --configuration Release
```

The default suite uses an isolated EF Core InMemory database per test class and deterministic AI/vector fakes. CI additionally requires the PostgreSQL fidelity gate and the live-Ollama golden-dataset RAG quality gate.

## Health and Observability

- `GET /api/health/live`: liveness, always 200.
- `GET /api/health`: PostgreSQL readiness plus timeout-bounded/cached Ollama health.
- `GET /metrics`: internal Prometheus endpoint.
- Admin diagnostics: `/api/search/diagnostics`, `/api/search/embedding-status`, `/api/search/storage-status`, `/api/search/rag-observability`.
- Routine queue recovery: `POST /api/search/repair-indexing` repairs only missing/stuck jobs without invalidating healthy indexes; corpus-wide `POST /api/search/reindex` is reserved for planned maintenance.

Prometheus RAG alerts are under `../ops/prometheus/`; the Grafana dashboard is under `../ops/grafana/`.
