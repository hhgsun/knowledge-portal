using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class EmbeddingVector1024 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 768-dim vectors cannot be cast to vector(1024); wipe them and let the
            // embedding background service re-index every article with the new model.
            migrationBuilder.Sql("""DELETE FROM article_embeddings;""");
            migrationBuilder.Sql("""UPDATE articles SET "IndexedAt" = NULL;""");

            migrationBuilder.AlterColumn<Vector>(
                name: "Embedding",
                table: "article_embeddings",
                type: "vector(1024)",
                nullable: false,
                oldClrType: typeof(Vector),
                oldType: "vector(768)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DELETE FROM article_embeddings;""");
            migrationBuilder.Sql("""UPDATE articles SET "IndexedAt" = NULL;""");

            migrationBuilder.AlterColumn<Vector>(
                name: "Embedding",
                table: "article_embeddings",
                type: "vector(768)",
                nullable: false,
                oldClrType: typeof(Vector),
                oldType: "vector(1024)");
        }
    }
}
