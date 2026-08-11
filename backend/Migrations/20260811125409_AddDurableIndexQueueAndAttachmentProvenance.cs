using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableIndexQueueAndAttachmentProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttachmentId",
                table: "article_embeddings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceLocation",
                table: "article_embeddings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceName",
                table: "article_embeddings",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "article_embeddings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "article");

            migrationBuilder.AddColumn<DateTime>(
                name: "ExtractedAt",
                table: "article_attachments",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractionError",
                table: "article_attachments",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractionStatus",
                table: "article_attachments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "pending");

            migrationBuilder.AddColumn<string>(
                name: "Sha256",
                table: "article_attachments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "index_jobs",
                columns: table => new
                {
                    ArticleId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Generation = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    AvailableAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LockedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LockedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastError = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_index_jobs", x => x.ArticleId);
                    table.ForeignKey(
                        name: "FK_index_jobs_articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_article_embeddings_AttachmentId",
                table: "article_embeddings",
                column: "AttachmentId");

            migrationBuilder.CreateIndex(
                name: "IX_index_jobs_Status_AvailableAt_Priority",
                table: "index_jobs",
                columns: new[] { "Status", "AvailableAt", "Priority" });

            migrationBuilder.AddForeignKey(
                name: "FK_article_embeddings_article_attachments_AttachmentId",
                table: "article_embeddings",
                column: "AttachmentId",
                principalTable: "article_attachments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_article_embeddings_article_attachments_AttachmentId",
                table: "article_embeddings");

            migrationBuilder.DropTable(
                name: "index_jobs");

            migrationBuilder.DropIndex(
                name: "IX_article_embeddings_AttachmentId",
                table: "article_embeddings");

            migrationBuilder.DropColumn(
                name: "AttachmentId",
                table: "article_embeddings");

            migrationBuilder.DropColumn(
                name: "SourceLocation",
                table: "article_embeddings");

            migrationBuilder.DropColumn(
                name: "SourceName",
                table: "article_embeddings");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "article_embeddings");

            migrationBuilder.DropColumn(
                name: "ExtractedAt",
                table: "article_attachments");

            migrationBuilder.DropColumn(
                name: "ExtractionError",
                table: "article_attachments");

            migrationBuilder.DropColumn(
                name: "ExtractionStatus",
                table: "article_attachments");

            migrationBuilder.DropColumn(
                name: "Sha256",
                table: "article_attachments");
        }
    }
}
