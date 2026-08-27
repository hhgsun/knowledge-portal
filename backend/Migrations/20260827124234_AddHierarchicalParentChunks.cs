using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHierarchicalParentChunks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "parent_chunk_id",
                table: "article_embeddings",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "article_chunk_parents",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    article_id = table.Column<string>(type: "text", nullable: false),
                    parent_index = table.Column<int>(type: "integer", nullable: false),
                    source_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attachment_id = table.Column<string>(type: "text", nullable: true),
                    source_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source_location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    content = table.Column<string>(type: "text", nullable: false),
                    text_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    word_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_article_chunk_parents", x => x.id);
                    table.ForeignKey(
                        name: "fk_article_chunk_parents_article_attachments_attachment_id",
                        column: x => x.attachment_id,
                        principalTable: "article_attachments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_article_chunk_parents_articles_article_id",
                        column: x => x.article_id,
                        principalTable: "articles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_article_embeddings_parent_chunk_id",
                table: "article_embeddings",
                column: "parent_chunk_id");

            migrationBuilder.CreateIndex(
                name: "ix_article_chunk_parents_article_id_parent_index",
                table: "article_chunk_parents",
                columns: new[] { "article_id", "parent_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_article_chunk_parents_attachment_id",
                table: "article_chunk_parents",
                column: "attachment_id");

            migrationBuilder.AddForeignKey(
                name: "fk_article_embeddings_article_chunk_parents_parent_chunk_id",
                table: "article_embeddings",
                column: "parent_chunk_id",
                principalTable: "article_chunk_parents",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_article_embeddings_article_chunk_parents_parent_chunk_id",
                table: "article_embeddings");

            migrationBuilder.DropTable(
                name: "article_chunk_parents");

            migrationBuilder.DropIndex(
                name: "ix_article_embeddings_parent_chunk_id",
                table: "article_embeddings");

            migrationBuilder.DropColumn(
                name: "parent_chunk_id",
                table: "article_embeddings");
        }
    }
}
