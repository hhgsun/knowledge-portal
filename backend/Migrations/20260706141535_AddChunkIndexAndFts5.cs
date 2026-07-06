using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddChunkIndexAndFts5 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_article_embeddings_ArticleId",
                table: "article_embeddings");

            migrationBuilder.AddColumn<int>(
                name: "ChunkIndex",
                table: "article_embeddings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_article_embeddings_ArticleId_ChunkIndex",
                table: "article_embeddings",
                columns: new[] { "ArticleId", "ChunkIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_article_embeddings_ArticleId_ChunkIndex",
                table: "article_embeddings");

            migrationBuilder.DropColumn(
                name: "ChunkIndex",
                table: "article_embeddings");

            migrationBuilder.CreateIndex(
                name: "IX_article_embeddings_ArticleId",
                table: "article_embeddings",
                column: "ArticleId",
                unique: true);
        }
    }
}
