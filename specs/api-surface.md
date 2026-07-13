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

When rate limit is exceeded, returns `429 Too Many Requests`.

---

## Health Check

### `GET /api/health`
**Auth**: None

**200 Response**:
```json
{ "status": "healthy", "timestamp": "2026-01-01T00:00:00.0000000Z" }
```

---

## MCP (Model Context Protocol)

### `POST /mcp`
**Auth**: X-API-Key or Bearer token (required)  
**Transport**: Streamable HTTP (stateless, JSON-RPC 2.0)  
**Protocol Version**: 2024-11-05

Exposes Knowledge Portal tools via the Model Context Protocol. AI tools (Claude Desktop, Cursor, VS Code Copilot) can connect to this endpoint to search articles, get article content, list tags, and retrieve portal statistics.

**Supported Methods**: `initialize`, `notifications/initialized`, `tools/list`, `tools/call`, `ping`

**Available Tools**:
- `search_articles` — Full-text search across published articles (params: query*, limit, tags, authors, content_type, include_content)
- `get_article` — Get article details by ID or slug (params: id_or_slug*)
- `list_articles` — List published articles with pagination (params: page, limit, content_type, tags, sort)
- `list_tags` — List all available tags with article counts
- `get_portal_info` — Portal statistics (counts, content type distribution, recent articles)

**Tool result format**: `{ "content": [{ "type": "text", "text": "..." }] }` (with optional `isError: true`)

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

Returns server transport info for MCP client discovery.

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
| `content` | object | No | TipTap JSON document |
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
**200 Response**: Full article object with deserialized `content` (TipTap JSON).
**404**: Article not found.

---

### `PUT /api/articles/{id}`
**Auth**: Bearer (JWT or API Key)
**Permission**: Owner of the article, OR `articles:edit_any`. Additionally: `articles:publish` required to set status→published, `articles:archive` required to set status→archived.

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `title` | string | No | Slug regenerated if title changes |
| `content` | object | No | TipTap JSON |
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

Approves a pending article and sets its status to `published`.

**Guards**: Article must be in `pending` status.
**200 Response**: `{ "message": "Article approved and published", "id": "...", "slug": "..." }`
**400**: Article is not in pending status.

---

### `POST /api/articles/{id}/reject`
**Auth**: Bearer (JWT or API Key)
**Permission**: `articles:approve`

Rejects a pending article and returns it to `draft` status.

**Guards**: Article must be in `pending` status.
**200 Response**: `{ "message": "Article rejected and returned to draft", "id": "...", "slug": "..." }`
**400**: Article is not in pending status.

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
**Auth**: Bearer (JWT or API Key) — requires `articles:edit_own` (if owner) or `articles:edit_any`

**200 Response**: `{ "message": "Attachment deleted" }`
**403**: No edit permission.
**404**: Article or attachment not found.

### `GET /api/attachments/{id}/download`
**Auth**: Bearer (JWT or API Key)

Returns the file as a binary stream with appropriate `Content-Type` and `Content-Disposition` headers.
**404**: Attachment not found or file missing from disk.

---

## Tags

### `GET /api/tags`
**Auth**: Bearer (JWT or API Key)

**200 Response**:
```json
[
  { "id": "...", "name": "getting started", "slug": "getting-started", "articleCount": 5 }
]
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

**200 Response**: Version object with deserialized `content` (TipTap JSON).

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
| `limit` | int | 20 | Max results (1–50) |
| `onlyOwnContent` | bool | false | Optional. When true + API key auth → filters to articles created by that API key |
| `includeContent` | bool | false | Optional. When true → includes article content as plain text (extracted from TipTap JSON) in search results |
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
- **Tag-only** (only `#` tags, no remaining text): Returns tag-browse results
- **Fulltext**: FTS5 with BM25 ranking (fallback to LIKE), published articles only
- **Semantic**: Ollama embeddings, cosine similarity
- **Hybrid**: Reciprocal Rank Fusion (FTS5 + semantic)
- **RAG**: AI-generated answer with source citations

**Side effects**: Logs a `SearchQuery` record with query text, result count, response time, and search type.

**200 Response (non-RAG)**:
```json
{
  "results": [
    { "id": "...", "title": "...", "slug": "...", "excerpt": "...", "contentType": "...", "updatedAt": "..." }
  ],
  "total": 5,
  "searchType": "fulltext",
  "responseTimeMs": 12,
  "searchQueryId": "abc123..."
}
```
> `searchQueryId` is used to track which result was clicked via `POST /api/search/click`.

**200 Response (RAG)**:
```json
{
  "answer": "...",
  "sources": [{ "articleId": "...", "text": "...", "score": 0.95 }]
}
```

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

Marks all published articles for re-embedding by clearing `IndexedAt` and deleting all existing embeddings. The background service will re-process them.

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
  "ollamaEnabled": true,
  "modelName": "nomic-embed-text"
}
```

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

**200 Response**:
```json
{
  "overview": {
    "totalArticles": 42,
    "articlesByStatus": { "draft": 5, "published": 30, "pending": 4, "archived": 3 },
    "viewsThisWeek": 156,
    "searchesToday": 23,
    "staleArticles": 3
  },
  "topSearches": [{ "query": "...", "count": 15 }],
  "failedSearches": [{ "query": "...", "count": 8 }],
  "topArticles": [{ "articleId": "...", "title": "...", "slug": "...", "views": 42 }]
}
```

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
