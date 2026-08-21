# Data Model

> **⚠️ Bu dosya `AGENTS.md`'ye tabidir.** Çelişki durumunda `AGENTS.md` geçerlidir.
> Entity listesi, Validation Rules → `AGENTS.md`

## Entity-Relationship Diagram

```mermaid
erDiagram
    User ||--o{ Article : "owns"
    User ||--o{ ApiKey : "has"
    User ||--o{ ArticleVote : "votes"
    User ||--o{ ArticleComment : "comments"
    User ||--o{ ArticleView : "records"
    User ||--o{ ArticleVersion : "changed_by"
    User ||--o{ SearchQuery : "performs"

    Article ||--o{ ArticleVersion : "has"
    Article ||--o{ ArticleTag : "tagged"
    Article ||--o{ ArticleVote : "voted"
    Article ||--o{ ArticleComment : "commented"
    Article ||--o{ ArticleView : "tracked"
    Article ||--o{ ArticleAttachment : "has"
    Article ||--o{ ArticleEmbedding : "embedded (chunks)"
    Article o|--o| ApiKey : "created_via"

    Tag ||--o{ ArticleTag : "applied"

    SearchQuery o|--o| Article : "clicked"

    User {
        string id PK "21-char truncated GUID"
        string name
        string slug UK "auto-generated from name"
        string email UK
        string password_hash
        string role "default: viewer"
        datetime created_at
        datetime updated_at
    }

    Article {
        string id PK
        string title
        string slug UK
        string content "nullable, canonical CommonMark/GFM Markdown"
        string excerpt "nullable"
        string status "default: draft"
        string owner_id FK
        string content_type "default: reference"
        string created_via_api_key_id FK "nullable, SetNull"
        int read_time_minutes "nullable"
        datetime published_at "nullable"
        datetime last_reviewed_at "nullable"
        int review_interval_days "default: 90"
        datetime indexed_at "nullable"
        datetime created_at
        datetime updated_at
    }

    ArticleVersion {
        string id PK
        string article_id FK "Cascade"
        string title
        string content "nullable, canonical Markdown snapshot"
        string changed_by FK
        string change_summary "nullable"
        int version
        datetime created_at
    }

    Tag {
        string id PK
        string name
        string slug UK
    }

    ArticleTag {
        string article_id PK_FK "Cascade"
        string tag_id PK_FK "Cascade"
    }

    ArticleVote {
        string id PK
        string article_id FK "Cascade"
        string user_id FK
        bool is_helpful
        string reason "nullable, only when is_helpful=false"
        datetime created_at
        datetime updated_at
    }

    ArticleComment {
        string id PK
        string article_id FK "Cascade"
        string user_id FK
        string comment
        datetime created_at
    }

    ArticleView {
        string id PK
        string article_id FK "Cascade"
        string user_id FK "nullable"
        datetime created_at
    }

    ApiKey {
        string id PK
        string user_id FK "Cascade"
        string key_hash "BCrypt"
        string key_prefix "8-char index"
        string name
        datetime last_used_at "nullable"
        datetime expires_at "nullable"
        datetime created_at
    }

    SearchQuery {
        string id PK
        string query
        string user_id FK "nullable"
        int results_count "default: 0"
        string clicked_article_id FK "nullable"
        string search_type "default: fulltext"
        int response_time_ms "nullable"
        datetime created_at
    }

    ArticleAttachment {
        string id PK
        string article_id FK
        string file_name
        string stored_file_name
        string content_type
        long size_bytes
        string uploaded_by_id FK
        datetime created_at
    }
```

## Entity Details

### User

