using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDynamicLlmModelSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "preferred_llm_model",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "system_settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    updated_by_id = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_settings", x => x.key);
                    table.ForeignKey(
                        name: "fk_system_settings_users_updated_by_id",
                        column: x => x.updated_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_system_settings_updated_by_id",
                table: "system_settings",
                column: "updated_by_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "system_settings");

            migrationBuilder.DropColumn(
                name: "preferred_llm_model",
                table: "users");
        }
    }
}
