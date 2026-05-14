# API Reference — Knowledge Portal

Base URL: `http://localhost:3000/api`

All endpoints require authentication via session cookie (NextAuth JWT) or API key (`Authorization: Bearer kp_...`).

---

## Authentication

### POST /api/auth/register
Create a new user account (self-registration, default role: `viewer`).

**Body:**
```json
{
  "name": "string (1-100 chars)",
  "email": "string (valid email)",
  "password": "string (8-128 chars)"
}
```

**Response:** `201 Created`
```json
{ "id": "string", "name": "string", "email": "string" }
```

**Errors:** `400` (invalid input), `409` (email exists)

### POST /api/auth/[...nextauth]
NextAuth.js handlers (login, logout, session). Use NextAuth client SDK.

---

## Articles

### GET /api/articles
List articles with pagination and filtering.

**Query params:**
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `page` | int | 1 | Page number |
| `limit` | int | 20 | Items per page (max 100) |
| `status` | string | — | Filter by status: `draft`, `in_review`, `published`, `archived` |
| `q` | string | — | Title search (LIKE) |

**Note:** Viewers only see `published` articles.

**Response:** `200 OK`
```json
{
  "articles": [{ ...article }],
  "total": 42,
  "page": 1,
  "limit": 20
}
```

### POST /api/articles
Create a new article. **Permission:** `articles:create`

**Body:**
```json
{
  "title": "string (1-300 chars, required)",
  "content": { "type": "doc", ... },
  "excerpt": "string (max 500)",
  "status": "draft | in_review | published | archived",
  "contentType": "how-to | reference | adr | runbook | faq | policy | onboarding",
  "difficulty": "beginner | intermediate | advanced",
  "audience": "string (max 200)",
  "tags": ["tag-id-1", "tag-id-2"]
}
```

**Response:** `201 Created` — full article object

### GET /api/articles/:id
Get article by ID or slug. Records a view event.

**Response:** `200 OK` — full article object  
**Errors:** `404` (not found)

### PUT /api/articles/:id
Update an article. **Permission:** `articles:edit_own` (own) or `articles:edit_any` (others')

**Body:** (all fields optional)
```json
{
  "title": "string",
  "content": { ... },
  "excerpt": "string",
  "status": "string",
  "contentType": "string",
  "difficulty": "string",
  "audience": "string | null",
  "changeSummary": "string (max 500)"
}
```

Creates a new version entry automatically.

**Response:** `200 OK` — updated article object

### DELETE /api/articles/:id
Delete an article. **Permission:** `articles:delete_own` or `articles:delete_any`

**Response:** `200 OK`
```json
{ "success": true }
```

---

## Article Versions

### GET /api/articles/:id/versions
List all versions of an article (newest first).

**Response:** `200 OK`
```json
[
  {
    "id": "string",
    "version": 1,
    "title": "string",
    "changeSummary": "string | null",
    "changedBy": "user-id",
    "changedByName": "string",
    "createdAt": "ISO date"
  }
]
```

### GET /api/articles/:id/versions/:versionId
Get a specific version's full content.

**Response:** `200 OK` — version object with content

---

## Article Feedback

### POST /api/articles/:id/feedback
Submit feedback for an article.

**Body:**
```json
{
  "helpful": true,
  "comment": "string (max 1000, optional)"
}
```

**Response:** `200 OK` — `{ "success": true }`

### GET /api/articles/:id/feedback
Get feedback summary for an article.

**Response:** `200 OK`
```json
{
  "helpful": 10,
  "notHelpful": 2,
  "total": 12
}
```

---

## Search

### GET /api/search
Search the knowledge base.

**Query params:**
| Param | Type | Default | Description |
|-------|------|---------|-------------|
| `q` | string | — | Search query (required) |
| `type` | string | `hybrid` | Search type: `fulltext`, `semantic`, `hybrid`, `rag` |
| `limit` | int | 20 | Max results (max 50) |

**Response (search):** `200 OK`
```json
{
  "results": [{ ...article, "score": 0.85 }],
  "query": "string",
  "type": "hybrid",
  "responseTimeMs": 120
}
```

**Response (RAG):** `200 OK`
```json
{
  "answer": "Generated answer text...",
  "sources": [{ "articleId": "...", "text": "...", "score": 0.9 }],
  "query": "string",
  "type": "rag",
  "responseTimeMs": 2500
}
```

---

## Tags

### GET /api/tags
List all tags with article counts.

**Response:** `200 OK`
```json
[{ "id": "string", "name": "string", "slug": "string", "articleCount": 5 }]
```

### POST /api/tags
Create a new tag. **Permission:** `tags:manage`

**Body:**
```json
{ "name": "string (1-50 chars)" }
```

**Response:** `201 Created` — tag object

### DELETE /api/tags
Delete a tag. **Permission:** `tags:manage`

**Body:**
```json
{ "id": "tag-id" }
```

**Response:** `200 OK` — `{ "success": true }`

---

## API Keys

### GET /api/keys
List current user's API keys. **Permission:** `api_keys:manage`

**Response:** `200 OK`
```json
[{
  "id": "string",
  "name": "string",
  "permissions": ["articles:read"],
  "lastUsedAt": "ISO date | null",
  "expiresAt": "ISO date | null",
  "createdAt": "ISO date"
}]
```

### POST /api/keys
Create a new API key. **Permission:** `api_keys:manage`

**Body:**
```json
{
  "name": "string (1-100 chars)",
  "permissions": ["articles:read", "search"],
  "expiresInDays": 90
}
```

**Response:** `201 Created`
```json
{
  "id": "string",
  "key": "kp_abc123...",
  "name": "string"
}
```
> ⚠️ The raw key is only returned once at creation time.

### DELETE /api/keys
Revoke an API key. **Permission:** `api_keys:manage`

**Body:**
```json
{ "id": "key-id" }
```

---

## Analytics

### GET /api/analytics
Dashboard analytics data. **Permission:** `analytics:view`

**Response:** `200 OK`
```json
{
  "articlesByStatus": [{ "status": "published", "count": 30 }],
  "viewsThisWeek": 150,
  "searchesToday": 25,
  "topSearches": [{ "query": "docker", "count": 12 }],
  "contentGaps": [{ "query": "kubernetes", "count": 5 }],
  "topArticles": [{ "id": "...", "title": "...", "views": 45 }],
  "staleArticles": [{ "id": "...", "title": "...", "lastReviewedAt": "..." }]
}
```

---

## Admin

### GET /api/admin/users
List all users. **Permission:** `users:manage`

### PUT /api/admin/users
Update a user's role. **Permission:** `users:manage`

**Body:**
```json
{ "userId": "string", "role": "admin | editor | viewer" }
```

### DELETE /api/admin/users
Delete a user. **Permission:** `users:manage`

**Body:**
```json
{ "userId": "string" }
```

---

## Error Format

All errors follow a consistent format:
```json
{
  "error": "Human-readable error message",
  "details": { ... }
}
```

| Status Code | Meaning |
|-------------|---------|
| 400 | Invalid input (Zod validation failed) |
| 401 | Not authenticated |
| 403 | Insufficient permissions |
| 404 | Resource not found |
| 409 | Conflict (duplicate) |
| 500 | Internal server error |

---

## External API Key Authentication

For external integrations, use API keys instead of session cookies:

```bash
curl -H "Authorization: Bearer kp_your-key-here" \
  http://localhost:3000/api/articles
```

API keys use the `kp_` prefix. Permissions are scoped per key.
