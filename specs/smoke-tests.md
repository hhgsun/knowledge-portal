# Current State Validation

> **⚠️ Bu dosya `AGENTS.md`'ye tabidir.** Çelişki durumunda `AGENTS.md` geçerlidir.
> **Last verified**: 2026-08-27
> **Note**: This is a smoke-test checklist. For authoritative system docs see `AGENTS.md`.

This document describes how to verify the Knowledge Portal is functioning correctly. Use it before making changes (baseline) and after modifications (regression).

---

## Startup Verification

### Backend

| Step | Command | Expected Outcome |
|------|---------|-----------------|
| 1 | `cd backend && dotnet build` | Build succeeds with zero errors |
| 2 | `cd backend && dotnet run` | Server starts on `http://localhost:5174` |
| 3 | Check console | "Now listening on: http://localhost:5174" logged |
| 4 | Verify DB | PostgreSQL database exists (auto-created on startup) and contains tables |

### Frontend

| Step | Command | Expected Outcome |
|------|---------|-----------------|
| 1 | `cd frontend && npm install` | Dependencies resolve (React 19, Milkdown Crepe, etc.) |
| 2 | `cd frontend && npm run dev` | Vite dev server starts on `http://localhost:5173` |
| 3 | `cd frontend && npm run build` | TypeScript compilation + Vite build succeeds |

---

## Authentication Smoke Tests

### Login

| # | Action | Expected |
|---|--------|----------|
| 1 | `POST /api/auth/login` with `{"email":"admin@finagotech.com.tr","password":"1q2w3E*/"}` | 200: returns `{ token, user }` with `role: "admin"` |
| 2 | `POST /api/auth/login` with wrong password | 401: returns `{ error }` |
| 3 | `GET /api/auth/me` with valid Bearer token | 200: returns `{ id, name, email, role, isAzureUser }` |
| 4 | `GET /api/auth/me` with no token | 401 |

### Registration

| # | Action | Expected |
|---|--------|----------|
| 1 | `POST /api/auth/register` with valid name, unique email, 8+ char password | 201: returns `{ id, name, email }` |
| 2 | `POST /api/auth/register` with duplicate email | 400: email already exists |
| 3 | `POST /api/auth/register` with 4-char password | 400: password too short |

---

## RBAC Smoke Tests

| # | Role | Action | Expected |
|---|------|--------|----------|
| 1 | admin | `POST /api/articles` | 201: article created |
| 2 | editor | `POST /api/articles` | 201: article created |
| 3 | viewer | `POST /api/articles` | 201: article created (status forced to draft) |
| 4 | admin | `GET /api/admin/users` | 200: user list |
| 5 | editor | `GET /api/admin/users` | 403: forbidden |
| 6 | viewer | `GET /api/admin/users` | 403: forbidden |
| 7 | admin | `GET /api/analytics` | 200: analytics data |
| 8 | editor | `GET /api/analytics` | 200: analytics data |
| 9 | viewer | `GET /api/analytics` | 403: forbidden |

---

## Article Lifecycle Tests

| # | Action | Expected |
|---|--------|----------|
| 1 | Create article (admin): `POST /api/articles` with title | 201: returns `{ id, slug, title }` |
| 2 | Get article by slug: `GET /api/articles/{slug}` | 200: full article with content |
| 3 | Update article: `PUT /api/articles/{id}` with new content | 200: updated article |
| 4 | Check version created: `GET /api/articles/{id}/versions` | Array with ≥ 2 versions (content change triggers version) |
| 5 | Delete article: `DELETE /api/articles/{id}` | 200: deleted |
| 6 | Get deleted article | 404 |

---

## Tag Tests

| # | Action | Expected |
|---|--------|----------|
| 1 | `GET /api/tags` | 200: array with ≥ 11 default tags |
| 2 | `POST /api/tags` with `{"name":"new-tag"}` | 201: returns `{ id, name, slug }` |
| 3 | `POST /api/tags` with existing name | 200: returns existing tag (upsert — 200 if exists, 201 if new) |
| 4 | `DELETE /api/tags?id={id}` | 200: tag deleted |

