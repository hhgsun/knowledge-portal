using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSlug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "users",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // Backfill slugs from Id to ensure uniqueness before adding unique index
            migrationBuilder.Sql("UPDATE users SET Slug = LOWER(REPLACE(Id, ' ', '-')) WHERE Slug = ''");

            migrationBuilder.CreateIndex(
                name: "IX_users_Slug",
                table: "users",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_Slug",
                table: "users");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "users");
        }
    }
}
