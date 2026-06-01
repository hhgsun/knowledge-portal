# Current State Validation

This document describes the verifiable baseline state of the Knowledge Portal system as of 2026-05-22. Use it to confirm the system is functioning correctly before making changes, and to run regression checks after modifications.

---

## Startup Verification

### Backend

| Step | Command | Expected Outcome |
|------|---------|-----------------|
| 1 | `cd backend && dotnet build` | Build succeeds with zero errors |
| 2 | `cd backend && dotnet run` | Server starts on `http://localhost:5174` |
| 3 | Check console | "Now listening on: http://localhost:5174" logged |
| 4 | Verify DB | `../data/knowledge.db` file exists and contains tables |

### Frontend

| Step | Command | Expected Outcome |
|------|---------|-----------------|
| 1 | `cd frontend && npm install` | Dependencies resolve (React 19, TipTap, etc.) |
| 2 | `cd frontend && npm run dev` | Vite dev server starts on `http://localhost:5173` |
| 3 | `cd frontend && npm run build` | TypeScript compilation + Vite build succeeds |

---

## Authentication Smoke Tests

### Login

| # | Action | Expected |
|---|--------|----------|
| 1 | `POST /api/auth/login` with `{"email":"admin@knowledge.local","password":"admin123"}` | 200: returns `{ token, user }` with `role: "admin"` |
| 2 | `POST /api/auth/login` with wrong password | 401: returns `{ error }` |
| 3 | `GET /api/auth/me` with valid Bearer token | 200: returns `{ id, name, email, role, avatar }` |
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
| 3 | Update article: `PUT /api/articles/{id}` with new title | 200: updated slug and title |
| 4 | Check version created: `GET /api/articles/{id}/versions` | Array with ≥ 2 versions |
| 5 | Delete article: `DELETE /api/articles/{id}` | 200: deleted |
| 6 | Get deleted article | 404 |

---

## Tag Tests

| # | Action | Expected |
|---|--------|----------|
| 1 | `GET /api/tags` | 200: array with ≥ 10 default tags |
| 2 | `POST /api/tags` with `{"name":"new-tag"}` | 201: returns `{ id, name, slug }` |
| 3 | `POST /api/tags` with existing name | 201: returns existing tag (idempotent) |
| 4 | `DELETE /api/tags?id={id}` | 200: tag deleted |

---

## Search Tests

| # | Action | Expected |
|---|--------|----------|
| 1 | `GET /api/search?q=test` | 200: results array (may be empty) |
| 2 | `GET /api/search?q=@tutorial` | 200: tag-based search results |
| 3 | `GET /api/search?q=test&type=rag` | 200: placeholder RAG response |
| 4 | Check `search_queries` table | New record with query, results_count, response_time_ms |

---

## API Key Tests

| # | Action | Expected |
|---|--------|----------|
| 1 | `POST /api/keys` with `{"name":"test-key"}` | 201: returns raw key (`kp_...`), permissions, expiresAt |
| 2 | Use returned key as `Authorization: Bearer kp_...` on `GET /api/articles` | 200: articles returned |
| 3 | Use API key on `GET /api/admin/users` | 403: API key rejected for session-only endpoint |
| 4 | `DELETE /api/keys?id={id}` | 200: key deleted |

---

## Dashboard & Analytics Tests

| # | Action | Expected |
|---|--------|----------|
| 1 | `GET /api/dashboard` (authenticated) | 200: `{ totalArticles, viewsThisWeek, searchesToday, staleCount, recentArticles, topSearches }` |
| 2 | `GET /api/analytics` (admin/editor) | 200: `{ overview, topSearches, failedSearches, topArticles }` |
| 3 | `GET /api/analytics` via API key | 403: rejected |

---

## Frontend UI Verification

| # | Action | Expected |
|---|--------|----------|
| 1 | Navigate to `http://localhost:5173` | Redirected to `/login` |
| 2 | Log in with admin credentials | Dashboard loads with stat cards |
| 3 | Navigate to Articles | Article list renders |
| 4 | Click "New Article" | Editor page loads with TipTap toolbar |
| 5 | Type content, select tags, save | Redirected to article view |
| 6 | Navigate to Search | Search input with type tabs visible |
| 7 | Navigate to Analytics | Stats, top searches, content gaps render |
| 8 | Navigate to Admin > Users | User table with pagination loads |
| 9 | Navigate to Settings > API Keys | Key list with create form loads |
| 10 | Click Logout | Redirected to `/login`, localStorage cleared |

---

## Seed Data Verification

| Entity | Expected State |
|--------|---------------|
| Admin user | `admin@knowledge.local` with role `admin` exists |
| Default tags | 10 tags: getting-started, tutorial, troubleshooting, best-practices, api, deployment, security, performance, testing, monitoring |

---

## Known Limitations (Not Bugs)

These behaviors are by design in the current baseline and should not be treated as regressions:

| Behavior | Reason |
|----------|--------|
| Semantic search returns empty/stub results | Not yet implemented (placeholder endpoint) |
| RAG search returns placeholder response | Not yet implemented (placeholder endpoint) |
| `backend/Services/` directory is empty | No service layer; logic in controllers |
| No test suite exists | Test infrastructure not yet established |
| Dark mode follows system only (no toggle) | Dark mode toggle is a backlog item |
| Notifications bell is non-functional | Visual indicator only; real notifications are backlog |
| User profile button is non-functional | Profile page is a backlog item |
| Register success message not shown | `?registered=true` query param not handled on login page |