---

## Search Tests

| # | Action | Expected |
|---|--------|----------|
| 1 | `GET /api/search?q=test` | 200: results array (may be empty) |
| 2 | `GET /api/search?q=@tutorial` | 200: tag-based search results |
| 3 | `GET /api/search?q=test&type=hybrid` | 200: document `results`; no generated `answer` |
| 3b | `GET /api/search?q=test&type=rag` | 400: unsupported Search mode |
| 4 | Check `search_queries` table | New record with query, results_count, response_time_ms |

---

## API Key Tests

| # | Action | Expected |
|---|--------|----------|
| 1 | `POST /api/keys` with `{"name":"test-key"}` | 201: returns raw key (`kp_...`), permissions, expiresAt |
| 2 | Use returned key as `X-API-Key: kp_...` header on `GET /api/articles` | 200: articles returned |
| 3 | Use API key on `GET /api/admin/users` | 403: API key rejected for session-only endpoint |
| 4 | Use API key on `DELETE /api/articles/{id}` | 403: destructive deletes are session-only (key cap) |
| 5 | Use admin-owned API key on `PUT /api/articles/{id}` of another user's article | 403: key capped at editor (`edit_own` only) |
| 6 | `DELETE /api/keys?id={id}` (session) | 200: key deleted |

---

## Bilgi Asistanı Tests

| # | Action | Expected |
|---|--------|----------|
| 1 | `POST /api/assistant` without auth | 401 |
| 2 | Post a portal policy question | 200: grounded `answer`, `rag`, `interactionId`, and `toolCalls: ["knowledge_rag"]`; no search results/routing metadata |
| 3 | Post with tag/author/content-type scope | 200: cited/consulted evidence remains inside the requested scope |
| 4 | Disable Ollama and post a question | 503; Assistant does not fall back to Search or ungrounded chat |
| 5 | Call MCP `search_articles` with `type=rag` | Tool validation error; only three document-search modes exist |
| 6 | Call MCP `ask_knowledge` | Grounded answer payload using the same canonical RAG service |
| 7 | Set `Assistant:Enabled=false` and restart backend | `POST /api/assistant` returns 404; `/api/search` remains operational |
| 8 | Call `GET /api/capabilities` after disabling Assistant | `assistant.enabled: false`; the authenticated UI hides navigation and rejects direct `/assistant` routing |
| 9 | Build with `VITE_ASSISTANT_ENABLED=false` | Assistant navigation and `/assistant` route are omitted regardless of backend capability |
| 10 | Submit thumbs-down with `wrong_source` | Owned interaction is updated; another user receives 403; admin feedback summary shows RAG cohorts |
| 11 | Click a cited source | `/api/assistant/source-click` updates only the owned interaction and cited/consulted article |
| 12 | Cancel an in-flight Assistant request | Browser aborts the request without a failure toast; a retry can be submitted normally |
| 13 | Call `GET /api/admin/rag/debug?q=...` as viewer/API key, then admin session | Viewer/key gets 403; admin gets retrieval/context plan without an LLM answer |
| 14 | Run `backend/scripts/run-rag-live-quality-gate.ps1` against deployment | Live grounded-answer quality thresholds pass |
| 15 | Create a conversation, ask a topic, then ask “peki detayları?” | Only the owner can read it; response query contains bounded prior topic context; delete-one/clear remove messages |
| 16 | Call `/api/assistant/stream` | `text/event-stream`: status → verified token chunks → complete; cancellation closes work without a server failure |
| 17 | Repeat a fully grounded answer, then update a cited article | Second request may be `cacheHit:true`; update forces miss. Another user/API-key scope never receives the entry |
| 18 | Inspect `assistant_interactions` after feedback | RAG versions/profile/grounding/answer hash exist; no raw query/answer or SearchQuery relation exists |

