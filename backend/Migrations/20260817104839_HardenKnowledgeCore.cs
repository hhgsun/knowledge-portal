using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class HardenKnowledgeCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_article_versions_ArticleId",
                table: "article_versions");

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "articles",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FtsIndexedAt",
                table: "articles",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VersionCounter",
                table: "articles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ExtractedSegmentsJson",
                table: "article_attachments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractedText",
                table: "article_attachments",
                type: "text",
                nullable: true);

            // Normalize any historical duplicate version numbers before enforcing the invariant,
            // then initialize the atomic per-article allocator from the actual history.
            migrationBuilder.Sql("""
                WITH numbered AS (
                    SELECT "Id", ROW_NUMBER() OVER (
                        PARTITION BY "ArticleId" ORDER BY "CreatedAt", "Id")::int AS next_version
                    FROM article_versions
                )
                UPDATE article_versions v SET "Version" = numbered.next_version
                FROM numbered WHERE numbered."Id" = v."Id";

                UPDATE articles a SET "VersionCounter" = COALESCE((
                    SELECT MAX(v."Version") FROM article_versions v WHERE v."ArticleId" = a."Id"
                ), 0);
                """);

            // search_vector is intentionally managed by raw SQL rather than the EF model.
            // Preserve the known-good FTS state when upgrading instead of marking the whole
            // corpus dirty; a missing vector remains NULL and is picked up by the durable queue.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'articles' AND column_name = 'search_vector'
                    ) THEN
                        UPDATE articles SET "FtsIndexedAt" = COALESCE("IndexedAt", "UpdatedAt")
                        WHERE search_vector IS NOT NULL;
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_articles_ExternalId",
                table: "articles",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_articles_Status_FtsIndexedAt",
                table: "articles",
                columns: new[] { "Status", "FtsIndexedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_article_versions_ArticleId_Version",
                table: "article_versions",
                columns: new[] { "ArticleId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_articles_ExternalId",
                table: "articles");

            migrationBuilder.DropIndex(
                name: "IX_articles_Status_FtsIndexedAt",
                table: "articles");

            migrationBuilder.DropIndex(
                name: "IX_article_versions_ArticleId_Version",
                table: "article_versions");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "articles");

            migrationBuilder.DropColumn(
                name: "FtsIndexedAt",
                table: "articles");

            migrationBuilder.DropColumn(
                name: "VersionCounter",
                table: "articles");

            migrationBuilder.DropColumn(
                name: "ExtractedSegmentsJson",
                table: "article_attachments");

            migrationBuilder.DropColumn(
                name: "ExtractedText",
                table: "article_attachments");

            migrationBuilder.CreateIndex(
                name: "IX_article_versions_ArticleId",
                table: "article_versions",
                column: "ArticleId");
        }
    }
}
