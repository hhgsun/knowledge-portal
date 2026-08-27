using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantConversationsQualityAndCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "application_version",
                table: "assistant_interactions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "classifier_model",
                table: "assistant_interactions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "confidence_calibration_samples",
                table: "assistant_interactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "conversation_id",
                table: "assistant_interactions",
                type: "character varying(21)",
                maxLength: 21,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "raw_confidence",
                table: "assistant_interactions",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "routing_config_snapshot_json",
                table: "assistant_interactions",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "routing_prompt_version",
                table: "assistant_interactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "assistant_answer_cache",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_scope = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    query_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    query_embedding_json = table.Column<string>(type: "jsonb", nullable: false),
                    corpus_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    runtime_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    answer_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    last_hit_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    hit_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_answer_cache", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "assistant_conversations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_conversations", x => x.id);
                    table.ForeignKey(
                        name: "fk_assistant_conversations_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "assistant_evaluation_candidates",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    interaction_id = table.Column<string>(type: "text", nullable: false),
                    question = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    actual_route = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    expected_route = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reviewed_by_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    reviewed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_evaluation_candidates", x => x.id);
                    table.ForeignKey(
                        name: "fk_assistant_evaluation_candidates_assistant_interactions_inte~",
                        column: x => x.interaction_id,
                        principalTable: "assistant_interactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_assistant_evaluation_candidates_users_reviewed_by_id",
                        column: x => x.reviewed_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "assistant_routing_shadow_samples",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    query_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    primary_route = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    primary_confidence = table.Column<double>(type: "double precision", nullable: false),
                    shadow_route = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    shadow_confidence = table.Column<double>(type: "double precision", nullable: false),
                    primary_model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    shadow_model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    agreed = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_routing_shadow_samples", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "assistant_messages",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    conversation_id = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    content = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    route = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    interaction_id = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_assistant_messages_assistant_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "assistant_conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_interactions_conversation_id",
                table: "assistant_interactions",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_answer_cache_user_scope_expires_at",
                table: "assistant_answer_cache",
                columns: new[] { "user_scope", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_answer_cache_user_scope_query_fingerprint",
                table: "assistant_answer_cache",
                columns: new[] { "user_scope", "query_fingerprint" });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_conversations_user_id_updated_at",
                table: "assistant_conversations",
                columns: new[] { "user_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_evaluation_candidates_interaction_id",
                table: "assistant_evaluation_candidates",
                column: "interaction_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assistant_evaluation_candidates_reviewed_by_id",
                table: "assistant_evaluation_candidates",
                column: "reviewed_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_evaluation_candidates_status_created_at",
                table: "assistant_evaluation_candidates",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_messages_conversation_id_created_at",
                table: "assistant_messages",
                columns: new[] { "conversation_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_routing_shadow_samples_created_at",
                table: "assistant_routing_shadow_samples",
                column: "created_at");

            migrationBuilder.AddForeignKey(
                name: "fk_assistant_interactions_assistant_conversations_conversation~",
                table: "assistant_interactions",
                column: "conversation_id",
                principalTable: "assistant_conversations",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_assistant_interactions_assistant_conversations_conversation~",
                table: "assistant_interactions");

            migrationBuilder.DropTable(
                name: "assistant_answer_cache");

            migrationBuilder.DropTable(
                name: "assistant_evaluation_candidates");

            migrationBuilder.DropTable(
                name: "assistant_messages");

            migrationBuilder.DropTable(
                name: "assistant_routing_shadow_samples");

            migrationBuilder.DropTable(
                name: "assistant_conversations");

            migrationBuilder.DropIndex(
                name: "ix_assistant_interactions_conversation_id",
                table: "assistant_interactions");

            migrationBuilder.DropColumn(
                name: "application_version",
                table: "assistant_interactions");

            migrationBuilder.DropColumn(
                name: "classifier_model",
                table: "assistant_interactions");

            migrationBuilder.DropColumn(
                name: "confidence_calibration_samples",
                table: "assistant_interactions");

            migrationBuilder.DropColumn(
                name: "conversation_id",
                table: "assistant_interactions");

            migrationBuilder.DropColumn(
                name: "raw_confidence",
                table: "assistant_interactions");

            migrationBuilder.DropColumn(
                name: "routing_config_snapshot_json",
                table: "assistant_interactions");

            migrationBuilder.DropColumn(
                name: "routing_prompt_version",
                table: "assistant_interactions");
        }
    }
}
