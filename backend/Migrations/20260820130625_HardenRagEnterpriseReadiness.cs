using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class HardenRagEnterpriseReadiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "rag_evaluation_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CasesSnapshotJson",
                table: "rag_evaluation_runs",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "DatasetVersion",
                table: "rag_evaluation_runs",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "legacy");

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAt",
                table: "rag_evaluation_runs",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RuntimeSnapshotJson",
                table: "rag_evaluation_runs",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "ThresholdsSnapshotJson",
                table: "rag_evaluation_runs",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "WorkerId",
                table: "rag_evaluation_runs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE rag_evaluation_runs r
                SET "DatasetVersion" = d."Version",
                    "CasesSnapshotJson" = d."CasesJson",
                    "ThresholdsSnapshotJson" = d."ThresholdsJson",
                    "RuntimeSnapshotJson" = '{"promptVersion":"legacy"}'::jsonb
                FROM rag_evaluation_datasets d
                WHERE d."Id" = r."DatasetId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "rag_evaluation_runs");

            migrationBuilder.DropColumn(
                name: "CasesSnapshotJson",
                table: "rag_evaluation_runs");

            migrationBuilder.DropColumn(
                name: "DatasetVersion",
                table: "rag_evaluation_runs");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "rag_evaluation_runs");

            migrationBuilder.DropColumn(
                name: "RuntimeSnapshotJson",
                table: "rag_evaluation_runs");

            migrationBuilder.DropColumn(
                name: "ThresholdsSnapshotJson",
                table: "rag_evaluation_runs");

            migrationBuilder.DropColumn(
                name: "WorkerId",
                table: "rag_evaluation_runs");
        }
    }
}
