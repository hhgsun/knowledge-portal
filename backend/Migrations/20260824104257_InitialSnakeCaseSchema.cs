using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialSnakeCaseSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "featured_links",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    link_type = table.Column<string>(type: "text", nullable: false),
                    target = table.Column<string>(type: "text", nullable: false),
                    icon = table.Column<string>(type: "text", nullable: true),
                    color = table.Column<string>(type: "text", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_featured_links", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lookup_values",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    color = table.Column<string>(type: "text", nullable: true),
                    icon = table.Column<string>(type: "text", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    authority_weight = table.Column<int>(type: "integer", nullable: false, defaultValue: 50),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lookup_values", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rag_evaluation_datasets",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    cases_json = table.Column<string>(type: "jsonb", nullable: false),
                    thresholds_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rag_evaluation_datasets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tags",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tags", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false, defaultValue: "viewer"),
                    azure_object_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    key_hash = table.Column<string>(type: "text", nullable: false),
                    key_prefix = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    last_used_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_api_keys_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rag_evaluation_runs",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    dataset_id = table.Column<string>(type: "text", nullable: false),
                    requested_by_id = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    total_cases = table.Column<int>(type: "integer", nullable: false),
                    completed_cases = table.Column<int>(type: "integer", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    worker_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    lease_expires_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    dataset_version = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    cases_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    thresholds_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    runtime_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    metrics_json = table.Column<string>(type: "jsonb", nullable: true),
                    results_json = table.Column<string>(type: "jsonb", nullable: true),
                    error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rag_evaluation_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_rag_evaluation_runs_rag_evaluation_datasets_dataset_id",
                        column: x => x.dataset_id,
                        principalTable: "rag_evaluation_datasets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_rag_evaluation_runs_users_requested_by_id",
                        column: x => x.requested_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "articles",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "text", nullable: true),
                    excerpt = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "draft"),
                    owner_id = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false, defaultValue: "reference"),
                    created_via_api_key_id = table.Column<string>(type: "text", nullable: true),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    read_time_minutes = table.Column<int>(type: "integer", nullable: true),
                    published_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    last_reviewed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    approved_by_id = table.Column<string>(type: "text", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    review_interval_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 90),
                    version_counter = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    fts_indexed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    indexed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_articles", x => x.id);
                    table.ForeignKey(
                        name: "fk_articles_api_keys_created_via_api_key_id",
                        column: x => x.created_via_api_key_id,
                        principalTable: "api_keys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_articles_users_approved_by_id",
                        column: x => x.approved_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_articles_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usage_events",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: true),
                    api_key_id = table.Column<string>(type: "text", nullable: true),
                    auth_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    operation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    http_method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: false),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usage_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_usage_events_api_keys_api_key_id",
                        column: x => x.api_key_id,
                        principalTable: "api_keys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_usage_events_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "article_attachments",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    article_id = table.Column<string>(type: "text", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    stored_file_name = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    extraction_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    extraction_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    extracted_text = table.Column<string>(type: "text", nullable: true),
                    extracted_segments_json = table.Column<string>(type: "text", nullable: true),
                    extraction_truncated = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    extracted_characters = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    extraction_character_limit = table.Column<int>(type: "integer", nullable: false, defaultValue: 50000),
                    extracted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    uploaded_by_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_article_attachments", x => x.id);
                    table.ForeignKey(
                        name: "fk_article_attachments_articles_article_id",
                        column: x => x.article_id,
                        principalTable: "articles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_article_attachments_users_uploaded_by_id",
                        column: x => x.uploaded_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "article_comments",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    article_id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    comment = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_article_comments", x => x.id);
                    table.ForeignKey(
                        name: "fk_article_comments_articles_article_id",
                        column: x => x.article_id,
                        principalTable: "articles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_article_comments_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "article_tags",
                columns: table => new
                {
                    article_id = table.Column<string>(type: "text", nullable: false),
                    tag_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_article_tags", x => new { x.article_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_article_tags_articles_article_id",
                        column: x => x.article_id,
                        principalTable: "articles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_article_tags_tags_tag_id",
                        column: x => x.tag_id,
                        principalTable: "tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "article_versions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    article_id = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "text", nullable: true),
                    changed_by = table.Column<string>(type: "text", nullable: false),
                    change_summary = table.Column<string>(type: "text", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_article_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_article_versions_articles_article_id",
                        column: x => x.article_id,
                        principalTable: "articles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_article_versions_users_changed_by",
                        column: x => x.changed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "article_views",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    article_id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_article_views", x => x.id);
                    table.ForeignKey(
                        name: "fk_article_views_articles_article_id",
                        column: x => x.article_id,
                        principalTable: "articles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_article_views_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "article_votes",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    article_id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    is_helpful = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_article_votes", x => x.id);
                    table.ForeignKey(
                        name: "fk_article_votes_articles_article_id",
                        column: x => x.article_id,
                        principalTable: "articles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_article_votes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "index_jobs",
                columns: table => new
                {
                    article_id = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    generation = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    available_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    locked_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    locked_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_index_jobs", x => x.article_id);
                    table.ForeignKey(
                        name: "fk_index_jobs_articles_article_id",
                        column: x => x.article_id,
                        principalTable: "articles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "search_queries",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    query = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: true),
                    results_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    clicked_article_id = table.Column<string>(type: "text", nullable: true),
                    search_type = table.Column<string>(type: "text", nullable: false, defaultValue: "fulltext"),
                    response_time_ms = table.Column<int>(type: "integer", nullable: true),
                    rag_trace_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    rag_prompt_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    rag_retrieval_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    rag_reranker = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    rag_index_profile = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    rag_grounding_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    rag_answer_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    rag_feedback = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    rag_feedback_reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    rag_feedback_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_search_queries", x => x.id);
                    table.ForeignKey(
                        name: "fk_search_queries_articles_clicked_article_id",
                        column: x => x.clicked_article_id,
                        principalTable: "articles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_search_queries_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "article_embeddings",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    article_id = table.Column<string>(type: "text", nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    source_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attachment_id = table.Column<string>(type: "text", nullable: true),
                    source_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source_location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    embedding = table.Column<Vector>(type: "vector(1024)", nullable: false),
                    model_name = table.Column<string>(type: "text", nullable: false),
                    text_hash = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "text", nullable: true),
                    dimensions = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: true),
                    created_via_api_key_id = table.Column<string>(type: "text", nullable: true),
                    owner_id = table.Column<string>(type: "text", nullable: true),
                    tag_slugs = table.Column<string[]>(type: "text[]", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_article_embeddings", x => x.id);
                    table.ForeignKey(
                        name: "fk_article_embeddings_article_attachments_attachment_id",
                        column: x => x.attachment_id,
                        principalTable: "article_attachments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_article_embeddings_articles_article_id",
                        column: x => x.article_id,
                        principalTable: "articles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_key_prefix",
                table: "api_keys",
                column: "key_prefix");

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_user_id",
                table: "api_keys",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_article_attachments_article_id",
                table: "article_attachments",
                column: "article_id");

            migrationBuilder.CreateIndex(
                name: "ix_article_attachments_uploaded_by_id",
                table: "article_attachments",
                column: "uploaded_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_article_comments_article_id",
                table: "article_comments",
                column: "article_id");

            migrationBuilder.CreateIndex(
                name: "ix_article_comments_user_id",
                table: "article_comments",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_article_embeddings_article_id_chunk_index",
                table: "article_embeddings",
                columns: new[] { "article_id", "chunk_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_article_embeddings_attachment_id",
                table: "article_embeddings",
                column: "attachment_id");

            migrationBuilder.CreateIndex(
                name: "ix_article_embeddings_content_type",
                table: "article_embeddings",
                column: "content_type");

            migrationBuilder.CreateIndex(
                name: "ix_article_embeddings_created_via_api_key_id",
                table: "article_embeddings",
                column: "created_via_api_key_id");

            migrationBuilder.CreateIndex(
                name: "ix_article_embeddings_owner_id",
                table: "article_embeddings",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_article_embeddings_tag_slugs",
                table: "article_embeddings",
                column: "tag_slugs")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_article_tags_tag_id",
                table: "article_tags",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_article_versions_article_id_version",
                table: "article_versions",
                columns: new[] { "article_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_article_versions_changed_by",
                table: "article_versions",
                column: "changed_by");

            migrationBuilder.CreateIndex(
                name: "ix_article_views_article_id",
                table: "article_views",
                column: "article_id");

            migrationBuilder.CreateIndex(
                name: "ix_article_views_user_id",
                table: "article_views",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_article_votes_article_id_user_id",
                table: "article_votes",
                columns: new[] { "article_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_article_votes_user_id",
                table: "article_votes",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_articles_approved_by_id",
                table: "articles",
                column: "approved_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_articles_created_via_api_key_id",
                table: "articles",
                column: "created_via_api_key_id");

            migrationBuilder.CreateIndex(
                name: "ix_articles_external_id",
                table: "articles",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_articles_owner_id",
                table: "articles",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_articles_slug",
                table: "articles",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_articles_status_fts_indexed_at",
                table: "articles",
                columns: new[] { "status", "fts_indexed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_articles_status_indexed_at",
                table: "articles",
                columns: new[] { "status", "indexed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_index_jobs_status_available_at_priority",
                table: "index_jobs",
                columns: new[] { "status", "available_at", "priority" });

            migrationBuilder.CreateIndex(
                name: "ix_lookup_values_category_value",
                table: "lookup_values",
                columns: new[] { "category", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rag_evaluation_datasets_name",
                table: "rag_evaluation_datasets",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rag_evaluation_runs_dataset_id",
                table: "rag_evaluation_runs",
                column: "dataset_id");

            migrationBuilder.CreateIndex(
                name: "ix_rag_evaluation_runs_requested_by_id",
                table: "rag_evaluation_runs",
                column: "requested_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_rag_evaluation_runs_status_created_at",
                table: "rag_evaluation_runs",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_search_queries_clicked_article_id",
                table: "search_queries",
                column: "clicked_article_id");

            migrationBuilder.CreateIndex(
                name: "ix_search_queries_user_id",
                table: "search_queries",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tags_slug",
                table: "tags",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usage_events_api_key_id_occurred_at",
                table: "usage_events",
                columns: new[] { "api_key_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_usage_events_occurred_at",
                table: "usage_events",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_usage_events_operation_occurred_at",
                table: "usage_events",
                columns: new[] { "operation", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_usage_events_user_id_occurred_at",
                table: "usage_events",
                columns: new[] { "user_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_slug",
                table: "users",
                column: "slug",
                unique: true);

            migrationBuilder.Sql("""
                CREATE INDEX ix_article_embeddings_embedding_hnsw
                ON article_embeddings USING hnsw (embedding vector_cosine_ops)
                WITH (m = 16, ef_construction = 200);

                CREATE OR REPLACE FUNCTION article_tag_slugs(p_article_id text)
                RETURNS text[] AS $$
                    SELECT COALESCE(array_agg(t.slug ORDER BY t.slug), '{}')
                    FROM article_tags at2
                    JOIN tags t ON t.id = at2.tag_id
                    WHERE at2.article_id = p_article_id;
                $$ LANGUAGE sql STABLE;

                CREATE OR REPLACE FUNCTION article_embeddings_fill_filters()
                RETURNS trigger AS $$
                BEGIN
                    SELECT a.owner_id, a.content_type, a.created_via_api_key_id
                      INTO NEW.owner_id, NEW.content_type, NEW.created_via_api_key_id
                    FROM articles a WHERE a.id = NEW.article_id;
                    NEW.tag_slugs := article_tag_slugs(NEW.article_id);
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_article_embeddings_fill_filters
                BEFORE INSERT ON article_embeddings
                FOR EACH ROW EXECUTE FUNCTION article_embeddings_fill_filters();

                CREATE OR REPLACE FUNCTION articles_sync_embedding_filters()
                RETURNS trigger AS $$
                BEGIN
                    UPDATE article_embeddings
                    SET owner_id = NEW.owner_id,
                        content_type = NEW.content_type,
                        created_via_api_key_id = NEW.created_via_api_key_id
                    WHERE article_id = NEW.id;
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_articles_sync_embedding_filters
                AFTER UPDATE OF owner_id, content_type, created_via_api_key_id ON articles
                FOR EACH ROW
                WHEN (OLD.owner_id IS DISTINCT FROM NEW.owner_id
                   OR OLD.content_type IS DISTINCT FROM NEW.content_type
                   OR OLD.created_via_api_key_id IS DISTINCT FROM NEW.created_via_api_key_id)
                EXECUTE FUNCTION articles_sync_embedding_filters();

                CREATE OR REPLACE FUNCTION article_tags_sync_embedding_filters()
                RETURNS trigger AS $$
                BEGIN
                    IF TG_OP <> 'INSERT' THEN
                        UPDATE article_embeddings SET tag_slugs = article_tag_slugs(OLD.article_id)
                        WHERE article_id = OLD.article_id;
                    END IF;
                    IF TG_OP <> 'DELETE' THEN
                        UPDATE article_embeddings SET tag_slugs = article_tag_slugs(NEW.article_id)
                        WHERE article_id = NEW.article_id;
                    END IF;
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_article_tags_sync_embedding_filters
                AFTER INSERT OR UPDATE OR DELETE ON article_tags
                FOR EACH ROW EXECUTE FUNCTION article_tags_sync_embedding_filters();

                CREATE OR REPLACE FUNCTION tags_sync_embedding_filters()
                RETURNS trigger AS $$
                BEGIN
                    UPDATE article_embeddings e
                    SET tag_slugs = article_tag_slugs(e.article_id)
                    WHERE e.article_id IN (SELECT article_id FROM article_tags WHERE tag_id = NEW.id);
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_tags_sync_embedding_filters
                AFTER UPDATE OF slug ON tags
                FOR EACH ROW WHEN (OLD.slug IS DISTINCT FROM NEW.slug)
                EXECUTE FUNCTION tags_sync_embedding_filters();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS tags_sync_embedding_filters() CASCADE;
                DROP FUNCTION IF EXISTS article_tags_sync_embedding_filters() CASCADE;
                DROP FUNCTION IF EXISTS articles_sync_embedding_filters() CASCADE;
                DROP FUNCTION IF EXISTS article_embeddings_fill_filters() CASCADE;
                DROP FUNCTION IF EXISTS article_tag_slugs(text) CASCADE;
                """);

            migrationBuilder.DropTable(
                name: "article_comments");

            migrationBuilder.DropTable(
                name: "article_embeddings");

            migrationBuilder.DropTable(
                name: "article_tags");

            migrationBuilder.DropTable(
                name: "article_versions");

            migrationBuilder.DropTable(
                name: "article_views");

            migrationBuilder.DropTable(
                name: "article_votes");

            migrationBuilder.DropTable(
                name: "featured_links");

            migrationBuilder.DropTable(
                name: "index_jobs");

            migrationBuilder.DropTable(
                name: "lookup_values");

            migrationBuilder.DropTable(
                name: "rag_evaluation_runs");

            migrationBuilder.DropTable(
                name: "search_queries");

            migrationBuilder.DropTable(
                name: "usage_events");

            migrationBuilder.DropTable(
                name: "article_attachments");

            migrationBuilder.DropTable(
                name: "tags");

            migrationBuilder.DropTable(
                name: "rag_evaluation_datasets");

            migrationBuilder.DropTable(
                name: "articles");

            migrationBuilder.DropTable(
                name: "api_keys");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
