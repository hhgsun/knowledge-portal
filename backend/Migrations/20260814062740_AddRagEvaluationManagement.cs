using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRagEvaluationManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rag_evaluation_datasets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CasesJson = table.Column<string>(type: "jsonb", nullable: false),
                    ThresholdsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rag_evaluation_datasets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rag_evaluation_runs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    DatasetId = table.Column<string>(type: "text", nullable: false),
                    RequestedById = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TotalCases = table.Column<int>(type: "integer", nullable: false),
                    CompletedCases = table.Column<int>(type: "integer", nullable: false),
                    MetricsJson = table.Column<string>(type: "jsonb", nullable: true),
                    ResultsJson = table.Column<string>(type: "jsonb", nullable: true),
                    Error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rag_evaluation_runs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rag_evaluation_runs_rag_evaluation_datasets_DatasetId",
                        column: x => x.DatasetId,
                        principalTable: "rag_evaluation_datasets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_rag_evaluation_runs_users_RequestedById",
                        column: x => x.RequestedById,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_rag_evaluation_datasets_Name",
                table: "rag_evaluation_datasets",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rag_evaluation_runs_DatasetId",
                table: "rag_evaluation_runs",
                column: "DatasetId");

            migrationBuilder.CreateIndex(
                name: "IX_rag_evaluation_runs_RequestedById",
                table: "rag_evaluation_runs",
                column: "RequestedById");

            migrationBuilder.CreateIndex(
                name: "IX_rag_evaluation_runs_Status_CreatedAt",
                table: "rag_evaluation_runs",
                columns: new[] { "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rag_evaluation_runs");

            migrationBuilder.DropTable(
                name: "rag_evaluation_datasets");
        }
    }
}