---

## Dashboard & Analytics Tests

| # | Action | Expected |
|---|--------|----------|
| 1 | `GET /api/dashboard` (authenticated) | 200: `{ totalArticles, viewsThisWeek, searchesToday, staleCount, recentArticles, topSearches }` |
| 2 | `GET /api/analytics` (admin/editor) | 200: `{ overview, topSearches, failedSearches, topArticles }` |
| 3 | `GET /api/analytics` via API key | 403: rejected |

---

## Health Check, Metrics & Rate Limiting

| # | Action | Expected |
|---|--------|----------|
| 1 | `GET /api/health` (no auth) | 200: `{ status: "healthy" \| "degraded", ollamaStatus, pendingEmbeddings, ... }` — `degraded` when Ollama down |
| 2 | `GET /api/health` while PostgreSQL is stopped | 503: `{ status: "unhealthy", error: "database unreachable" }` |
| 3 | `GET /api/health/live` (no auth) | 200: `{ status: "alive" }` (no dependency probes) |
| 4 | `GET /metrics` (internal network) | 200: Prometheus text incl. `kp_pending_embeddings` |
| 5 | 11x `POST /api/auth/login` in 1 minute from one IP | 11th request returns 429 (per-client partition — another IP unaffected) |
| 6 | 31x `GET /api/search?q=test` in 1 minute as one user | 31st request returns 429 (another user unaffected) |
| 7 | 61x `POST /mcp` (ping) in 1 minute with one key | 61st request returns 429 (another key unaffected) |
| 8 | `curl -I` any API response | `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer` present |

---

## Frontend UI Verification

| # | Action | Expected |
|---|--------|----------|
| 1 | Navigate to `http://localhost:5173` | Redirected to `/login` |
| 2 | Log in with admin credentials | Dashboard loads with stat cards |
| 3 | Navigate to Articles | Article list renders |
| 4 | Click "New Article" | Milkdown Crepe editor loads with its formatting UI |
| 5 | Type content, select tags, save | Redirected to article view |
| 6 | Navigate to Search | Search input with type tabs visible |
| 7 | Navigate to Assistant | Auto/manual mode controls render; a source-backed question shows citations/evidence |
| 8 | Navigate to Analytics | Stats, top searches, content gaps render |
| 9 | Navigate to Admin > Users | User table with pagination loads |
| 10 | Navigate to Settings > API Keys | Key list with create form loads |
| 11 | Click Logout | Redirected to `/login`, localStorage cleared |

---

## Seed Data Verification

| Entity | Expected State |
|--------|---------------|
| Admin user | `admin@finagotech.com.tr` with role `admin` exists |
| Default tags | 11 tags: project-knowledge-portal, getting-started, tutorial, troubleshooting, best-practices, api, deployment, security, performance, testing, monitoring |

---

## Known Limitations (Not Bugs)

These behaviors are by design in the current baseline and should not be treated as regressions:

| Behavior | Reason |
|----------|--------|
| Semantic/hybrid search falls back to fulltext when Ollama is unavailable | Graceful degradation by design (`warning` field set in response) |
| `/metrics` is anonymous | Not proxied by nginx — reachable only from the internal network |
| Notifications bell is non-functional | Visual indicator only; real notifications are backlog |
| Tag deletion not exposed in UI | Backend supports `DELETE /api/tags?id=` but no frontend control |

---

## Automated Test Suite

| Command | Expected |
|---------|----------|
| `cd backend/Tests && dotnet test` | 374 tests pass — no Docker required (EF Core InMemory + in-process fakes) |

Also runs as the gating `Test` stage in `azure-pipelines.yml` before image build/deploy.

### Coverage Gaps (known)
- No ArticleVersions tests
- Limited Tags scenario testing
- No Admin corner cases (self-demote, bulk ops)
- No search click tracking tests
- No frontend tests (0 coverage)
