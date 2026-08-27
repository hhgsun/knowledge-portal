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
    User ||--o{ Article : "approves"

    Article ||--o{ ArticleVersion : "has"
    Article ||--o{ ArticleTag : "tagged"
    Article ||--o{ ArticleVote : "voted"
    Article ||--o{ ArticleComment : "commented"
    Article ||--o{ ArticleView : "tracked"
    Article ||--o{ ArticleAttachment : "has"
    Article ||--o{ ArticleChunkParent : "context parents"
    ArticleChunkParent ||--o{ ArticleEmbedding : "searchable children"
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
        string external_id UK "nullable, bulk identity"
        int read_time_minutes "nullable"
        datetime published_at "nullable"
        datetime last_reviewed_at "nullable"
        string approved_by_id FK "nullable, SetNull"
        datetime approved_at "nullable"
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
| Name | `string` | `name` | Required | — |
| Slug | `string` | `slug` | Required, Unique index | Auto-generated from name |
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
| ExternalId | `string?` | `external_id` | Unique (nullable), max 200 | Stable bulk-import identity |
| ReadTimeMinutes | `int?` | `read_time_minutes` | — | `null` |
| PublishedAt | `DateTime?` | `published_at` | — | `null` (set on first publish) |
| LastReviewedAt | `DateTime?` | `last_reviewed_at` | — | `null` (set on approval; material changes clear it) |
| ApprovedById | `string?` | `approved_by_id` | FK → users.id (SetNull) | `null` |
| ApprovedAt | `DateTime?` | `approved_at` | — | `null` |
| ReviewIntervalDays | `int` | `review_interval_days` | — | `90` |
| VersionCounter | `int` | `version_counter` | Required | Atomic per-article version allocator |
| FtsIndexedAt | `DateTime?` | `fts_indexed_at` | — | `null` (lexical index dirty marker) |
| IndexedAt | `DateTime?` | `indexed_at` | — | `null` (semantic index dirty marker) |
| CreatedAt | `DateTime` | `created_at` | Required | UTC Now |
| UpdatedAt | `DateTime` | `updated_at` | Required | UTC Now |

**Valid statuses**: `draft`, `published`, `archived`
**Valid content types**: DB-driven active values in `lookup_values` category `content_type`

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
| RagTraceId | `string?` | `rag_trace_id` | Max 64; RAG only | `null` |
| RagPromptVersion | `string?` | `rag_prompt_version` | Max 100; RAG only | `null` |
| RagRetrievalVersion | `string?` | `rag_retrieval_version` | Max 100; query/retrieval/ranking contract | `null` |
| RagReranker | `string?` | `rag_reranker` | Max 100; local or external model identity | `null` |
| RagIndexProfile | `string?` | `rag_index_profile` | Max 64; RAG only | `null` |
| RagGroundingStatus | `string?` | `rag_grounding_status` | Max 40; RAG only | `null` |
| RagAnswerHash | `string?` | `rag_answer_hash` | SHA-256; avoids duplicating generated text | `null` |
| RagFeedback | `string?` | `rag_feedback` | `helpful` or `not_helpful` | `null` |
| RagFeedbackReason | `string?` | `rag_feedback_reason` | Bounded reason code | `null` |
| RagFeedbackAt | `DateTime?` | `rag_feedback_at` | — | `null` |
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
| ExtractionTruncated | `bool` | `extraction_truncated` | True when text exceeded the configured extraction cap | `false` |
| ExtractedCharacters | `int` | `extracted_characters` | Persisted extracted character count | `0` |
| ExtractionCharacterLimit | `int` | `extraction_character_limit` | Limit used for this extraction | `50000` |
| ExtractionProfile | `string?` | `extraction_profile` | Max 200; parser/strategy/vision cache identity | `null` |
| UploadedById | `string` | `uploaded_by_id` | FK → users.id, Required | — |
| CreatedAt | `DateTime` | `created_at` | Required | UTC Now |

### ArticleChunkParent

Structure-bounded context rows stored in `article_chunk_parents`. They carry `id`, `article_id`,
`parent_index`, source/attachment provenance, exact `content`, `text_hash`, `word_count`, and
`created_at`. Parents are not embedded; one parent is reused by every matching searchable child.
The `(article_id, parent_index)` pair is unique.

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
| ParentChunkId | `string?` | `parent_chunk_id` | FK → article_chunk_parents.id (Cascade); nullable for rolling legacy rows | `null` |
| Embedding | `Vector` | `embedding` | Required, vector(1024) | — |
| ModelName | `string` | `model_name` | Required | — |
| TextHash | `string` | `text_hash` | Required | — (SHA256 hex) |
| Content | `string?` | `content` | Exact embedded chunk text | `null` |
| Dimensions | `int` | `dimensions` | Required | — |
| CreatedAt | `DateTime` | `created_at` | Required | UTC Now |

The following database-owned shadow columns are maintained by triggers so filtered HNSW retrieval never needs to join `articles`: `owner_id`, `content_type`, `created_via_api_key_id`, and `tag_slugs text[]`. B-tree indexes cover scalar filters, a GIN index covers `tag_slugs`, and `ix_article_embeddings_embedding_hnsw` covers cosine vector search.

### IndexJob

One durable, coalescing queue row per article. `Generation` increments on every edit; workers may complete only the generation they claimed. Status is `pending`, `processing`, `completed`, or `failed`; lease, retry, priority and error fields are persisted.

### FeaturedLink

Sidebar configuration stored in `featured_links`: `id`, `label`, `link_type`, `target`, optional `icon`/`color`, `sort_order`, `is_active`, and `created_at`. `link_type` is `content_type`, `tag`, or `custom`.

### UsageEvent

Authenticated usage telemetry stored in `usage_events`: `id`, `occurred_at`, nullable `user_id`/`api_key_id`, `auth_source`, `channel`, `operation`, `http_method`, `outcome`, `status_code`, and `duration_ms`. Foreign keys use SetNull so historical analytics survive user/key deletion.

### AssistantInteraction

Privacy-safe routing audit and feedback stored in `assistant_interactions`: `id`, nullable `user_id`/`api_key_id`, SHA-256 `query_fingerprint`, route/source/reason/confidence, nullable `search_query_id`, JSONB tool names, duration, optional helpful/reason/corrected-route feedback, and timestamps. Raw user text and generated answers are intentionally not stored. User/API-key foreign keys use SetNull; the search query identifier is audit correlation rather than a database foreign key so historical interaction rows remain removable and loosely coupled from the search subsystem.

### RagEvaluationDataset and RagEvaluationRun

`rag_evaluation_datasets` stores named/versioned JSONB cases and thresholds plus timestamps. `rag_evaluation_runs` stores the requesting admin, durable lease/retry state, immutable dataset/config/runtime snapshots, progress, JSONB metrics/results, errors and lifecycle timestamps. Dataset deletion cascades to runs; deleting a requesting user is restricted while runs reference that user.

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
| AuthorityWeight | `int` | `authority_weight` | 0–100 | `50` |
| IsActive | `bool` | `is_active` | Required | `true` |
| CreatedAt | `DateTime` | `created_at` | Required | UTC Now |

## Indexes

| Table | Column(s) | Type |
|-------|-----------|------|
| `users` | `email` | Unique |
| `articles` | `slug` | Unique |
| `articles` | `external_id` | Unique (nullable) |
| `articles` | `status`, `indexed_at` | Composite |
| `articles` | `status`, `fts_indexed_at` | Composite |
| `article_embeddings` | `article_id`, `chunk_index` | Unique (composite) |
| `article_embeddings` | `parent_chunk_id` | B-tree |
| `article_chunk_parents` | `article_id`, `parent_index` | Unique (composite) |
| `article_embeddings` | `embedding` | HNSW cosine |
| `article_embeddings` | `tag_slugs` | GIN |
| `tags` | `slug` | Unique |
| `assistant_interactions` | `created_at` | B-tree |
| `assistant_interactions` | `route`, `created_at` | Composite |
| `assistant_interactions` | `user_id`, `created_at` | Composite |
| `assistant_interactions` | `api_key_id` | B-tree |

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
| Article | ArticleChunkParent | Cascade |
| ArticleChunkParent | ArticleEmbedding | Cascade |
| Article | ArticleEmbedding | Cascade |
| ApiKey | Article.CreatedViaApiKeyId | SetNull |
| User | Article.ApprovedById | SetNull |
| Tag | ArticleTag | Cascade |
| User | AssistantInteraction | SetNull |
| ApiKey | AssistantInteraction | SetNull |

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
