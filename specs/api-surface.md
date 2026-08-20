# API Surface

> **⚠️ Bu dosya `AGENTS.md`'ye tabidir.** Çelişki durumunda `AGENTS.md` geçerlidir.
> Endpoint Authorization Matrix → `AGENTS.md` · Validation Rules → `AGENTS.md`

Base URL: `http://localhost:5174/api`
All endpoints return JSON. All timestamps are ISO 8601 UTC.

## Authentication Headers

| Method | Header | Format |
|--------|--------|--------|
| JWT | `Authorization` | `Bearer <token>` |
| API Key | `X-API-Key` | `kp_<key>` |

In endpoint descriptions below, **"JWT or API Key"** means the endpoint accepts either header.

## Rate Limiting

| Policy | Limit | Window | Endpoints |
|--------|-------|--------|-----------|
| `auth` | 10 requests | 1 minute | `POST /api/auth/login`, `POST /api/auth/register`, `POST /api/auth/azure-login` |
| `search` | 30 requests | 1 minute | `GET /api/search` |
| `mcp` | 60 requests | 1 minute | `GET /mcp`, `POST /mcp` |

When rate limit is exceeded, returns `429 Too Many Requests`.

Limits are **partitioned per client**: partition key = API key id > user id > client IP (real client IP resolved via ForwardedHeaders behind the reverse proxy). Each client gets its own window; one abuser cannot exhaust the shared budget.

---

## Health Check

### `GET /api/health` (readiness)
**Auth**: None

**200 Response** (healthy, or degraded when only Ollama is down):
```json
{ "status": "healthy", "timestamp": "2026-01-01T00:00:00.0000000Z", "ollamaStatus": "connected", "embeddingModel": "bge-m3", "pendingEmbeddings": 0 }
```
`status` values: `healthy` | `degraded` (Ollama unavailable — search falls back to fulltext) | `unhealthy`.

**503 Response** (database unreachable): same shape with `status: "unhealthy"`, `pendingEmbeddings: null`, `error: "database unreachable"`.

### `GET /api/health/live` (liveness)
**Auth**: None

Always `200` — `{ "status": "alive" }`. No dependency probes.

### `GET /metrics`
**Auth**: None (not proxied by nginx — reachable only from the internal network)

Prometheus text exposition: ASP.NET Core request metrics + `kp_pending_embeddings` gauge + `kp_embedding_failures` counter.

---

## MCP (Model Context Protocol)

### `POST /mcp`
**Auth**: X-API-Key or Bearer token (required)  
**Transport**: Streamable HTTP (stateless, JSON-RPC 2.0)  
**Protocol Version**: negotiated — supported: 2025-11-25 (default), 2025-06-18, 2025-03-26, 2024-11-05. `initialize` echoes a supported client version and otherwise returns the default.

POST requests require `Content-Type: application/json`. `MCP-Protocol-Version`, when supplied, must contain a supported version. Browser-originated requests are accepted only from the MCP endpoint's own host. JSON-RPC notifications return `202 Accepted` with no body; MCP JSON-RPC batch payloads are rejected.

Exposes Knowledge Portal tools via the Model Context Protocol. AI tools (Claude Desktop, Cursor, VS Code Copilot) can connect to this endpoint to search articles, get article content, list tags, and retrieve portal statistics.

**Supported Methods**: `initialize`, `notifications/initialized` (returns 202 Accepted, empty body), `tools/list`, `tools/call`, `ping`

**Available Tools**:
- `search_articles` — Portal-equivalent search across published articles. Params: `query*`, `type` (`fulltext|semantic|hybrid|rag`, default `fulltext`), `page`, `limit`, `tags`, `authors`, `content_type`, `include_content`, `include_attachments`, `only_own_content`. Supports `@author`, `#tag`, and `##content-type` inline syntax. Full-text/tag searches are paged; semantic/hybrid/RAG use the same top-N/fallback behavior as `GET /api/search`.
- `get_article` — Get article details by ID or slug (params: id_or_slug*)
- `list_articles` — List published articles with pagination (params: page, limit, content_type, tags, sort — sort validated against `newest|oldest|most_viewed`)
- `list_tags` — List all available tags with article counts
- `get_portal_info` — Portal statistics (counts, content type distribution, recent articles)
- `get_project_context` — Build a governed project briefing from a project tag
- `get_integration_guidance` — Hybrid retrieval for an integration goal, optionally project-scoped
- `find_authoritative_content` — Find decision sources and expose governance-recommended source ordering
- `compare_sources` — Compare 2-10 published sources with canonical content and governance; contradiction status remains explicit
- `get_recent_changes` — Recently updated published knowledge, optionally scoped to a project tag

