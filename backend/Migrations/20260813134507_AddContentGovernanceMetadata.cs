using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddContentGovernanceMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AuthorityWeight",
                table: "lookup_values",
                type: "integer",
                nullable: false,
                defaultValue: 50);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "articles",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedById",
                table: "articles",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_articles_ApprovedById",
                table: "articles",
                column: "ApprovedById");

            migrationBuilder.AddForeignKey(
                name: "FK_articles_users_ApprovedById",
                table: "articles",
                column: "ApprovedById",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_articles_users_ApprovedById",
                table: "articles");

            migrationBuilder.DropIndex(
                name: "IX_articles_ApprovedById",
                table: "articles");

            migrationBuilder.DropColumn(
                name: "AuthorityWeight",
                table: "lookup_values");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "articles");

            migrationBuilder.DropColumn(
                name: "ApprovedById",
                table: "articles");
        }
    }
}
