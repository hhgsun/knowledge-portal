using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAttachmentIndexInclusionFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "include_in_index",
                table: "article_attachments",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // Preserve the behaviour of already-extracted legacy Markdown duplicates while
            // making the persisted flag the sole runtime decision from this migration onward.
            migrationBuilder.Sql(
                """
                UPDATE article_attachments AS attachment
                SET include_in_index = FALSE
                FROM articles AS article
                WHERE attachment.article_id = article.id
                  AND lower(attachment.file_name) LIKE '%.md'
                  AND attachment.extraction_truncated = FALSE
                  AND attachment.extracted_text IS NOT NULL
                  AND article.content IS NOT NULL
                  AND btrim(replace(attachment.extracted_text, E'\r', ''), E' \t\n') =
                      btrim(replace(article.content, E'\r', ''), E' \t\n');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "include_in_index",
                table: "article_attachments");
        }
    }
}