**Tool result format**: Every tool advertises an `outputSchema` and returns the machine-readable payload in `structuredContent`. For backwards compatibility, the same serialized JSON is also returned as `{ "content": [{ "type": "text", "text": "..." }] }`. Tool failures use `isError: true`.

Search results include `evidenceAvailable` and an `evidence[]` provenance array. Evidence contains the article ID/slug, canonical API URL, source type, matched passage when available, update timestamp, match type, and score. Title-only matches explicitly set `evidenceAvailable: false` rather than fabricating a passage. RAG sources similarly include their canonical URL and source type.

MCP search hits also include `governance`: optional approval state (`approved` or `not_recorded`), approver/time when recorded, review state (`current`, `due_soon`, `overdue`, `not_recorded`), next review date, dynamic content-type label/authority weight, reliability score, and warnings. Content-type authority is configured as `authorityWeight` (0-100, default 50) on each dynamic `content_type` lookup; no content-type names are hard-coded. Directly published/imported content remains available and is truthfully marked `not_recorded`. Search responses aggregate caution indicators and reliability-ordered `recommendedArticleIds` in `decisionSupport`. Semantic contradiction detection is deliberately not claimed: `conflictAssessment` is `not_evaluated` until a dedicated contradiction evaluator exists.

MCP article/search/compare outputs include `securityAssessment` (`riskLevel`, explainable `signals`, `secretsRedacted`, `treatAsUntrustedData`, `allowAutomaticExecution=false`). Common portal keys, bearer tokens, JWTs, AWS access-key IDs, and assigned secret/token/password values are replaced with `[REDACTED_SECRET]` throughout structured and compatibility-text output. Injection signals are flagged, not silently deleted. RAG source blocks redact secrets, mark risky chunks as `SECURITY-RISK`, neutralize source delimiters, and explicitly forbid following source instructions, tool execution, URL visits, or credential disclosure.

Every `tools/call` response includes `X-Trace-Id`. A structured audit event records trace ID, tool, outcome, auth source, user/API-key identifiers, bounded client user-agent, protocol version, duration, serialized output size, and a privacy-preserving argument shape. Raw argument values, queries, article content, credentials, and reversible hashes are never written to the MCP audit event. Prometheus exports `kp_mcp_tool_calls`, `kp_mcp_tool_errors`, `kp_mcp_tool_duration_ms`, and `kp_mcp_tool_output_bytes`, tagged by bounded tool/outcome/auth dimensions.

MCP resilience limits are configurable under `Mcp`: request body 256 KiB, output default 1 MiB, tool/mode-specific execution budgets, bounded AI concurrency (default 2), and an instance-local Ollama circuit breaker (3 transient failures, 30-second break by default). Resilience failures use structured tool errors with stable codes (`tool_timeout`, `server_busy`, `circuit_open`, `output_too_large`), `retryable`, optional `retryAfterSeconds`, and details. Client disconnects propagate cancellation and are audited as `cancelled`. These controls are intentionally instance-local; distributed concurrency/rate limiting requires a gateway or shared store when horizontally scaled.

**Client configuration example (Claude Desktop)**:
```json
{
  "mcpServers": {
    "knowledge-portal": {
      "url": "http://localhost:5174/mcp",
      "headers": { "X-API-Key": "kp_your_api_key_here" }
    }
  }
}
```

### `GET /mcp`
**Auth**: X-API-Key or Bearer token (required)

Returns `405 Method Not Allowed` with `Allow: POST`. This server is stateless and does not offer an SSE stream or server-initiated messages.

---

## Authentication

### `POST /api/auth/login`
**Auth**: None

| Field | Type | Required | Constraints |
|-------|------|----------|-------------|
| `email` | string | Yes | — |
| `password` | string | Yes | — |

**200 Response**:
```json
{
  "token": "eyJhbG...",
  "user": { "id": "...", "name": "...", "email": "...", "role": "admin" }
}
```

**401**: Invalid credentials.

---

### `POST /api/auth/register`
**Auth**: None

| Field | Type | Required | Constraints |
|-------|------|----------|-------------|
| `name` | string | Yes | — |
| `email` | string | Yes | Must be unique |
| `password` | string | Yes | 8–128 characters |

**201 Response**: `{ "id", "name", "email" }`
**400**: Validation error (missing fields, password length).
**409**: Email already registered.

---

### `POST /api/auth/azure-login`
**Auth**: None
**Rate Limit**: `auth` (10/min)

| Field | Type | Required | Constraints |
|-------|------|----------|-------------|
| `accessToken` | string | Yes | Valid Azure AD access token with User.Read scope |

**200 Response**: `{ "token", "user": { "id", "name", "email", "role" } }`
**400**: Azure AD not enabled, missing token, or account has no email.
**401**: Invalid Azure AD token (Microsoft Graph validation failed).

