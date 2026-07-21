using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <inheritdoc />
    public partial class RebuildEmbeddingHnswIndexTuned : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rebuild the HNSW index with explicit build parameters. The default index
            // (created in AddEmbeddingHnswIndex) uses pgvector defaults (m=16, ef_construction=64),
            // which loses recall at 50k+ articles / hundreds of thousands of chunks. A higher
            // ef_construction builds a denser graph → better recall for the same query-time
            // ef_search (set per-query in VectorSearchService). One-time rebuild; on a large
            // table this can take several minutes.
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS ix_article_embeddings_embedding_hnsw;
                CREATE INDEX IF NOT EXISTS ix_article_embeddings_embedding_hnsw
                ON article_embeddings USING hnsw ("Embedding" vector_cosine_ops)
                WITH (m = 16, ef_construction = 200);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS ix_article_embeddings_embedding_hnsw;
                CREATE INDEX IF NOT EXISTS ix_article_embeddings_embedding_hnsw
                ON article_embeddings USING hnsw ("Embedding" vector_cosine_ops);
                """);
        }
    }
}