| Column | C# Type | DB Column | Constraints | Default |
|--------|---------|-----------|-------------|---------|
| Id | `string` | `id` | PK, 21 chars | Truncated GUID |
| ExternalId | `string?` | `external_id` | Unique (nullable), max 200 | Stable bulk-import identity |
| Name | `string` | `name` | Required | — |
| Email | `string` | `email` | Required, Unique index | — |
| PasswordHash | `string` | `password_hash` | Required | BCrypt (cost 12) |
| Role | `string` | `role` | Required | `"viewer"` |
| AzureObjectId | `string?` | `azure_object_id` | Unique (nullable) | `null` |
| CreatedAt | `DateTime` | `created_at` | Required | UTC Now |
| UpdatedAt | `DateTime` | `updated_at` | Required | UTC Now |

**Valid roles**: `admin`, `editor`, `viewer`

### Article

| Column | C# Type | DB Column | Constraints | Default |
|--------|---------|-----------|-------------|---------|
| Id | `string` | `id` | PK, 21 chars | Truncated GUID |
| Title | `string` | `title` | Required | — |
| Slug | `string` | `slug` | Required, Unique index | Auto-generated from title |
| Content | `string?` | `content` | — | `null` (canonical CommonMark/GFM Markdown; exposed as `contentMarkdown`) |
| Excerpt | `string?` | `excerpt` | — | `null` |
| Status | `string` | `status` | Required | `"draft"` |
| OwnerId | `string` | `owner_id` | FK → users.id | — |
| ContentType | `string` | `content_type` | Required | `"reference"` |
| CreatedViaApiKeyId | `string?` | `created_via_api_key_id` | FK → api_keys.id (SetNull) | `null` |
| ReadTimeMinutes | `int?` | `read_time_minutes` | — | `null` |
| PublishedAt | `DateTime?` | `published_at` | — | `null` (set on first publish) |
| LastReviewedAt | `DateTime?` | `last_reviewed_at` | — | `null` (set on approval; material changes clear it) |
| ReviewIntervalDays | `int` | `review_interval_days` | — | `90` |
| VersionCounter | `int` | `version_counter` | Required | Atomic per-article version allocator |
| FtsIndexedAt | `DateTime?` | `fts_indexed_at` | — | `null` (lexical index dirty marker) |
| IndexedAt | `DateTime?` | `indexed_at` | — | `null` (semantic index dirty marker) |
| CreatedAt | `DateTime` | `created_at` | Required | UTC Now |
| UpdatedAt | `DateTime` | `updated_at` | Required | UTC Now |

**Valid statuses**: `draft`, `published`, `archived`
**Valid content types**: DB-driven active values in `lookup_values` category `content_type`
**Valid difficulties**: `beginner`, `intermediate`, `advanced`

### ArticleVersion

| Column | C# Type | DB Column | Constraints | Default |
|--------|---------|-----------|-------------|---------|
| Id | `string` | `id` | PK | Truncated GUID |
| ArticleId | `string` | `article_id` | FK → articles.id (Cascade) | — |
| Title | `string` | `title` | Required | — |
| Content | `string?` | `content` | — | `null` |
| ChangedBy | `string` | `changed_by` | FK → users.id | — |
| ChangeSummary | `string?` | `change_summary` | — | `null` |
| Version | `int` | `version` | Required, unique with ArticleId | Sequential via Article.VersionCounter |
| CreatedAt | `DateTime` | `created_at` | Required | UTC Now |

### Tag

| Column | C# Type | DB Column | Constraints | Default |
|--------|---------|-----------|-------------|---------|
| Id | `string` | `id` | PK | Truncated GUID |
| Name | `string` | `name` | Required | — |
| Slug | `string` | `slug` | Required, Unique index | Auto-generated from name |

### ArticleTag (Join Table)

| Column | C# Type | DB Column | Constraints |
|--------|---------|-----------|-------------|
| ArticleId | `string` | `article_id` | Composite PK, FK (Cascade) |
| TagId | `string` | `tag_id` | Composite PK, FK (Cascade) |

### ArticleVote