Behavior:
- Validates token against Microsoft Graph `/me` endpoint
- Finds existing user by AzureObjectId or email
- If not found, auto-creates a viewer user
- Links AzureObjectId on first Azure login for existing email users
- Syncs display name from Azure AD profile on each login

---

### `GET /api/auth/me`
**Auth**: Bearer JWT

**200 Response**: `{ "id", "name", "email", "role", "isAzureUser" }`
**401**: Invalid/expired token.

---

### `PUT /api/auth/profile`
**Auth**: Bearer JWT

| Field | Type | Required | Constraints |
|-------|------|----------|-------------|
| `name` | string | No | Min 1 char, trimmed. Rejected (400) for Azure AD users — managed by Microsoft account |
| `email` | string | No | Valid email, unique. Rejected (400) for Azure AD users — managed by Microsoft account |
| `currentPassword` | string | No | Required if changing password (not required for Azure users setting password first time) |
| `newPassword` | string | No | 8–128 characters |

**200 Response**: `{ "id", "name", "email", "role" }`
**400**: Validation error (password length, current password incorrect, Azure-managed name/email change attempt).
**409**: Email already in use.

---

## Articles

### `GET /api/articles`
**Auth**: Bearer (JWT or API Key)

| Param | Type | Default | Notes |
|-------|------|---------|-------|
| `page` | int | 1 | Pagination |
| `limit` | int | 20 | Max results per page |
| `status` | string | — | Filter by status |
| `contentType` | string | — | Filter by content type (reference, how-to, adr, runbook, faq, policy, onboarding) |
| `mine` | bool | false | If true, return only articles owned by the current user |
| `q` | string | — | Search title (LIKE, wildcards `%`/`_` escaped) |
| `tag` | string[] | — | Filter by tag slugs (AND logic) |
| `dateFrom` | string | — | Filter articles updated on or after this date |
| `dateTo` | string | — | Filter articles updated before this date (+1 day) |
| `onlyOwnContent` | bool | false | When true + API key auth → filters to articles created by that API key |
| `includeContent` | bool | false | When true → includes article content as plain text in results |
| `includeAttachments` | bool | false | When true → includes attachment metadata per article in results |

**Visibility rules**:
- Viewers see only `published` articles + their own (any status)
- Editors/admins see all statuses (filtered if `status` param provided)

**200 Response**:
```json
{
  "articles": [
    {
      "id": "...", "title": "...", "slug": "...", "excerpt": "...",
      "status": "published", "contentType": "reference",
      "updatedAt": "...", "ownerName": "...", "apiKeyName": null,
      "tags": [{ "id": "...", "name": "...", "slug": "..." }],
      "viewCount": 5, "wilsonScore": 0.72,
      "content": "plain text (only if includeContent=true)",
      "attachments": [{ "id": "...", "fileName": "...", "contentType": "...", "sizeBytes": 1024, "downloadUrl": "/api/attachments/.../download" }]
    }
  ],
  "total": 42
}
```

---

### `POST /api/articles`
**Auth**: Bearer (JWT or API Key)
**Permission**: `articles:create`

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `title` | string | Yes | — |
| `contentMarkdown` | string | No | Canonical CommonMark/GFM Markdown edited with Milkdown |
| `excerpt` | string | No | — |
| `status` | string | No | Default: `"draft"` |
| `contentType` | string | No | Default: `"reference"` |
| `tags` | string[] | No | Array of tag ID, name, or slug (resolved in that priority) |

**Side effects**: Creates version 1 with the initial content.
**201 Response**: `{ "id", "slug", "title" }`

---

### `GET /api/articles/{idOrSlug}`
**Auth**: Bearer (JWT or API Key)

Accepts both article ID and slug for lookup.

**Side effects**: Records an `ArticleView` entry (deduplicated: same user+article within 15 minutes counts as 1 view).
**200 Response**: Full article object with canonical `contentMarkdown`, derived `contentText`, and attachment metadata.
**404**: Article not found.

---

### `PUT /api/articles/{id}`
**Auth**: Bearer (JWT or API Key)
**Permission**: Owner of the article, OR `articles:edit_any`. Additionally: `articles:publish` required to set status→published, `articles:archive` required to set status→archived.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `title` | string | No | Slug regenerated if title changes |
| `contentMarkdown` | string | No | Canonical CommonMark/GFM Markdown |
| `excerpt` | string | No | — |
| `status` | string | No | Requires `articles:publish` for "published", `articles:archive` for "archived" |
| `contentType` | string | No | — |
| `changeSummary` | string | No | Stored in version record |
| `tags` | string[] | No | Array of tag ID, name, or slug (replaces all existing tags) |

