using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgePortal.Api.Migrations
{
    /// <summary>
    /// Copies the columns an article filter needs onto article_embeddings, so a filtered
    /// semantic search no longer has to join articles inside the distance-ordered scan.
    ///
    /// Why: with the join present the planner joins first and sorts afterwards, discarding the
    /// HNSW index — verified with scripts/hnsw_recall_benchmark.sql section 4, where the very
    /// same table and query gave "Index Scan using ...hnsw" unfiltered and a Seq Scan + Sort
    /// once the join appeared. At millions of chunks that is the difference between an index
    /// lookup and a full scan of every embedding, and it also makes hnsw.iterative_scan inert,
    /// because that setting only applies to an actual index scan.
    ///
    /// The copies are maintained by triggers rather than application code on purpose: article
    /// owner, content type and tags are mutated from several controllers and the MCP tools, and
    /// a single missed call site would not fail — it would silently filter search results by
    /// stale data. Triggers cannot be bypassed and run in the writer's own transaction.
    /// The application never writes these columns; they are deliberately absent from the
    /// ArticleEmbedding entity so nothing is tempted to.
    ///
    /// Scale note: the backfill below is a single UPDATE over every embedding row. That is fine
    /// for a small table; on a corpus with millions of chunks run it in batches instead, or the
    /// migration will hold a long transaction and leave the table full of dead tuples.
    /// </summary>
    public partial class DenormalizeEmbeddingFilterColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE article_embeddings
                    ADD COLUMN IF NOT EXISTS "OwnerId" text,
                    ADD COLUMN IF NOT EXISTS "ContentType" text,
                    ADD COLUMN IF NOT EXISTS "CreatedViaApiKeyId" text,
                    ADD COLUMN IF NOT EXISTS "TagSlugs" text[];
                """);

            // Shared helper so the four trigger paths cannot drift apart in how they build the
            // tag array. Sorted for stable comparisons; '{}' rather than NULL so the containment
            // operator (@>) behaves for untagged articles instead of yielding NULL.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION article_tag_slugs(p_article_id text)
                RETURNS text[] AS $$
                    SELECT COALESCE(array_agg(t."Slug" ORDER BY t."Slug"), '{}')
                    FROM article_tags at2
                    JOIN tags t ON t."Id" = at2."TagId"
                    WHERE at2."ArticleId" = p_article_id;
                $$ LANGUAGE sql STABLE;
                """);

            migrationBuilder.Sql("""
                UPDATE article_embeddings e
                SET "OwnerId"            = a."OwnerId",
                    "ContentType"        = a."ContentType",
                    "CreatedViaApiKeyId" = a."CreatedViaApiKeyId",
                    "TagSlugs"           = article_tag_slugs(a."Id")
                FROM articles a
                WHERE a."Id" = e."ArticleId";
                """);

            // New chunks fill themselves from the article. BEFORE INSERT rather than app-side
            // assignment so a correct row is guaranteed no matter who does the insert.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION article_embeddings_fill_filters()
                RETURNS trigger AS $$
                BEGIN
                    SELECT a."OwnerId", a."ContentType", a."CreatedViaApiKeyId"
                      INTO NEW."OwnerId", NEW."ContentType", NEW."CreatedViaApiKeyId"
                    FROM articles a
                    WHERE a."Id" = NEW."ArticleId";

                    NEW."TagSlugs" := article_tag_slugs(NEW."ArticleId");
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                DROP TRIGGER IF EXISTS trg_article_embeddings_fill_filters ON article_embeddings;
                CREATE TRIGGER trg_article_embeddings_fill_filters
                BEFORE INSERT ON article_embeddings
                FOR EACH ROW EXECUTE FUNCTION article_embeddings_fill_filters();
                """);

            // Article edits propagate to its chunks. The WHEN clause keeps an ordinary edit
            // (title, body, IndexedAt) from touching article_embeddings at all.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION articles_sync_embedding_filters()
                RETURNS trigger AS $$
                BEGIN
                    UPDATE article_embeddings
                    SET "OwnerId"            = NEW."OwnerId",
                        "ContentType"        = NEW."ContentType",
                        "CreatedViaApiKeyId" = NEW."CreatedViaApiKeyId"
                    WHERE "ArticleId" = NEW."Id";
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                DROP TRIGGER IF EXISTS trg_articles_sync_embedding_filters ON articles;
                CREATE TRIGGER trg_articles_sync_embedding_filters
                AFTER UPDATE OF "OwnerId", "ContentType", "CreatedViaApiKeyId" ON articles
                FOR EACH ROW
                WHEN (OLD."OwnerId"            IS DISTINCT FROM NEW."OwnerId"
                   OR OLD."ContentType"        IS DISTINCT FROM NEW."ContentType"
                   OR OLD."CreatedViaApiKeyId" IS DISTINCT FROM NEW."CreatedViaApiKeyId")
                EXECUTE FUNCTION articles_sync_embedding_filters();
                """);

            // Tag link changes. Handles UPDATE too (a re-pointed link touches two articles), and
            // covers tag deletion as well, since removing a tag cascades into article_tags.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION article_tags_sync_embedding_filters()
                RETURNS trigger AS $$
                BEGIN
                    IF TG_OP <> 'INSERT' THEN
                        UPDATE article_embeddings
                        SET "TagSlugs" = article_tag_slugs(OLD."ArticleId")
                        WHERE "ArticleId" = OLD."ArticleId";
                    END IF;

                    IF TG_OP <> 'DELETE' THEN
                        UPDATE article_embeddings
                        SET "TagSlugs" = article_tag_slugs(NEW."ArticleId")
                        WHERE "ArticleId" = NEW."ArticleId";
                    END IF;

                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                DROP TRIGGER IF EXISTS trg_article_tags_sync_embedding_filters ON article_tags;
                CREATE TRIGGER trg_article_tags_sync_embedding_filters
                AFTER INSERT OR UPDATE OR DELETE ON article_tags
                FOR EACH ROW EXECUTE FUNCTION article_tags_sync_embedding_filters();
                """);

            // Renaming a tag changes the slug every one of its articles is filtered by.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION tags_sync_embedding_filters()
                RETURNS trigger AS $$
                BEGIN
                    UPDATE article_embeddings e
                    SET "TagSlugs" = article_tag_slugs(e."ArticleId")
                    WHERE e."ArticleId" IN (SELECT "ArticleId" FROM article_tags WHERE "TagId" = NEW."Id");
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                DROP TRIGGER IF EXISTS trg_tags_sync_embedding_filters ON tags;
                CREATE TRIGGER trg_tags_sync_embedding_filters
                AFTER UPDATE OF "Slug" ON tags
                FOR EACH ROW WHEN (OLD."Slug" IS DISTINCT FROM NEW."Slug")
                EXECUTE FUNCTION tags_sync_embedding_filters();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_tags_sync_embedding_filters ON tags;
                DROP TRIGGER IF EXISTS trg_article_tags_sync_embedding_filters ON article_tags;
                DROP TRIGGER IF EXISTS trg_articles_sync_embedding_filters ON articles;
                DROP TRIGGER IF EXISTS trg_article_embeddings_fill_filters ON article_embeddings;
                DROP FUNCTION IF EXISTS tags_sync_embedding_filters();
                DROP FUNCTION IF EXISTS article_tags_sync_embedding_filters();
                DROP FUNCTION IF EXISTS articles_sync_embedding_filters();
                DROP FUNCTION IF EXISTS article_embeddings_fill_filters();
                DROP FUNCTION IF EXISTS article_tag_slugs(text);

                ALTER TABLE article_embeddings
                    DROP COLUMN IF EXISTS "OwnerId",
                    DROP COLUMN IF EXISTS "ContentType",
                    DROP COLUMN IF EXISTS "CreatedViaApiKeyId",
                    DROP COLUMN IF EXISTS "TagSlugs";
                """);
        }
    }
}
