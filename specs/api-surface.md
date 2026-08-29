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
| `search` | 30 requests | 1 minute | `GET /api/search`, `POST /api/assistant` |
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
- **Auth**: X-API-Key or Bearer token (required)
- **Transport**: Official `ModelContextProtocol.AspNetCore` v2 Streamable HTTP server (stateless, JSON-RPC 2.0)
- **Protocol Version**: preferred modern `2026-07-28` plus the initialize-capable legacy revisions negotiated by the official SDK. Modern clients use `server/discover`, per-request `_meta`, and the `Mcp-Method`/`Mcp-Name` routing headers.

POST requests require `Content-Type: application/json`; clients advertise both `application/json` and `text/event-stream` in `Accept`. The official SDK owns JSON-RPC parsing, protocol/version negotiation, capability discovery, routing-header validation, JSON/SSE response framing and notification semantics. Browser-originated requests are accepted only from the MCP endpoint's own host, and the portal retains a 256 KiB request ceiling.

Exposes Knowledge Portal tools via the Model Context Protocol. Cursor, VS Code Copilot, and SDK clients that can attach a static API-key/Bearer header can connect directly. Claude remote custom connectors require a publicly reachable endpoint and the documented OAuth connector flow, so they cannot connect directly to this API-key-only endpoint.

**Supported Methods**: `server/discover` (2026 era), `initialize` and `notifications/initialized` (2025 era), `tools/list`, `tools/call`, `ping`

**Available Tools**:
- `search_articles` — Document retrieval across published articles. Params: `query*`, `type` (`fulltext|semantic|hybrid`, default `fulltext`), `page`, `limit`, `scope`, `authors`, `include_content`, `include_attachments`, `only_own_content`. Supports `@author`, `#tag`, and generic `+category:value` inline syntax. It never generates an AI answer.
- `ask_knowledge` — Grounded AI-RAG answer from authorized portal evidence. Params: `question*`, `scope`, `authors`, `only_own_content`. Uses the same canonical answer pipeline as REST Assistant.
- `get_article` — Get article details by ID or slug (params: id_or_slug*)
- `list_articles` — List published articles with pagination (params: page, limit, scope, sort; legacy `content_type` and `tags` remain accepted; sort is validated against `newest|oldest|most_viewed`)
- `list_tags` — List all available tags with article counts
- `get_portal_info` — Portal statistics (counts, content type distribution, recent articles)
- `get_project_context` — Build a governed briefing from a required non-empty `scope` (legacy `project_tag` remains accepted)
- `get_integration_guidance` — Hybrid retrieval for an integration goal, optionally constrained by `scope`
- `find_authoritative_content` — Find decision sources, optionally constrained by `scope`, and expose governance-recommended source ordering
- `compare_sources` — Compare 2-10 published sources, optionally requiring each source to fall inside `scope`, with canonical content and governance; contradiction status remains explicit
- `get_recent_changes` — Recently updated published knowledge, optionally constrained by `scope`

The shared scope shape is `{ "tags": ["a", "x", "y"], "contentTypes": ["how-to", "adr"] }`. Tags are free semantic tag slugs; prefixes such as `project-` or `team-` are not required. Every supplied tag must match (AND), while any supplied content type may match (OR); the two dimensions combine with AND. Omitting scope performs general retrieval. Unknown tags or content types produce no matches and never broaden the result set. `search_articles`, `list_articles`, `get_project_context`, `get_integration_guidance`, `find_authoritative_content`, `compare_sources`, and `get_recent_changes` use this contract. Legacy `tags`, `content_type`, and `project_tag` values are merged and deduplicated into the same effective scope.

**Tool result format**: Every tool advertises an `outputSchema` and returns the machine-readable payload in `structuredContent`. For backwards compatibility, the same serialized JSON is also returned as `{ "content": [{ "type": "text", "text": "..." }] }`. Tool failures use `isError: true`.

Search results include `evidenceAvailable` and an `evidence[]` provenance array. Evidence contains the article ID/slug, canonical API URL, source type, matched passage when available, update timestamp, match type, and score. Title-only matches explicitly set `evidenceAvailable: false` rather than fabricating a passage. `ask_knowledge` returns cited/consulted sources, claims and evidence separately.