**Side effects**: If content changes, creates a new `ArticleVersion` with incremented version number.
**200 Response**: `{ "id", "slug", "title" }`
**403**: Not owner and lacks `articles:edit_any`, OR lacks `articles:publish`/`articles:archive` for status change.

---

### `DELETE /api/articles/{id}`
**Auth**: Bearer (JWT or API Key)
**Permission**: Owner of the article, OR `articles:delete_any`

**200 Response**: `{ "message": "Article deleted" }`
**403**: Not owner and lacks `articles:delete_any`.

---

### `POST /api/articles/{id}/approve`
**Auth**: Bearer (JWT or API Key)
**Permission**: `articles:approve`

Adds an approval trust signal to an already-published article without changing its publication status.

**Guards**: Article must be in `published` status.
**200 Response**: `{ "message": "Article approved", "id": "...", "slug": "...", "approvedAt": "..." }`
**400**: Article is not published.

---

### `DELETE /api/articles/{id}/approve`
**Auth**: Bearer (JWT or API Key)
**Permission**: `articles:approve`

Removes the recorded approval without unpublishing the article.

**Guards**: Article must currently have a recorded approval.
**200 Response**: `{ "message": "Article approval removed", "id": "...", "slug": "..." }`
**400**: Article is not approved.

`POST /api/articles/{id}/reject` remains as a backwards-compatible alias.

---

## Attachments

### `GET /api/articles/{id}/attachments`
**Auth**: Bearer (JWT or API Key)

**200 Response**:
```json
{
  "attachments": [
    { "id": "...", "fileName": "diagram.png", "contentType": "image/png", "sizeBytes": 102400, "downloadUrl": "/api/attachments/.../download", "createdAt": "2026-01-01T00:00:00Z" }
  ],
  "total": 1
}
```

### `POST /api/articles/{id}/attachments`
**Auth**: Bearer (JWT or API Key) — requires `articles:edit_own` (if owner) or `articles:edit_any`
**Content-Type**: `multipart/form-data`
**Body**: `file` (IFormFile, max 20MB, extension whitelist enforced)

**201 Response**:
```json
{ "id": "...", "fileName": "diagram.png", "contentType": "image/png", "sizeBytes": 102400, "downloadUrl": "/api/attachments/.../download", "createdAt": "2026-01-01T00:00:00Z" }
```
**400**: Empty file, invalid extension, MIME mismatch, max attachments reached.
**403**: No edit permission.

### `DELETE /api/articles/{id}/attachments/{attachmentId}`
**Auth**: Bearer session only — requires `articles:edit_own` (if owner) or `articles:edit_any`

**200 Response**: `{ "message": "Attachment deleted" }`
**403**: No edit permission.
**404**: Article or attachment not found.

### `GET /api/attachments/{id}/download`
**Auth**: Bearer (JWT or API Key)

Returns the file as a binary stream with appropriate `Content-Type` and `Content-Disposition` headers.
The caller must also be allowed to view the parent article; draft ownership is enforced and inline images are loaded with bearer-authenticated fetches.
**404**: Attachment/parent article not visible, or file missing from disk.

---

## Tags

Article create/update requests may include a new tag name in their `tags` array. For users with `tags:manage` (and API-key article flows), the tag is created and linked atomically when the article is saved; merely entering it in the editor does not persist it.

### `GET /api/tags`
**Auth**: Bearer (JWT or API Key)

Without query parameters, the legacy response is an array:
```json
[
  { "id": "...", "name": "getting started", "slug": "getting-started", "articleCount": 5 }
]
```

For asynchronous selectors, accepts `page` (default 1), `limit` (default 30, max 100), `q` (name/slug search), and repeatable `ids` (selected-tag lookup). Supplying any of these returns a paged response:

```json
{
  "tags": [{ "id": "...", "name": "getting started", "slug": "getting-started", "articleCount": 5 }],
  "total": 42,
  "page": 1,
  "totalPages": 2
}
```

---

### `POST /api/tags`
**Auth**: Bearer (JWT or API Key)
**Permission**: `tags:manage`

| Field | Type | Required |
|-------|------|----------|
| `name` | string | Yes |

**Side effects**: Auto-generates slug. Returns existing tag if slug already exists.
**201 Response**: `{ "id", "name", "slug" }`

---

### `PUT /api/tags`
**Auth**: Bearer (JWT or API Key)
**Permission**: `tags:manage`

| Field | Type | Required |
|-------|------|----------|
| `id` | string | Yes |
| `name` | string | Yes |

**Side effects**: Regenerates slug from new name. Returns 409 if new slug conflicts with another tag.
**200 Response**: `{ "id", "name", "slug" }`

---