| Column | C# Type | DB Column | Constraints | Default |
|--------|---------|-----------|-------------|---------|
| Id | `string` | `id` | PK | Truncated GUID |
| ArticleId | `string` | `article_id` | FK (Cascade) | — |
| UserId | `string` | `user_id` | FK → users.id; unique with ArticleId | — |
| IsHelpful | `bool` | `is_helpful` | Required | — |
| Reason | `string?` | `reason` | — | `null` |
| CreatedAt | `DateTime` | `created_at` | Required | UTC Now |
| UpdatedAt | `DateTime` | `updated_at` | Required | UTC Now |

### ArticleComment

| Column | C# Type | DB Column | Constraints | Default |
|--------|---------|-----------|-------------|---------|
| Id | `string` | `id` | PK | Truncated GUID |
| ArticleId | `string` | `article_id` | FK (Cascade) | — |
| UserId | `string` | `user_id` | FK → users.id | — |
| Comment | `string` | `comment` | Required | — |
| CreatedAt | `DateTime` | `created_at` | Required | UTC Now |

### ArticleView

| Column | C# Type | DB Column | Constraints | Default |
|--------|---------|-----------|-------------|---------|
| Id | `string` | `id` | PK | Truncated GUID |
| ArticleId | `string` | `article_id` | FK (Cascade) | — |
| UserId | `string?` | `user_id` | FK → users.id | `null` |

| CreatedAt | `DateTime` | `created_at` | Required | UTC Now |

### ApiKey

| Column | C# Type | DB Column | Constraints | Default |
|--------|---------|-----------|-------------|---------|
| Id | `string` | `id` | PK | Truncated GUID |
| UserId | `string` | `user_id` | FK → users.id (Cascade) | — |
| KeyHash | `string` | `key_hash` | Required | BCrypt hash |
| KeyPrefix | `string` | `key_prefix` | Required, 8 chars, Indexed | — |
| Name | `string` | `name` | Required | — |
| LastUsedAt | `DateTime?` | `last_used_at` | — | `null` |
| ExpiresAt | `DateTime?` | `expires_at` | — | `null` |
| CreatedAt | `DateTime` | `created_at` | Required | UTC Now |

**Key format**: `kp_` + 32 random hex characters

### SearchQuery

| Column | C# Type | DB Column | Constraints | Default |
|--------|---------|-----------|-------------|---------|
| Id | `string` | `id` | PK | Truncated GUID |
| Query | `string` | `query` | Required | — |
| UserId | `string?` | `user_id` | FK → users.id | `null` |
| ResultsCount | `int` | `results_count` | — | `0` |
| ClickedArticleId | `string?` | `clicked_article_id` | FK → articles.id | `null` |
| SearchType | `string` | `search_type` | Required | `"fulltext"` |
| ResponseTimeMs | `int?` | `response_time_ms` | — | `null` |
| CreatedAt | `DateTime` | `created_at` | Required | UTC Now |

### ArticleAttachment

| Column | C# Type | DB Column | Constraints | Default |
|--------|---------|-----------|-------------|---------|
| Id | `string` | `id` | PK | Truncated GUID |
| ArticleId | `string` | `article_id` | FK → articles.id, Required | — |
| FileName | `string` | `file_name` | Required | — |
| StoredFileName | `string` | `stored_file_name` | Required | GUID-based |
| ContentType | `string` | `content_type` | Required | — |
| SizeBytes | `long` | `size_bytes` | Required | — |
| Sha256 | `string` | `sha256` | Required, 64 chars | — |
| ExtractionStatus | `string` | `extraction_status` | Required | `pending` |
| ExtractionError | `string?` | `extraction_error` | Max 2000 | `null` |
| ExtractedAt | `DateTime?` | `extracted_at` | — | `null` |
| ExtractedText | `string?` | `extracted_text` | Cached bounded plain text | `null` |
| ExtractedSegmentsJson | `string?` | `extracted_segments_json` | Provenance segments | `null` |
| UploadedById | `string` | `uploaded_by_id` | FK → users.id, Required | — |
| CreatedAt | `DateTime` | `created_at` | Required | UTC Now |

