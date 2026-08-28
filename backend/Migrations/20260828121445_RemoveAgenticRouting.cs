using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAgenticRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assistant_evaluation_candidates");

            migrationBuilder.DropTable(
                name: "assistant_routing_shadow_samples");

            migrationBuilder.DropIndex(
                name: "ix_assistant_interactions_route_created_at",
                table: "assistant_interactions");

            migrationBuilder.DropColumn(
                name: "classifier_model",
                table: "assistant_interactions");

            migrationBuilder.DropColumn(
                name: "confidence",
                table: "assistant_interactions");

            migrationBuilder.DropColumn(
                name: "confidence_calibration_samples",
                table: "assistant_interactions");

            migrationBuilder.DropColumn(
                name: "corrected_route",
                table: "assistant_interactions");

            migrationBuilder.DropColumn(
                name: "raw_confidence",
                table: "assistant_interactions");

            migrationBuilder.DropColumn(
                name: "reason_code",
                table: "assistant_interactions");

            migrationBuilder.DropColumn(
                name: "route",
                table: "assistant_interactions");

            migrationBuilder.DropColumn(
                name: "route_source",
                table: "assistant_interactions");

            migrationBuilder.DropColumn(
                name: "routing_config_snapshot_json",
                table: "assistant_interactions");

            migrationBuilder.DropColumn(
                name: "routing_prompt_version",
                table: "assistant_interactions");

            migrationBuilder.DropColumn(
                name: "search_query_id",
                table: "assistant_interactions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "classifier_model",
                table: "assistant_interactions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "confidence",
                table: "assistant_interactions",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "confidence_calibration_samples",
                table: "assistant_interactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "corrected_route",
                table: "assistant_interactions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "raw_confidence",
                table: "assistant_interactions",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "reason_code",
                table: "assistant_interactions",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "route",
                table: "assistant_interactions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "route_source",
                table: "assistant_interactions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "routing_config_snapshot_json",
                table: "assistant_interactions",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "routing_prompt_version",
                table: "assistant_interactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "search_query_id",
                table: "assistant_interactions",
                type: "character varying(21)",
                maxLength: 21,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "assistant_evaluation_candidates",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    interaction_id = table.Column<string>(type: "text", nullable: false),
                    reviewed_by_id = table.Column<string>(type: "text", nullable: true),
                    actual_route = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    expected_route = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    question = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reviewed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
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
                    agreed = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    primary_confidence = table.Column<double>(type: "double precision", nullable: false),
                    primary_model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    primary_route = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    query_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    shadow_confidence = table.Column<double>(type: "double precision", nullable: false),
                    shadow_model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    shadow_route = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_routing_shadow_samples", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_interactions_route_created_at",
                table: "assistant_interactions",
                columns: new[] { "route", "created_at" });

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
                name: "ix_assistant_routing_shadow_samples_created_at",
                table: "assistant_routing_shadow_samples",
                column: "created_at");
        }
    }
}