### `DELETE /api/tags?id={id}`
**Auth**: Bearer (JWT or API Key)
**Permission**: `tags:manage`

**Side effects**: Returns 409 if tag has associated articles. Only content-free tags can be deleted.
**200 Response**: `{ "message": "Tag deleted" }`

---

## Article Versions

### `GET /api/articles/{articleId}/versions`
**Auth**: Bearer

**200 Response**:
```json
[
  {
    "id": "...", "version": 3, "title": "...", "changeSummary": "Fixed typo",
    "changedBy": "userId", "changedByName": "Admin", "createdAt": "..."
  }
]
```
Ordered by version number descending.

---

### `GET /api/articles/{articleId}/versions/{versionId}`
**Auth**: Bearer

**200 Response**: Version object with its canonical `contentMarkdown` snapshot.

---

## Article Votes & Comments

### `POST /api/articles/{articleId}/vote`
**Auth**: Bearer

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `isHelpful` | bool | Yes | true = helpful, false = not helpful |
| `reason` | string | No | Only accepted when isHelpful=false |

**201 Response** (new vote): `{ "action": "created" }`
**200 Response** (toggle off): `{ "action": "removed" }`
**200 Response** (changed): `{ "action": "changed" }`

---

### `DELETE /api/articles/{articleId}/vote`
**Auth**: Bearer

**200 Response**: `{ "message": "Vote removed" }`
**404 Response**: `{ "error": "No vote found" }`

---

### `GET /api/articles/{articleId}/votes`
**Auth**: Bearer

**200 Response**:
```json
{
  "helpful": 12,
  "notHelpful": 3,
  "wilsonScore": 0.6842,
  "userVote": true,
  "reasons": ["Güncel değil", "Eksik bilgi"]
}
```

---

### `POST /api/articles/{articleId}/comments`
**Auth**: Bearer

| Field | Type | Required |
|-------|------|----------|
| `comment` | string | Yes |

**201 Response**: `{ "message": "Comment added" }`
**400 Response**: `{ "error": "Comment is required" }`

---

### `GET /api/articles/{articleId}/comments`
**Auth**: Bearer

**200 Response**:
```json
{
  "comments": [
    { "id": "abc", "comment": "Very useful", "userName": "John", "createdAt": "2026-01-01T00:00:00Z", "isOwn": false }
  ]
}
```

---

### `DELETE /api/articles/{articleId}/comments/{commentId}`
**Auth**: Bearer

**200 Response**: `{ "message": "Comment deleted" }`
**403 Response**: `{ "error": "You can only delete your own comments" }`
**404 Response**: `{ "error": "Comment not found" }`

---

### `GET /api/articles/{id}/related`
**Auth**: Bearer

| Param | Type | Default | Notes |
|-------|------|---------|-------|
| `limit` | int | 5 | Max 20 |

**200 Response**:
```json
{
  "articles": [
    { "id": "...", "title": "...", "slug": "...", "excerpt": "...", "contentType": "how-to", "updatedAt": "...", "tags": [{ "id": "...", "name": "...", "slug": "..." }] }
  ]
}
```

---

## Search

### `GET /api/search`
**Auth**: Bearer (JWT or API Key)

