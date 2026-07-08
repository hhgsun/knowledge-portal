using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public class FullTextSearchService(AppDbContext db, IConfiguration config, ILogger<FullTextSearchService> logger)
{
    public record FtsResult(string ArticleId, double Rank);

    /// <summary>
    /// Initialize the full-text search infrastructure (search_vector column + GIN index).
    /// Called once at startup.
    /// </summary>
    public async Task InitializeAsync()
    {
        await db.Database.ExecuteSqlRawAsync("""
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_name = 'articles' AND column_name = 'search_vector'
                ) THEN
                    ALTER TABLE articles ADD COLUMN search_vector tsvector;
                END IF;
            END $$;
            """);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS idx_articles_search_vector ON articles USING gin(search_vector);
            """);

        logger.LogInformation("PostgreSQL full-text search infrastructure ensured");
    }

    /// <summary>
    /// Rebuild the entire FTS index from scratch.
    /// </summary>
    public async Task RebuildAsync(CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync("UPDATE articles SET search_vector = NULL", ct);

        var articles = await db.Articles
            .Where(a => a.Status == "published")
            .Select(a => new { a.Id, a.Title, a.Excerpt, a.Content })
            .ToListAsync(ct);

        var basePath = config["FileStorage:BasePath"] ?? "../data/uploads";
        var baseDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), basePath));

        foreach (var article in articles)
        {
            var attachmentText = await GetAttachmentTextAsync(article.Id, baseDir, ct);
            var contentText = ContentExtractor.ExtractSearchableText(article.Title ?? "", article.Excerpt, article.Content, attachmentText);
            await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE articles SET search_vector =
                    setweight(to_tsvector('simple', COALESCE({0}, '')), 'A') ||
                    setweight(to_tsvector('simple', COALESCE({1}, '')), 'B') ||
                    setweight(to_tsvector('simple', COALESCE({2}, '')), 'C')
                WHERE "Id" = {3}
                """,
                article.Title ?? "", article.Excerpt ?? "", contentText, article.Id);
        }

        logger.LogInformation("Full-text search index rebuilt with {Count} articles", articles.Count);
    }

    /// <summary>
    /// Sync a single article to the FTS index (upsert).
    /// Call when an article is published or content changes.
    /// </summary>
    public async Task SyncArticleAsync(Article article)
    {
        if (article.Status != "published")
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE articles SET search_vector = NULL WHERE \"Id\" = {0}", article.Id);
            return;
        }

        var basePath = config["FileStorage:BasePath"] ?? "../data/uploads";
        var baseDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), basePath));
        var attachmentText = await GetAttachmentTextAsync(article.Id, baseDir);

        var contentText = ContentExtractor.ExtractSearchableText(article.Title, article.Excerpt, article.Content, attachmentText);
        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE articles SET search_vector =
                setweight(to_tsvector('simple', COALESCE({0}, '')), 'A') ||
                setweight(to_tsvector('simple', COALESCE({1}, '')), 'B') ||
                setweight(to_tsvector('simple', COALESCE({2}, '')), 'C')
            WHERE "Id" = {3}
            """,
            article.Title ?? "", article.Excerpt ?? "", contentText, article.Id);
    }

    /// <summary>
    /// Remove an article from the FTS index.
    /// </summary>
    public async Task RemoveArticleAsync(string articleId)
    {
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE articles SET search_vector = NULL WHERE \"Id\" = {0}", articleId);
    }

    /// <summary>
    /// Search using PostgreSQL full-text search. Returns article IDs ranked by relevance.
    /// </summary>
    public async Task<List<FtsResult>> SearchAsync(string query, int limit)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var tsQuery = BuildTsQuery(query);
        if (string.IsNullOrWhiteSpace(tsQuery)) return [];

        var results = await db.Database
            .SqlQueryRaw<FtsRawResult>(
                """
                SELECT "Id" AS "ArticleId", ts_rank_cd(search_vector, to_tsquery('simple', {0})) AS "Rank"
                FROM articles
                WHERE search_vector IS NOT NULL AND search_vector @@ to_tsquery('simple', {0})
                ORDER BY "Rank" DESC
                LIMIT {1}
                """,
                tsQuery, limit)
            .ToListAsync();

        // Fallback to ILIKE if tsquery yields no results
        if (results.Count == 0)
        {
            var likePattern = $"%{query.Replace("%", "\\%").Replace("_", "\\_")}%";
            results = await db.Database
                .SqlQueryRaw<FtsRawResult>(
                    """
                    SELECT "Id" AS "ArticleId", 0.1 AS "Rank"
                    FROM articles
                    WHERE "Status" = 'published' AND (
                        "Title" ILIKE {0} OR "Excerpt" ILIKE {0}
                    )
                    LIMIT {1}
                    """,
                    likePattern, limit)
                .ToListAsync();
        }

        return results.Select(r => new FtsResult(r.ArticleId, r.Rank)).ToList();
    }

    private async Task<string> GetAttachmentTextAsync(string articleId, string baseDir, CancellationToken ct = default)
    {
        var attachments = await db.ArticleAttachments
            .Where(a => a.ArticleId == articleId)
            .Select(a => new { a.StoredFileName, a.FileName })
            .ToListAsync(ct);

        if (attachments.Count == 0) return "";

        var sb = new System.Text.StringBuilder();
        var articleDir = Path.Combine(baseDir, articleId);

        foreach (var att in attachments)
        {
            var extension = Path.GetExtension(att.FileName).ToLowerInvariant();
            var filePath = Path.Combine(articleDir, att.StoredFileName);
            var text = AttachmentTextExtractor.ExtractText(filePath, extension);
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.Append(text);
                sb.Append(' ');
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Build a PostgreSQL tsquery string from user input.
    /// Tokens are joined with OR operator for broader matching.
    /// </summary>
    private static string BuildTsQuery(string input)
    {
        var tokens = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return "";

        // Escape special characters and join with | (OR) operator
        var escaped = tokens.Select(t => t.Replace("'", "").Replace("\\", "").Replace("&", "").Replace("|", "").Replace("!", "").Replace("(", "").Replace(")", "").Replace(":", "").Trim())
            .Where(t => t.Length > 0);
        return string.Join(" | ", escaped);
    }

    private class FtsRawResult
    {
        public string ArticleId { get; set; } = null!;
        public double Rank { get; set; }
    }
}