### ArticleEmbedding

| Column | C# Type | DB Column | Constraints | Default |
|--------|---------|-----------|-------------|---------|
| Id | `string` | `id` | PK | Truncated GUID |
| ArticleId | `string` | `article_id` | FK → articles.id (Cascade) | — |
| ChunkIndex | `int` | `chunk_index` | Required, Unique with ArticleId | 0 |
| SourceType | `string` | `source_type` | `article` or `attachment` | `article` |
| AttachmentId | `string?` | `attachment_id` | FK → article_attachments.id | `null` |
| SourceName | `string?` | `source_name` | Max 500 | `null` |
| SourceLocation | `string?` | `source_location` | Max 200 | `null` |
| Embedding | `Vector` | `embedding` | Required, vector(1024) | — |
| ModelName | `string` | `model_name` | Required | — |
| TextHash | `string` | `text_hash` | Required | — (SHA256 hex) |
| Dimensions | `int` | `dimensions` | Required | — |
| CreatedAt | `DateTime` | `created_at` | Required | UTC Now |

### IndexJob

One durable, coalescing queue row per article. `Generation` increments on every edit; workers may complete only the generation they claimed. Status is `pending`, `processing`, `completed`, or `failed`; lease, retry, priority and error fields are persisted.

### LookupValue

| Column | C# Type | DB Column | Constraints | Default |
|--------|---------|-----------|-------------|---------|
| Id | `string` | `id` | PK | Truncated GUID |
| Category | `string` | `category` | Required | — |
| Value | `string` | `value` | Required | — |
| Label | `string` | `label` | Required | — |
| Color | `string?` | `color` | — | `null` (Tailwind color key) |
| Icon | `string?` | `icon` | — | `null` (Lucide icon name) |
| SortOrder | `int` | `sort_order` | Required | Sequential |
| IsActive | `bool` | `is_active` | Required | `true` |
| CreatedAt | `DateTime` | `created_at` | Required | UTC Now |

## Indexes

| Table | Column(s) | Type |
|-------|-----------|------|
| `users` | `email` | Unique |
| `articles` | `slug` | Unique |
| `article_embeddings` | `article_id`, `chunk_index` | Unique (composite) |
| `tags` | `slug` | Unique |

## Cascade Behavior

| Parent | Child | On Delete |
|--------|-------|-----------|
| User | ApiKey | Cascade |
| User | Article | (no cascade — ownership preserved) |
| Article | ArticleVersion | Cascade |
| Article | ArticleTag | Cascade |
| Article | ArticleVote | Cascade |
| Article | ArticleComment | Cascade |
| Article | ArticleView | Cascade |
| Article | ArticleAttachment | Cascade |
| Article | ArticleEmbedding | Cascade |
| ApiKey | Article.CreatedViaApiKeyId | SetNull |
| Tag | ArticleTag | Cascade |

## Seed Data

On application startup, `DbInitializer.SeedAsync()`:

1. Applies pending EF Core migrations (`MigrateAsync`)
2. Creates admin user if `admin@finagotech.com.tr` does not exist:
   - Name: `Admin`
   - Email: `admin@finagotech.com.tr`
   - Password: `1q2w3E*/` (BCrypt, cost 12)
   - Role: `admin`
3. Creates 11 default tags if they do not exist:
   - `project-knowledge-portal`, `getting-started`, `tutorial`, `troubleshooting`, `best-practices`, `api`, `deployment`, `security`, `performance`, `testing`, `monitoring`
4. Creates the default `content_type` lookup values if no lookup values exist:
   - `reference`, `how-to`, `adr`, `runbook`, `faq`, `policy`, `onboarding`
5. Loads `backend/SeedData/articles/*.md` in filename order when the articles table is empty. These files are maintained as product documentation and must be updated alongside the behavior they describe.