| Param | Type | Default | Notes |
|-------|------|---------|-------|
| `q` | string | — | Required. Inline syntax: `@user-slug` (author), `#tag-slug` (tag), `##content-type` (type) |
| `type` | string | `"fulltext"` | `fulltext`, `semantic`, `hybrid`, `rag` |
| `limit` | int | 20 | Max results per page (1–50) |
| `page` | int | 1 | Page number (min 1). Applies to `fulltext` and tag-browse; `semantic`/`hybrid` are top-N only |
| `onlyOwnContent` | bool | false | Optional. When true + API key auth → filters to articles created by that API key |
| `includeContent` | bool | false | Optional. When true → includes article content as plain text derived from Markdown in search results |
| `includeAttachments` | bool | false | Optional. When true → includes attachment metadata (id, fileName, contentType, sizeBytes, downloadUrl) per article |
| `tag` | string[] | — | Optional, repeatable. Tag slugs (merged with #syntax) |
| `author` | string[] | — | Optional, repeatable. User slugs (merged with @syntax) |
| `contentType` | string[] | — | Optional, repeatable. Content type values (merged with ##syntax) |

**Inline query syntax** (parsed in order: `##` → `#` → `@` → text):
- `@user-slug` — filter by author (OR when multiple)
- `#tag-slug` — filter by tag (AND when multiple)
- `##content-type` — filter by content type (OR when multiple)
- Example: `@ahmet #react #typescript ##guide nasıl yapılır`

**Search modes**:
- **Tag-only** (only `#` tags, no remaining text): Returns tag-browse results, paged
- **Fulltext**: PostgreSQL tsvector (`turkish` config) with `ts_rank_cd` ranking, published articles only. Multi-word queries require **all** terms (AND); if nothing matches, retries with any-term (OR), then falls back to ILIKE on title/excerpt
- **Semantic**: Ollama embeddings, cosine similarity
- **Hybrid**: Reciprocal Rank Fusion (fulltext + semantic)
- **RAG**: AI-generated answer with source citations

**Side effects**: Logs a `SearchQuery` record with query text, result count, response time, and search type (including zero-result tag searches).

**200 Response (non-RAG)**:
```json
{
  "results": [
    { "id": "...", "title": "...", "slug": "...", "excerpt": "...", "snippet": "…match context…", "contentType": "...", "updatedAt": "..." }
  ],
  "total": 137,
  "page": 1,
  "totalPages": 7,
  "searchType": "fulltext",
  "responseTimeMs": 12,
  "searchQueryId": "abc123..."
}
```
> `total` is the true post-filter match count for `fulltext`/tag searches (`totalPages = ceil(total/limit)`); for `semantic`/`hybrid` it is the returned top-N count (`page`/`totalPages` fixed at 1).
> `snippet` is a match-context window from the article body (query terms matched accent/case-insensitively, stem-prefix tolerant); `null` when the match is title-only — clients fall back to `excerpt`.
> `searchQueryId` is used to track which result was clicked via `POST /api/search/click`.

**200 Response (RAG)**:
```json
{
  "answer": "Doğrulanmış claim [S1]",
  "sources": [{ "articleId": "...", "title": "...", "slug": "...", "score": 0.95 }],
  "claims": [{ "text": "Doğrulanmış claim", "sourceIds": ["S1"] }],
  "evidence": [{ "sourceId": "S1", "articleId": "...", "passage": "..." }],
  "citationCoverage": 1.0,
  "claimSupportCoverage": 1.0,
  "groundingStatus": "lexically_grounded",
  "insufficientContext": false,
  "partialResult": false,
  "warnings": []
}
```

The free-form model answer is never returned independently. The API rebuilds `answer` only from
claims that pass known-evidence, lexical-overlap, numeric-consistency and negation-consistency
checks. Malformed structured output or a response with no supported claims fails closed as an
insufficient-context response. Capacity saturation returns **429**, an open AI circuit returns
**503**, and an exceeded stage/request deadline returns **504** with `Retry-After` where applicable.

---

### `GET /api/search/authors`
**Auth**: Bearer (JWT or API Key)

Returns list of all users for author autocomplete in search.

**200 Response**:
```json
[
  { "id": "...", "name": "Admin", "slug": "admin" }
]
```

---

### `POST /api/search/click`
**Auth**: Bearer (JWT or API Key)

| Field | Type | Required |
|-------|------|----------|
| `searchQueryId` | string | Yes |
| `articleId` | string | Yes |

**Side effects**: Updates the `SearchQuery` record's `clicked_article_id` field.
**200 Response**: `{ "message": "Click recorded" }`
**400**: Missing searchQueryId or articleId.
**404**: SearchQuery not found.

---

### `POST /api/search/reindex`
**Auth**: Bearer (session only, rejects API key)
**Permission**: `users:manage`

Marks all published articles stale and upserts durable PostgreSQL `index_jobs`. Existing embeddings remain searchable until each article is atomically replaced; workers rebuild FTS and semantic data with persisted retry state.

**200 Response**: `{ "message": "Reindex queued", "articlesQueued": 42 }`
**503**: Ollama not enabled.

---

### `GET /api/search/embedding-status`
**Auth**: Bearer (session only, rejects API key)
**Permission**: `users:manage`

**200 Response**:
```json
{
  "totalPublished": 42,
  "totalIndexed": 38,
  "pendingCount": 4,
  "failedCount": 1,
  "ollamaEnabled": true,
  "modelName": "bge-m3",
  "configuredDimensions": 1024,
  "failedArticles": [
    { "articleId": "...", "failureCount": 10, "nextRetryAt": "2026-07-17T12:02:00.000Z", "error": "..." }
  ]
}
```

### `GET /api/search/storage-status`
**Auth**: Bearer (session only) · **Permission**: `users:manage`

Returns local `data/uploads` bytes/free space, extraction backlog/failures, and a bounded checksum/missing-file sample.

---

## Dashboard

### `GET /api/dashboard`
**Auth**: Bearer

**200 Response**:
```json
{
  "totalArticles": 42,
  "viewsThisWeek": 156,
  "searchesToday": 23,
  "staleCount": 3,
  "recentArticles": [{ "id": "...", "title": "...", "slug": "...", "contentType": "..." }],
  "topSearches": [{ "query": "deployment", "count": 15 }]
}
```

---

## Analytics

### `GET /api/analytics`
**Auth**: Bearer (session only, rejects API key)
**Permission**: `analytics:view`

| Param | Type | Default | Constraints |
|-------|------|---------|-------------|
| `days` | int | 30 | Clamped to 1–365; usage aggregates use this calendar-day window |

**200 Response**:
```json
{
  "overview": {
    "totalArticles": 42,
    "articlesByStatus": { "draft": 9, "published": 30, "archived": 3 },
    "viewsThisWeek": 156,
    "searchesToday": 23,
    "staleArticles": 3
  },
  "topSearches": [{ "query": "...", "count": 15 }],
  "failedSearches": [{ "query": "...", "count": 8 }],
  "topArticles": [{ "articleId": "...", "title": "...", "slug": "...", "views": 42 }],
  "usage": {
    "periodDays": 30,
    "periodStart": "2026-07-19T00:00:00Z",
    "periodEnd": "2026-08-17T12:00:00Z",
    "totalRequests": 1200,
    "successfulRequests": 1176,
    "errors": 24,
    "errorRate": 0.02,
    "averageDurationMs": 84.5,
    "activeUsers": 18,
    "activeIntegrations": 4,
    "sessionRequests": 900,
    "integrationRequests": 300,
    "restRequests": 1050,
    "mcpCalls": 150,
    "daily": [{
      "date": "2026-08-17", "requests": 40, "errors": 1,
      "averageDurationMs": 75, "activeUsers": 9, "activeIntegrations": 2,
      "sessionRequests": 30, "integrationRequests": 10,
      "restRequests": 34, "mcpCalls": 6
    }],
    "users": [{
      "userId": "...", "name": "...", "email": "...", "role": "editor",
      "requests": 80, "sessionRequests": 60, "integrationRequests": 20,
      "restRequests": 72, "mcpCalls": 8, "readRequests": 70, "writeRequests": 10,
      "errors": 2, "errorRate": 0.025, "averageDurationMs": 70,
      "lastUsedAt": "...", "activeDays": 12, "integrationsUsed": 1,
      "topOperation": "GET api/articles", "topOperationRequests": 30
    }],
    "integrations": [{
      "apiKeyId": "...", "name": "CI", "ownerId": "...",
      "ownerName": "...", "ownerEmail": "...", "requests": 100,
      "restRequests": 40, "mcpCalls": 60, "readRequests": 95, "writeRequests": 5,
      "errors": 3, "errorRate": 0.03, "averageDurationMs": 90,
      "lastUsedAt": "...", "activeDays": 20,
      "topOperation": "mcp.search_articles", "topOperationRequests": 45
    }],
    "operations": [{
      "operation": "mcp.search_articles", "channel": "mcp", "requests": 60,
      "errors": 2, "errorRate": 0.0333, "averageDurationMs": 110,
      "lastUsedAt": "...", "uniqueUsers": 3, "uniqueIntegrations": 2
    }]
  }
}
```

`daily` always contains one row per calendar day (including zero-usage days). User totals include both their direct session traffic and traffic made by their API keys. Integration totals identify API keys and their owners. Read/write classification treats non-mutating REST methods and the current read-only MCP tools as reads.

**Timeframes**: Week = last 7 days, Day = last 24 hours, Stale = 90+ days since `last_reviewed_at`.

---

## Admin — User Management

### `GET /api/admin/users`
**Auth**: Bearer (session only)
**Permission**: `users:manage`

| Param | Type | Default |
|-------|------|---------|
| `q` | string | — |
| `page` | int | 1 |
| `limit` | int | 50 |

**200 Response**: `{ "users": [...], "total": 15 }`

---

### `POST /api/admin/users`
**Auth**: Bearer (session only)
**Permission**: `users:manage`

| Field | Type | Required | Constraints |
|-------|------|----------|-------------|
| `name` | string | Yes | — |
| `email` | string | Yes | Unique |
| `password` | string | Yes | 8–128 characters |
| `role` | string | No | Default: `"viewer"` |

**201 Response**: `{ "id", "name", "email", "role", "createdAt" }`

---

### `PUT /api/admin/users`
**Auth**: Bearer (session only)
**Permission**: `users:manage`

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `userId` | string | Yes | Target user ID |
| `name` | string | No | — |
| `email` | string | No | Unique if changed |
| `password` | string | No | 8–128 characters if provided |
| `role` | string | No | Cannot self-demote from admin |

**200 Response**: `{ "id", "name", "email", "role", "updatedAt" }`
**400**: Self-demotion attempt or email conflict.

---

### `DELETE /api/admin/users?id={id}`
**Auth**: Bearer (session only)
**Permission**: `users:manage`

**Guards**: Cannot delete self.
**200 Response**: `{ "message": "User deleted" }`

---

## API Keys

### `GET /api/keys`
**Auth**: Bearer (session only)
**Permission**: `api_keys:manage`

**200 Response**: Array of API keys (key hash NOT included).

---

### `POST /api/keys`
**Auth**: Bearer (session only)
**Permission**: `api_keys:manage`

| Field | Type | Required | Constraints |
|-------|------|----------|-------------|
| `name` | string | Yes | 1–100 chars |
| `expiresInDays` | int | No | 1–365 days, default 90 |

**201 Response**: `{ "id", "key": "kp_abc123...", "name", "expiresAt" }`
> The raw key (`kp_...`) is returned **only once** at creation time.

---

### `POST /api/keys/{id}/rotate`
**Auth**: Bearer (session only)
**Permission**: `api_keys:manage`

Generates a new key value for an existing API key. The old key is immediately invalidated. Expiration is reset to 90 days from rotation.

**200 Response**: `{ "id", "key", "name", "expiresAt" }`

> ⚠️ The `key` field is only returned once — store it securely.

---

### `DELETE /api/keys?id={id}`
**Auth**: Bearer (session only)
**Permission**: `api_keys:manage`

**200 Response**: `{ "message": "API key deleted" }`

---

## Admin API Keys

All-user API key management for admins. `api_keys:manage` is granted to every role (each user manages their own keys via `/api/keys`); `api_keys:manage_any` is admin-only.

### `GET /api/admin/keys`
**Auth**: Bearer (session only)
**Permission**: `api_keys:manage_any`

| Query Param | Type | Description |
|-------------|------|-------------|
| `q` | string | Search by key name, user name, or user email |
| `userId` | string | Filter to a single user's keys |
| `page` | int | Default 1 |
| `limit` | int | Default 50, max 100 |

**200 Response**: `{ "keys": [ { "id", "name", "keyPrefix", "userId", "userName", "userEmail", "lastUsedAt", "expiresAt", "createdAt" } ], "total" }`

---

### `POST /api/admin/keys`
**Auth**: Bearer (session only)
**Permission**: `api_keys:manage_any`

| Field | Type | Required | Constraints |
|-------|------|----------|-------------|
| `userId` | string | Yes | Must be an existing user |
| `name` | string | Yes | 1–100 chars |
| `expiresInDays` | int | No | 1–365 days, default 90 |

**201 Response**: `{ "id", "key": "kp_abc123...", "name", "keyPrefix", "userId", "userName", "userEmail", "expiresAt", "createdAt" }`
> The raw key (`kp_...`) is returned **only once** at creation time.

---

### `PUT /api/admin/keys`
**Auth**: Bearer (session only)
**Permission**: `api_keys:manage_any`

| Field | Type | Required | Constraints |
|-------|------|----------|-------------|
| `id` | string | Yes | Key id (in body, not URL) |
| `name` | string | No | 1–100 chars |
| `expiresInDays` | int | No | 1–365; resets expiry from now |

**200 Response**: Updated key summary (same shape as list items).

---

### `DELETE /api/admin/keys?id={id}`
**Auth**: Bearer (session only)
**Permission**: `api_keys:manage_any`

**200 Response**: `{ "message": "API key deleted" }`

---

## System Logs

### `GET /api/logs`
**Auth**: Bearer (session only)
**Permission**: `users:manage`

**200 Response**:
```json
{
  "files": [
    { "fileName": "log_20260708.log", "sizeBytes": 12345, "createdAt": "...", "lastModifiedAt": "...", "isToday": true, "canDelete": false }
  ],
  "total": 1
}
```

---

### `GET /api/logs/{fileName}`
**Auth**: Bearer (session only)
**Permission**: `users:manage`

| Query Param | Type | Default | Notes |
|-------------|------|---------|-------|
| `tail` | int? | — | Return only last N lines |

**200 Response**:
```json
{ "fileName": "log_20260708.log", "totalLines": 500, "returnedLines": 200, "content": "..." }
```

---

### `DELETE /api/logs/{fileName}`
**Auth**: Bearer (session only)
**Permission**: `users:manage`

> Cannot delete today's log file.

**200 Response**: `{ "message": "Log file 'log_20260707.log' deleted successfully" }`
**400 Response**: `{ "error": "Cannot delete today's log file" }`

---

## Error Response Format

All error responses follow the pattern:
```json
{ "error": "Human-readable error message" }
```

Standard HTTP status codes:
- `400` — Validation error
- `401` — Not authenticated
- `403` — Insufficient permissions
- `404` — Resource not found
- `500` — Internal server error