MCP search hits also include `governance`: optional approval state (`approved` or `not_recorded`), approver/time when recorded, review state (`current`, `due_soon`, `overdue`, `not_recorded`), next review date, dynamic content-type label, generic lookup authority weight, reliability score, and warnings. `authorityWeight` (0-100, default 50) is configurable on every dynamic lookup value; an article uses the highest weight among active assignments in active categories, with a legacy `content_type` fallback only when it has no generic assignments. No category or value name is hard-coded into authority ranking. Directly published/imported content remains available and is truthfully marked `not_recorded`. Search responses aggregate caution indicators and reliability-ordered `recommendedArticleIds` in `decisionSupport`. Ordinary list/search and source-comparison responses keep `conflictAssessment: not_evaluated`; only RAG answers run the conservative numeric/explicit-polarity conflict screen described below, and no endpoint claims general semantic contradiction detection.

MCP article/search/compare outputs include `securityAssessment` (`riskLevel`, explainable `signals`, `secretsRedacted`, `treatAsUntrustedData`, `allowAutomaticExecution=false`). Common portal keys, bearer tokens, JWTs, AWS access-key IDs, and assigned secret/token/password values are replaced with `[REDACTED_SECRET]` throughout structured and compatibility-text output. Injection signals are flagged, not silently deleted. RAG source blocks redact secrets, mark risky chunks as `SECURITY-RISK`, neutralize source delimiters, and explicitly forbid following source instructions, tool execution, URL visits, or credential disclosure.

Every `tools/call` response includes `X-Trace-Id`. A structured audit event records trace ID, tool, outcome, auth source, user/API-key identifiers, bounded client user-agent, protocol version, duration, serialized output size, and a privacy-preserving argument shape. Raw argument values, queries, article content, credentials, and reversible hashes are never written to the MCP audit event. Prometheus exports `kp_mcp_tool_calls`, `kp_mcp_tool_errors`, `kp_mcp_tool_duration_ms`, and `kp_mcp_tool_output_bytes`, tagged by bounded tool/outcome/auth dimensions.

MCP uses a fixed 256 KiB request-body ceiling. Configurable resilience limits under `Mcp` include the output default of 1 MiB, tool-specific budgets, a semantic-search lane and a separate grounded-answer lane with independent concurrency/circuit state. Resilience failures use structured tool errors with stable retry metadata. Client disconnects propagate cancellation and are audited as `cancelled`. These controls are intentionally instance-local for the supported single-backend topology.

**Client configuration example (VS Code)**:
```json
{
  "inputs": [
    {
      "type": "promptString",
      "id": "knowledge-portal-key",
      "description": "Knowledge Portal API key",
      "password": true
    }
  ],
  "servers": {
    "knowledge-portal": {
      "type": "http",
      "url": "http://localhost:5174/mcp",
      "headers": { "X-API-Key": "${input:knowledge-portal-key}" }
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
| `currentPassword` | string | No | Required for local users changing password; Azure-linked users may omit it whenever setting/replacing the local password |
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
| `includeContent` | bool | false | When true → includes canonical Markdown as the string field `contentMarkdown` |
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
      "indexingStatus": { "state": "indexed", "indexedAt": "2026-08-21T10:30:00Z" },
      "contentMarkdown": "## Canonical Markdown (only if includeContent=true)",
      "attachments": [{ "id": "...", "fileName": "...", "contentType": "...", "sizeBytes": 1024, "downloadUrl": "/api/attachments/.../download", "includeInIndex": true }]
    }
  ],
  "total": 42
}
```

For editor/admin callers, list items and article detail include `indexingStatus`. Its `state` is
`indexed`, `indexing`, `pending`, `stale`, `failed`, or `not_applicable`; `indexedAt` is populated
only when the current article revision is fully synchronized. Viewer responses omit this
operational field.

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
| `contentType` | string | No | Active DB lookup value; default: `"reference"` |
| `tags` | string[] | No | Array of tag ID, name, or slug (resolved in that priority) |
| `reviewIntervalDays` | int | No | 1–3650, default 90 |

**Side effects**: Creates version 1, durably queues semantic/index recovery, and best-effort refreshes PostgreSQL FTS before returning. The worker still revalidates FTS before asynchronous embedding.
**201 Response**: `{ "id", "slug", "title" }`

---

### `GET /api/articles/{idOrSlug}`
**Auth**: Bearer (JWT or API Key)

Accepts both article ID and slug for lookup.

**Side effects**: Records an `ArticleView` entry (deduplicated: same user+article within 15 minutes counts as 1 view).
**200 Response**: Full article object with canonical `contentMarkdown`, derived `contentText`, `reviewIntervalDays`, and attachment metadata. Editor/admin responses also include the revision-aware `indexingStatus` described above.
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
| `contentType` | string | No | Must be an active DB lookup value |
| `changeSummary` | string | No | Stored in version record |
| `tags` | string[] | No | Array of tag ID, name, or slug (replaces all existing tags) |
| `reviewIntervalDays` | int | No | 1–3650; per-article governance review interval |

