using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public class FullTextSearchService(AppDbContext db, IConfiguration config, ILogger<FullTextSearchService> logger)
{
    public record FtsResult(string ArticleId, double Rank);

    // Built-in Turkish snowball configuration (stemming + stopwords). Accent folding is
    // done in C# via SlugHelper.Transliterate, applied symmetrically at index and query
    // time — no PostgreSQL extension (unaccent) required.
    private const string TsConfig = "turkish";

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
            .WherePublished()
            .Select(a => new { a.Id, a.Title, a.Excerpt, a.Content })
            .ToListAsync(ct);

        foreach (var article in articles)
            await UpdateSearchVectorAsync(article.Id, article.Title ?? "", article.Excerpt, article.Content, ct);

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
            await RemoveArticleAsync(article.Id);
            return;
        }

        await UpdateSearchVectorAsync(article.Id, article.Title, article.Excerpt, article.Content);
    }

    /// <summary>Recomputes the weighted tsvector (title=A, excerpt=B, content+attachments=C) for one article.</summary>
    private async Task UpdateSearchVectorAsync(string articleId, string title, string? excerpt, string? contentJson, CancellationToken ct = default)
    {
        var attachmentText = await AttachmentHelper.GetAttachmentTextAsync(db, config, articleId, ct);
        // Weight C carries only body + attachment text — title/excerpt already live in A/B;
        // repeating them here would inflate their effective weight
        var contentText = string.Join(". ",
            new[] { ContentExtractor.ExtractPlainText(contentJson), attachmentText }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
        await db.Database.ExecuteSqlRawAsync(
            $$"""
            UPDATE articles SET search_vector =
                setweight(to_tsvector('{{TsConfig}}', COALESCE({0}, '')), 'A') ||
                setweight(to_tsvector('{{TsConfig}}', COALESCE({1}, '')), 'B') ||
                setweight(to_tsvector('{{TsConfig}}', COALESCE({2}, '')), 'C')
            WHERE "Id" = {3}
            """,
            SlugHelper.Transliterate(title),
            SlugHelper.Transliterate(excerpt ?? ""),
            SlugHelper.Transliterate(contentText),
            articleId);
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
    /// Precision-first: all terms must match (AND); when that yields nothing, retries with
    /// any-term matching (OR), then falls back to ILIKE on title/excerpt.
    /// </summary>
    public async Task<List<FtsResult>> SearchAsync(string query, int limit)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var tokens = TokenizeQuery(query);
        if (tokens.Count == 0) return [];

        var results = await RunTsQueryAsync(string.Join(" & ", tokens), limit);

        if (results.Count == 0 && tokens.Count > 1)
            results = await RunTsQueryAsync(string.Join(" | ", tokens), limit);

        // Fallback to ILIKE if tsquery yields no results
        if (results.Count == 0)
        {
            var likePattern = $"%{SlugHelper.EscapeLikePattern(query)}%";
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

    private async Task<List<FtsRawResult>> RunTsQueryAsync(string tsQuery, int limit)
        => await db.Database
            .SqlQueryRaw<FtsRawResult>(
                $$"""
                SELECT "Id" AS "ArticleId", ts_rank_cd(search_vector, to_tsquery('{{TsConfig}}', {0})) AS "Rank"
                FROM articles
                WHERE search_vector IS NOT NULL AND search_vector @@ to_tsquery('{{TsConfig}}', {0})
                ORDER BY "Rank" DESC
                LIMIT {1}
                """,
                tsQuery, limit)
            .ToListAsync();

    /// <summary>
    /// Splits user input into sanitized tsquery lexemes: tsquery meta-characters stripped,
    /// Turkish accents folded (must mirror the indexing side).
    /// </summary>
    private static List<string> TokenizeQuery(string input)
        => input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => SlugHelper.Transliterate(
                t.Replace("'", "").Replace("\\", "").Replace("&", "").Replace("|", "").Replace("!", "").Replace("(", "").Replace(")", "").Replace(":", "").Trim()))
            .Where(t => t.Length > 0)
            .ToList();

    private class FtsRawResult
    {
        public string ArticleId { get; set; } = null!;
        public double Rank { get; set; }
    }
}
