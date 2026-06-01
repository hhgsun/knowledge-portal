using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDeadColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "session_id",
                table: "article_views");

            migrationBuilder.DropColumn(
                name: "permissions",
                table: "api_keys");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "session_id",
                table: "article_views",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "permissions",
                table: "api_keys",
                type: "TEXT",
                nullable: true);
        }
    }
}
