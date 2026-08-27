using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantInteractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assistant_interactions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: true),
                    api_key_id = table.Column<string>(type: "text", nullable: true),
                    query_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    route = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    route_source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: false),
                    search_query_id = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: true),
                    tool_calls_json = table.Column<string>(type: "jsonb", nullable: false),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    helpful = table.Column<bool>(type: "boolean", nullable: true),
                    feedback_reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    corrected_route = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    feedback_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_interactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_assistant_interactions_api_keys_api_key_id",
                        column: x => x.api_key_id,
                        principalTable: "api_keys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_assistant_interactions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_interactions_api_key_id",
                table: "assistant_interactions",
                column: "api_key_id");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_interactions_created_at",
                table: "assistant_interactions",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_assistant_interactions_route_created_at",
                table: "assistant_interactions",
                columns: new[] { "route", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_interactions_user_id_created_at",
                table: "assistant_interactions",
                columns: new[] { "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assistant_interactions");
        }
    }
}
