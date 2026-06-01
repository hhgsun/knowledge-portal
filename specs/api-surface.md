# API Surface

Base URL: `http://localhost:5174/api`
All endpoints return JSON. All timestamps are ISO 8601 UTC.

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

### `GET /api/auth/me`
**Auth**: Bearer JWT

**200 Response**: `{ "id", "name", "email", "role", "avatar" }`
**401**: Invalid/expired token.

---

## Articles

### `GET /api/articles`
**Auth**: Bearer (JWT or API Key)

| Param | Type | Default | Notes |
|-------|------|---------|-------|
| `page` | int | 1 | Pagination |
| `limit` | int | 20 | Max results per page |
| `status` | string | — | Filter by status |
| `q` | string | — | Search title (LIKE) |

**Visibility rules**:
- Viewers see only `published` articles
- Editors/admins see all statuses (filtered if `status` param provided)

**200 Response**:
```json
{
  "articles": [
    {
      "id": "...", "title": "...", "slug": "...", "excerpt": "...",
      "status": "published", "contentType": "reference", "difficulty": "beginner",
      "updatedAt": "...", "ownerName": "...", "apiKeyName": null
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
| `difficulty` | string | No | Default: `"beginner"` |
| `audience` | string | No | — |
| `tags` | string[] | No | Array of tag IDs |

**Side effects**: Creates version 1 with the initial content.
**201 Response**: `{ "id", "slug", "title" }`

---

### `GET /api/articles/{idOrSlug}`
**Auth**: Bearer (JWT or API Key)

**Side effects**: Records an `ArticleView` entry.
**200 Response**: Full article object with deserialized `content` (TipTap JSON).
**404**: Article not found.

---

### `PUT /api/articles/{id}`
**Auth**: Bearer (JWT or API Key)
**Permission**: Owner of the article, OR `articles:edit_any`

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| `title` | string | No | Slug regenerated if title changes |
| `content` | object | No | TipTap JSON |
| `excerpt` | string | No | — |
| `status` | string | No | — |
| `contentType` | string | No | — |
| `difficulty` | string | No | — |
| `changeSummary` | string | No | Stored in version record |

**Side effects**: If content changes, creates a new `ArticleVersion` with incremented version number.
**200 Response**: `{ "id", "slug", "title" }`
**403**: Not owner and lacks `articles:edit_any`.

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

### `DELETE /api/tags?id={id}`
**Auth**: Bearer (JWT or API Key)
**Permission**: `tags:manage`

**Side effects**: Cascades removal of ArticleTag mappings.
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

## Article Feedback

### `POST /api/articles/{articleId}/feedback`
**Auth**: Bearer

| Field | Type | Required |
|-------|------|----------|
| `helpful` | bool | Yes |
| `comment` | string | No |

**201 Response**: `{ "message": "Feedback submitted" }`

---

### `GET /api/articles/{articleId}/feedback`
**Auth**: Bearer

**200 Response**: `{ "helpful": 12, "notHelpful": 3 }`

---

## Search

### `GET /api/search`
**Auth**: Bearer (JWT or API Key)

| Param | Type | Default | Notes |
|-------|------|---------|-------|
| `q` | string | — | Required. Prefix with `@slug` for tag search |
| `type` | string | `"fulltext"` | `fulltext`, `semantic`, `hybrid`, `rag` |
| `limit` | int | 20 | Max results |

**Search modes**:
- **Tag-based** (`q` starts with `@`): Filters by tag slug, optional text after tag name
- **Fulltext**: SQL LIKE on `title` and `excerpt`, published articles only
- **Semantic / Hybrid**: Placeholder (returns empty or stub)
- **RAG**: Placeholder (returns stub response)

**Side effects**: Logs a `SearchQuery` record with query text, result count, response time, and search type.

**200 Response (non-RAG)**:
```json
{
  "results": [
    { "id": "...", "title": "...", "slug": "...", "excerpt": "...", "contentType": "...", "difficulty": "...", "updatedAt": "..." }
  ],
  "total": 5,
  "searchType": "fulltext",
  "responseTimeMs": 12
}
```

**200 Response (RAG)**:
```json
{
  "answer": "...",
  "sources": [{ "articleId": "...", "text": "...", "score": 0.95 }]
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
| `name` | string | Yes | — |
| `permissions` | string[] | No | Default: `["articles:read", "search"]` |
| `expiresInDays` | int | No | 1–365 days, default 90 |

**201 Response**: `{ "id", "key": "kp_abc123...", "name", "permissions", "expiresAt" }`
> The raw key (`kp_...`) is returned **only once** at creation time.

---

### `DELETE /api/keys?id={id}`
**Auth**: Bearer (session only)
**Permission**: `api_keys:manage`

**200 Response**: `{ "message": "API key deleted" }`

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
