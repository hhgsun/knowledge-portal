using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public class FullTextSearchService(AppDbContext db, ILogger<FullTextSearchService> logger)
{
    public record FtsResult(string ArticleId, double Rank);

    /// <summary>
    /// Initialize the FTS5 table and populate with all published articles.
    /// Called once at startup.
    /// </summary>
    public async Task InitializeAsync()
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE VIRTUAL TABLE IF NOT EXISTS articles_fts USING fts5(
                article_id UNINDEXED,
                title,
                excerpt,
                content_text,
                tokenize='unicode61 remove_diacritics 2'
            )
            """);
        logger.LogInformation("FTS5 table ensured");
    }

    /// <summary>
    /// Rebuild the entire FTS index from scratch.
    /// </summary>
    public async Task RebuildAsync(CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync("DELETE FROM articles_fts", ct);

        var articles = await db.Articles
            .Where(a => a.Status == "published")
            .Select(a => new { a.Id, a.Title, a.Excerpt, a.Content })
            .ToListAsync(ct);

        foreach (var article in articles)
        {
            var contentText = ContentExtractor.ExtractSearchableText(article.Title ?? "", article.Excerpt, article.Content);
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO articles_fts(article_id, title, excerpt, content_text) VALUES ({0}, {1}, {2}, {3})",
                article.Id, article.Title ?? "", article.Excerpt ?? "", contentText);
        }

        logger.LogInformation("FTS5 index rebuilt with {Count} articles", articles.Count);
    }

    /// <summary>
    /// Sync a single article to the FTS index (upsert).
    /// Call when an article is published or content changes.
    /// </summary>
    public async Task SyncArticleAsync(Article article)
    {
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM articles_fts WHERE article_id = {0}", article.Id);

        if (article.Status == "published")
        {
            var contentText = ContentExtractor.ExtractSearchableText(article.Title, article.Excerpt, article.Content);
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO articles_fts(article_id, title, excerpt, content_text) VALUES ({0}, {1}, {2}, {3})",
                article.Id, article.Title ?? "", article.Excerpt ?? "", contentText);
        }
    }

    /// <summary>
    /// Remove an article from the FTS index.
    /// </summary>
    public async Task RemoveArticleAsync(string articleId)
    {
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM articles_fts WHERE article_id = {0}", articleId);
    }

    /// <summary>
    /// Search the FTS5 index. Returns article IDs ranked by BM25 relevance.
    /// </summary>
    public async Task<List<FtsResult>> SearchAsync(string query, int limit)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        // Escape FTS5 special characters and build query
        var ftsQuery = BuildFtsQuery(query);
        if (string.IsNullOrWhiteSpace(ftsQuery)) return [];

        var results = await db.Database
            .SqlQueryRaw<FtsRawResult>(
                "SELECT article_id AS ArticleId, rank AS Rank FROM articles_fts WHERE articles_fts MATCH {0} ORDER BY rank LIMIT {1}",
                ftsQuery, limit)
            .ToListAsync();

        // FTS5 rank is negative (more negative = more relevant), convert to positive score
        return results.Select(r => new FtsResult(r.ArticleId, -r.Rank)).ToList();
    }

    private static string BuildFtsQuery(string input)
    {
        // Split into tokens and wrap each in quotes to handle special chars
        var tokens = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return "";

        // Use OR between tokens for broader matching, with column weighting
        var escaped = tokens.Select(t => $"\"{t.Replace("\"", "\"\"")}\"");
        return string.Join(" OR ", escaped);
    }

    private class FtsRawResult
    {
        public string ArticleId { get; set; } = null!;
        public double Rank { get; set; }
    }
}
