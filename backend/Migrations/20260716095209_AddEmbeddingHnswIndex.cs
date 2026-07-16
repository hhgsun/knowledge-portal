using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEmbeddingHnswIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ANN index for pgvector cosine distance search (pgvector >= 0.5).
            // Without it every semantic query is a sequential scan over all chunks.
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_article_embeddings_embedding_hnsw
                ON article_embeddings USING hnsw ("Embedding" vector_cosine_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_article_embeddings_embedding_hnsw;");
        }
    }
}