**Side effects**: If content changes, creates a new `ArticleVersion` with incremented version number. Every mutation durably queues index recovery and best-effort refreshes PostgreSQL FTS before returning; semantic embedding remains asynchronous.
**200 Response**: `{ "id", "slug", "title" }`
**403**: Not owner and lacks `articles:edit_any`, OR lacks `articles:publish`/`articles:archive` for status change.

---

### `DELETE /api/articles/{id}`
**Auth**: Bearer JWT session only
**Permission**: `articles:delete_any` (admin only)

**200 Response**: `{ "message": "Article deleted" }`
**403**: Caller is not an admin session.

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
    { "id": "...", "fileName": "diagram.png", "contentType": "image/png", "sizeBytes": 102400, "downloadUrl": "/api/attachments/.../download", "includeInIndex": true, "extractionStatus": "completed", "extractionTruncated": false, "extractedCharacters": 846, "extractionCharacterLimit": 50000, "createdAt": "2026-01-01T00:00:00Z" }
  ],
  "total": 1
}
```

### `POST /api/articles/{id}/attachments`
**Auth**: Bearer (JWT or API Key) — requires `articles:edit_own` (if owner) or `articles:edit_any`
**Content-Type**: `multipart/form-data`
**Body**: `file` (IFormFile, max 20MB, extension whitelist enforced), `includeInIndex` (boolean, optional, default `true`). When false, the file remains downloadable but contributes no FTS, semantic-search, or RAG source content.

**201 Response**:
```json
{ "id": "...", "fileName": "diagram.png", "contentType": "image/png", "sizeBytes": 102400, "downloadUrl": "/api/attachments/.../download", "includeInIndex": true, "extractionStatus": "pending", "extractionTruncated": false, "extractedCharacters": 0, "extractionCharacterLimit": 50000, "createdAt": "2026-01-01T00:00:00Z" }
```
**400**: Empty file, invalid extension, MIME mismatch, max attachments reached.
**403**: No edit permission.

Attachment extraction runs in the durable indexing job for attachments whose `includeInIndex` flag is true. PDF/DOCX/PPTX/XLSX/CSV tables are retained as GFM Markdown with page, slide, or sheet provenance; image attachments and supported embedded visuals are described and OCR'd by the configured local vision model. The resulting canonical extraction is shared by full-text and semantic indexing. `DocumentParsing:External` can optionally target an Unstructured-compatible `hi_res` partition endpoint for complex or scanned layouts; native parsing remains the default fallback unless `Required` is enabled. Parser, vision-model, extraction-setting, or index-inclusion-policy changes alter the index profile, so affected attachments are refreshed during the next repair/reindex cycle instead of reusing stale content.

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

Article create/update requests may include a new tag name in their `tags` array. Any caller authorized for that article mutation, including viewer sessions, may create and attach the tag in this context. The tag is created and linked atomically when the article is saved; merely entering it in the editor does not persist it. Standalone tag creation, rename, and deletion through `/api/tags` still require `tags:manage`.

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
| `q` | string | — | Required. Inline syntax: `@user-slug` (author), `#tag-slug` (tag), `+category:value` (dynamic lookup) |
| `type` | string | `"fulltext"` | `fulltext`, `semantic`, `hybrid` |
| `limit` | int | 20 | Max results per page (1–50) |
| `page` | int | 1 | Page number (min 1). Applies to `fulltext` and tag-browse; `semantic`/`hybrid` are top-N only |
| `onlyOwnContent` | bool | false | Optional. When true + API key auth → filters to articles created by that API key |
| `includeContent` | bool | false | Optional. When true → includes canonical Markdown as the string field `contentMarkdown` |
| `includeAttachments` | bool | false | Optional. When true → includes attachment metadata (id, fileName, contentType, sizeBytes, downloadUrl, includeInIndex) per article |
| `tag` | string[] | — | Optional, repeatable. Tag slugs (merged with #syntax) |
| `author` | string[] | — | Optional, repeatable. User slugs (merged with @syntax) |
| `contentType` | string[] | — | Optional, repeatable legacy content type values |
| `facet` | string[] | — | Optional, repeatable generic `category:value` filters; merged with inline `+category:value` |

**Inline query syntax**:
- `@user-slug` — filter by author (OR when multiple)
- `#tag-slug` — filter by tag (AND when multiple)
- `+category:value` — filter by any active dynamic lookup category (OR within one category, AND across categories)
- Example: `@ahmet #react +department:finance +content_type:guide nasıl yapılır`

**Search modes**:
- **Tag-only** (only `#` tags, no remaining text): Returns tag-browse results, paged
- **Fulltext**: PostgreSQL tsvector (`turkish` config) with `ts_rank_cd` ranking, published articles only. Multi-word queries require **all** terms (AND); if nothing matches, retries with any-term (OR), then falls back to ILIKE on title/excerpt
- **Semantic**: Ollama embeddings, cosine similarity
- **Hybrid**: Reciprocal Rank Fusion (fulltext + semantic)

**Side effects**: Logs a `SearchQuery` record with query text, result count, response time, and search type (including zero-result tag searches).

**200 Response**:
```json
{
  "results": [
    { "id": "...", "title": "...", "slug": "...", "excerpt": "...", "snippet": "…match context…", "contentType": "...", "updatedAt": "..." }
  ],
  "total": 137,
  "page": 1,
  "totalPages": 7,
  "searchType": "fulltext",
  "indexingPending": false,
  "indexCoverage": {
    "mode": "fulltext",
    "fullTextPending": 0,
    "semanticPending": 3,
    "relevantPending": 0
  },
  "responseTimeMs": 12,
  "searchQueryId": "abc123..."
}
```
> `total` is the true post-filter match count for `fulltext`/tag searches (`totalPages = ceil(total/limit)`); for `semantic`/`hybrid` it is the returned top-N count (`page`/`totalPages` fixed at 1).
> `snippet` is a match-context window from the article body (query terms matched accent/case-insensitively, stem-prefix tolerant); `null` when the match is title-only — clients fall back to `excerpt`.
> `searchQueryId` is used to track which result was clicked via `POST /api/search/click`.
> `indexingPending` is mode-aware and filter-scoped. Fulltext checks only `FtsIndexedAt`, semantic
> checks only `IndexedAt`, and hybrid/RAG check both. `indexCoverage` reports the separate pending
> counts plus a distinct `relevantPending` count for the requested mode; unrelated articles outside
> the active author/tag/content-type/API-key filters do not trigger the warning.

## Bilgi Asistanı RAG Yanıt Sözleşmesi

Bu yanıt şekli `POST /api/assistant`, `POST /api/assistant/stream` içindeki `complete` olayı ve MCP `ask_knowledge` tarafından kullanılır. `GET /api/search` bu sözleşmeyi döndürmez.

**Grounded RAG payload**:
```json
{
  "answer": "Doğrulanmış kısa sonuç [S1]\n\n**Açıklama**\n\n- Destekli açıklama [S2]",
  "sources": [{ "articleId": "article-1", "title": "...", "slug": "...", "score": 0.95, "authorityWeight": 90, "approved": true, "reviewState": "current", "reliabilityScore": 95, "updatedAt": "..." }],
  "consultedSources": [{ "articleId": "article-1", "title": "...", "slug": "...", "score": 0.95, "authorityWeight": 90, "approved": true, "reviewState": "current", "reliabilityScore": 95, "updatedAt": "..." }],
  "claims": [
    { "text": "Doğrulanmış kısa sonuç", "role": "summary", "sourceIds": ["S1"] },
    { "text": "Destekli açıklama", "role": "explanation", "sourceIds": ["S2"] }
  ],
  "evidence": [
    { "sourceId": "S1", "chunkId": "chunk-1", "articleId": "article-1", "canonicalUrl": "/api/articles/...", "pageNumber": 12, "passage": "..." },
    { "sourceId": "S2", "chunkId": "chunk-2", "articleId": "article-1", "canonicalUrl": "/api/articles/...", "pageNumber": 13, "passage": "..." }
  ],
  "citationCoverage": 1.0,
  "claimSupportCoverage": 1.0,
  "groundingStatus": "lexically_grounded",
  "insufficientContext": false,
  "partialResult": false,
  "conflictAssessment": { "status": "none_detected", "conflicts": [] },
  "warnings": []
}
```

`sources` contains only articles cited by claims that survived grounding. `consultedSources` contains
every article whose passage was supplied to generation, including relevant sources not used in the
final answer. `sourceId` is the request-local identifier supplied to the model. `chunkId` is the stable stored
embedding ID; a lexical-only fallback passage receives a deterministic `lex_...` ID derived from
its provenance and content. `canonicalUrl` points to the normal authenticated article endpoint,
and `pageNumber` is present only when PDF page provenance is available.

The chat request supplies an explicit JSON schema requiring `answer`, `claims` and
`insufficientContext`. A short keyword, product name, configuration key, or heading-like fragment is
treated as an implicit request to explain that topic from all directly relevant evidence, including
supported purpose, behavior, defaults, limits, and fallbacks. The free-form model answer is never
returned independently. The API rebuilds `answer` only from claims that pass known-evidence,
lexical-overlap, numeric-consistency and negation-consistency checks; repeating a document title as a
standalone claim is rejected. The generation contract requires a compact synthesis instead of a
source-by-source result recap: the first validated claim is the direct answer and later claims explain
supported behavior, reasons, practical meaning, steps, constraints, exceptions, and trade-offs. Each
claim has one required role: `summary`, `explanation`, `step`, `constraint`, `exception`, or `conflict`.
The API renders localized Markdown sections from those roles and normalizes legacy/fallback claims so
the first claim is always `summary`.
This presentation adds no model prose; it is built exclusively from grounded claims. For a bare-topic query, punctuation does not affect classification: the
first supported claim is retained as the summary paragraph. The same rule applies to direct definition
questions whose subject is a colon-delimited configuration path: compact source-native configuration
descriptions are accepted as definitions even when they do not contain a grammatical `is`/`-dır`
predicate. When the supplied evidence contains
multiple distinct relevant facts, at least one additional supported explanatory claim must follow in a
new paragraph. A single-claim answer is rejected only in that evidence-rich case. Rejected generation receives one bounded, evidence-bound grounding-repair attempt before the extractive
fallback is considered. Support is evaluated independently against local sentence and
contrast-separated clause windows in each cited evidence item; cited chunks are not concatenated,
so unrelated positive or negative statements cannot change a claim's polarity result. A complete contract-compliant JSON object may be recovered from a
model-added code fence or text wrapper; that wrapper is ignored and never shown. If a provider
ignores structured-output mode but returns prose with exact `[S1]`-style citations, the server
deterministically converts only those cited passages into claims and runs the same validation; uncited
prose is never recovered or displayed. Malformed structured output without recoverable cited claims,
missing required fields, or a response with no supported claims triggers a bounded extractive
fallback: query-overlapping sentences are selected directly from verified evidence, passed through
secret/instruction safety filtering, returned verbatim with their known evidence IDs and
`groundingStatus: "extractive_fallback"`, and marked as a partial result. Because fallback claims are
verbatim evidence sentences, lexical, numeric and negation support hold by construction. If no safe
relevant evidence sentence exists, the request fails closed as an
insufficient-context response. Capacity saturation returns **429**, an open AI circuit returns
**503**, and an exceeded stage/request deadline returns **504** with `Retry-After` where applicable.

The focused single-pass path adaptively selects between `Ollama:RagMinimumSourceLimit` (default 3) and
`Ollama:RagSourceLimit` (default 10, clamped to 1-20). Query token complexity, decomposition and
explanation intent increase breadth; sources below `RagSourceRelativeScoreFloor` (default 55% of the
best article score) are excluded after the minimum safety floor. Article-interleaved retrieval places
the best passage from each source before deeper passages. Context uses `RagMaxContextTokens` (default
12,000), bounded by the configured model window after output and system-prompt reserves. A conservative
Qwen/Unicode estimator performs preflight truncation and calibrates future estimates from Ollama's exact
post-response `InputTokenCount`. The context builder reserves an equal first-pass token share per
selected article, preventing a whole-article lexical fallback passage from monopolizing the prompt.
Corpus-wide summary/compare/list intents still use the broad map-reduce path and completeness gate.

Every source block contains trusted approval, dynamic authority, review, reliability and update metadata.
When facts conflict, precedence is approval → authority → review state → reliability → update time; tied
signals remain unresolved. `conflictAssessment` conservatively reports only deterministic numeric or
explicit polarity conflicts and never claims general semantic contradiction detection. Competing facts
are emitted as separate `conflict` claims so each remains independently groundable.

If a bare-topic/configuration definition produces a fully supported summary but both the initial and
repair calls omit the required explanation, the server preserves that summary and appends up to three
query-relevant, security-screened sentences from verified evidence as a second paragraph. This is
returned with `groundingStatus: "extractive_enrichment"`, `partialResult: true`, and an explicit warning;
unsupported model prose is never used for the enrichment. Definition question words such as `nedir`
and `what is` are excluded from relevance scoring; question-shaped headings and unrelated document
titles cannot become explanation claims. A follow-up configuration explanation must either mention
the requested subject or continue from the same evidence item as the validated definition.

For a colon-delimited configuration query whose model claims all fail grounding, the fallback can
recover the exact configuration entry from a flattened evidence chunk up to the next configuration
key and combine it with short, query-overlapping verified evidence sentences. The entry is shown as
the first paragraph and explanatory passages as the second; this remains an
`extractive_fallback`/partial result and never treats the mere existence of unrelated retrieval hits as
sufficient evidence.

Broad summary queries use a configurable completeness gate (`Ollama:RagBroadMinimumClaims`, default
6). When map-reduce leaves fewer supported claims than the available-evidence target, or fewer than
75% of attempted claims survive grounding, the server performs one bounded evidence-bound repair.
Supported, distinct claims from the reduce and repair passes are merged. If the merged answer is
still below the target, query-relevant verified evidence sentences complete the response as an
`extractive_enrichment` partial result; unrelated retrieval hits are not added merely to reach a count.

---

## Bilgi Asistanı

### `POST /api/assistant`
**Auth**: Bearer (JWT or API Key).
**Rate limit**: `assistant`

The Assistant has one purpose: produce a grounded answer from authorized portal evidence. It does not return document-search result lists, route to analytics/general chat, execute mutations, or run free-form SQL. `KnowledgeAnswerService` is the canonical RAG entry point shared with MCP `ask_knowledge`; `GET /api/search` remains a separate document-retrieval API.

**Request**:

```json
{
  "message": "VPN politikasının istisnaları nelerdir?",
  "conversationId": null,
  "onlyOwnContent": false,
  "tags": ["security"],
  "authors": [],
  "contentTypes": ["policy"]
}
```

| Field | Type | Required | Notes |
|-------|------|:--------:|-------|
| `message` | string | Yes | Trimmed, 1–4,000 characters by default (`Assistant:MaxMessageCharacters`) |
| `conversationId` | string | No | Owned session conversation; API keys cannot use history. Bounded recent user/assistant turns rewrite follow-ups into standalone queries. Optional HyDE is retrieval-only and model failure falls back deterministically. |
| `onlyOwnContent` | bool | No | With API-key auth, restricts evidence to content created by that key. |
| `tags` | string[] | No | Tag slugs, AND semantics; merged with inline `#` filters. |
| `authors` | string[] | No | Author slugs, OR semantics; merged with inline `@` filters. |
| `contentTypes` | string[] | No | Legacy content-type values, OR semantics. Generic query filtering uses `facets` or inline `+content_type:value`. |

Inline and explicit filters use the same `KnowledgeQueryScopeService` as Search, while execution remains distinct. If RAG is disabled, saturated, circuit-open, or timed out, the endpoint returns a bounded error and never silently falls back to a search result list or ungrounded chat answer.

**200 Response**:

```json
{
  "normalizedQuery": "VPN politikasının istisnaları nelerdir?",
  "answer": "Portal kaynaklarına dayalı doğrulanmış yanıt [S1]",
  "rag": {
    "sources": [{ "articleId": "...", "title": "VPN Politikası", "slug": "vpn-politikasi", "score": 0.95 }],
    "consultedSources": [],
    "claims": [{ "text": "...", "role": "summary", "sourceIds": ["S1"] }],
    "evidence": [{ "sourceId": "S1", "articleId": "...", "passage": "..." }],
    "citationCoverage": 1,
    "claimSupportCoverage": 1,
    "groundingStatus": "lexically_grounded",
    "insufficientContext": false,
    "partialResult": false
  },
  "toolCalls": ["knowledge_rag"],
  "warnings": [],
  "interactionId": "...",
  "responseTimeMs": 640,
  "traceId": "...",
  "conversationId": null,
  "cacheHit": false
}
```

Operational controls:

- `Assistant:Enabled=false`: Assistant returns `404`; Search remains available. `VITE_ASSISTANT_ENABLED=false` removes the frontend route/menu.
- `Assistant:TotalTimeoutSeconds` bounds the end-to-end operation.
- RAG and semantic Search use separate resilience lanes.
- Usage operations are `assistant.answer` and `assistant.stream.answer`.

Errors use `{ "error": "..." }`: `400` for invalid input, `404` when the Assistant kill switch is off, `429` for capacity saturation, `503` for unavailable/open-circuit AI, and `504` for a deadline.

### `POST /api/assistant/feedback`
**Auth**: Bearer (JWT or API Key). Feedback is accepted only for an interaction owned by the current principal's user.
**Rate limit**: `assistant`

```json
{
  "interactionId": "...",
  "helpful": false,
  "reason": "wrong_source"
}
```

Allowed reasons are `incorrect`, `incomplete`, `wrong_source`, `outdated`, `no_answer`, and `other`. Audit/feedback persistence stores SHA-256 query/answer fingerprints and RAG/tool/timing metadata, never raw query or answer content. Assistant feedback is stored only on the owned `AssistantInteraction`, not on `SearchQuery`.

### `POST /api/assistant/source-click`
**Auth**: Bearer (JWT or API Key). Records an article-source click only when the interaction belongs to the caller and the article was cited or consulted by that answer.

### `GET /api/capabilities`
**Auth**: Bearer (JWT or API Key).

Returns runtime enablement, grounded-RAG, feedback, maximum-message, streaming, conversation, and semantic-cache capabilities. The frontend combines this response with `VITE_ASSISTANT_ENABLED`.

### `POST /api/assistant/stream`
**Auth/policy/rate limit**: Same as `POST /api/assistant`.

Returns `text/event-stream` events: `status`, `metadata`, zero or more `token`, then `complete`; validation failures use an SSE `error` event. Grounding remains fail-closed: raw model tokens are buffered and validated, then only the verified final answer is emitted as token chunks. `X-Accel-Buffering: no` and `no-transform` are set to prevent reverse-proxy buffering.

### Assistant conversations

`GET/POST/DELETE /api/assistant/conversations`, `GET /api/assistant/conversations/{id}/messages`, and `DELETE /api/assistant/conversations/{id}` require an interactive session and enforce user ownership. Create returns a new conversation; list returns at most 100 recent items; delete-one and clear-all are recoverable only from database backup. Retention is configured by `Assistant:ConversationRetentionDays`.

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

### `POST /api/search/repair-indexing`
**Auth**: Bearer (session only, rejects API key)
**Permission**: `users:manage`

Repairs only published articles whose full-text or semantic index marker is missing. Missing jobs are created; completed, failed, delayed-retry and lease-expired jobs are made immediately available. Healthy articles and actively leased jobs are not changed, and existing index markers/embeddings are not invalidated.

**200 Response**: `{ "message": "Missing or stuck index jobs repaired", "articlesRepaired": 10, "pendingCount": 10 }`

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
  "chunkingVersion": "hierarchical-parent-child-v2",
  "semanticIndexProfile": "9f0c...",
  "failedArticles": [
    { "articleId": "...", "failureCount": 10, "nextRetryAt": "2026-07-17T12:02:00.000Z", "error": "..." }
  ]
}
```

### `GET /api/search/storage-status`
**Auth**: Bearer (session only) · **Permission**: `users:manage`

Returns local `data/uploads` bytes/free space, extraction backlog/failures/truncation count, and a bounded checksum/missing-file sample. `truncatedExtraction` identifies attachments whose searchable text reached `FileStorage:MaxExtractedCharacters`.

### `GET /api/admin/rag/observability`

Session-admin-only runtime snapshot for the Assistant/MCP RAG pipeline, resilience state, model/profile versions, active requests and recent aggregate health.

### `GET /api/admin/rag/debug?q=...`

Session-admin-only diagnostic path. Runs query understanding, hybrid/multi-query child retrieval, reranking, ACL recheck, selective child-to-parent resolution, deduplication and context budgeting without calling the chat model. Returns the rewritten/decomposed query plan, extracted filters, only post-authorization candidates, expanded parent count/locations, and exact bounded context/evidence mapping.

### `GET /api/admin/rag-evaluations/feedback-summary?days=30`

Session-admin-only production-quality summary: helpful rate, negative-reason distribution, grounding-status helpful rates, latency, and prompt/retrieval/reranker/index-profile cohorts.

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

### `PUT /api/keys`
**Auth**: Bearer (session only)
**Permission**: `api_keys:manage`

Body: `{ "id": "...", "name": "optional", "expiresInDays": 90 }`. The key must belong to the caller. Returns the updated key summary without raw key material.

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

## Bulk Transfer and Source Import

All endpoints require authentication. Bulk/source commit operations use the same `ArticleMutationService` invariants as ordinary article writes: active DB-driven content types, status/archive authorization, canonical Markdown storage, tags, versions and durable reindexing.

| Endpoint | Method | Permission | Purpose |
|----------|--------|------------|---------|
| `/api/bulk/templates/{format}` | GET | — | Download `md`, `jsonl`, or `csv` import template |
| `/api/bulk/import-schema` | GET | — | Return limits, active content types, statuses, fields, formats and conflict policies |
| `/api/bulk/import` | POST multipart | `articles:create` | Validate or import up to 5,000 records; `dryRun` and `conflictPolicy=skip|update|duplicate` |
| `/api/bulk/export` | GET | — | Export visible articles as JSONL, CSV or Markdown ZIP with optional filters |
| `/api/source-imports/analyze` | POST multipart | `articles:create` | Convert supported source files to editable Markdown previews |
| `/api/source-imports/commit` | POST multipart | `articles:create` | Create articles from the approved preview manifest and optionally retain originals as attachments |

Bulk files carry `contentMarkdown` as a string. Attachments are not embedded in bulk exports. Source import supports text/Markdown, CSV/TSV, JSON/YAML, PDF, DOCX, XLSX and PPTX conversion; PDF and Office previews reuse the production structure-aware attachment extractor, including GFM table and page/sheet/slide provenance, so previewed Markdown and later retrieval do not diverge. Unsupported but valid files can be offered as attachments. Analyze responses retain one draft per source without aborting the remaining files. A recoverable conversion condition is returned in the draft's `warning`; a damaged or unreadable source is returned in `analysisError`. Every preview, including failed analyses, exposes the Markdown editor, the original-file attachment option, an explicit original-file index-inclusion checkbox, and a draft-specific additional-attachment picker whose files each have their own inclusion checkbox. The commit manifest maps each draft to its files through `additionalAttachmentIndexes`; multipart `attachments` carries the corresponding files and `additionalAttachmentIncludeInIndex` carries the aligned flags. `originalIncludeInIndex` defaults to false because a parsed original is already represented by `contentMarkdown`; the review UI initially enables it for unparsed originals, and the user can override both original and supporting-file choices. Original and additional files share the configured size, extension and maximum-per-article limits and are committed atomically with their article. The review UI blocks commit only while an `analysisError` draft has no manual content; entering content or removing every unresolved failed draft enables commit. Commit response items include `sourceIndex`, `fileName`, article identity/title fields, and a file-specific `error` when that draft fails.

---

## Lookups and Featured Links

| Endpoint | Method | Permission | Session-only |
|----------|--------|------------|:------------:|
| `/api/lookups` | GET | — | No |
| `/api/lookups` | POST/PUT/DELETE | `tags:manage` | DELETE only |
| `/api/lookups/categories` | GET | — | No |
| `/api/lookups/categories` | POST/PUT/DELETE | `tags:manage` | DELETE only |
| `/api/featured-links` | GET | — | No |
| `/api/featured-links` | POST/PUT/DELETE | `featured_links:manage` | DELETE only |

Content-type selectors expose active `lookup_values(category="content_type")` entries while the seeded category is active. Create, update, bulk import and source import validate explicit legacy values against that active category and mirror them to generic assignments. Omitted classifications use the category's configured active default on create; `reference` is only the initial seed default. If the category is deactivated or removed, the legacy column remains available for backwards compatibility without recreating the category at startup.

Generic category definitions expose stable `key`, `label`, `single|multiple` cardinality, required/default behavior, active state, editable display order, and `none|filter` RAG behavior (default `filter`). Values also expose editable display order. Classification inputs use canonical lookup values. Article create/update accepts `classifications: { "department": ["finance"] }`; article summaries/details return the same canonical map. Repeatable REST `facet=department:finance` is supported by article lists, bulk exports and Search; inline `+department:finance`, Assistant `facets`, and MCP `scope.facets[]` use the same OR-within/AND-across semantics. Unknown/inactive category or value filters fail closed.

Bulk JSONL and Markdown carry `classifications` as an object. CSV carries the same object as JSON in a `classifications` column. Import validation requires canonical values and enforces category cardinality/required/default rules; exports preserve canonical assignments.

---

## RAG Evaluation Administration

All endpoints below require an admin JWT session and `users:manage`:

- `GET/POST /api/admin/rag-evaluations/datasets`
- `GET/PUT/DELETE /api/admin/rag-evaluations/datasets/{id}`
- `POST /api/admin/rag-evaluations/datasets/{id}/runs`
- `GET /api/admin/rag-evaluations/runs`
- `GET /api/admin/rag-evaluations/runs/{runId}`
- `GET /api/admin/rag-evaluations/feedback-summary?days=30`

`GET /api/search/diagnostics` is likewise admin-session-only and returns coverage, index validity/size, query-plan probes, traffic percentiles, effective settings and actionable warnings.

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
