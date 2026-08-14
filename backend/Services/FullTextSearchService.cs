using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public class FullTextSearchService(AppDbContext db, IConfiguration config, ILogger<FullTextSearchService> logger)
{
    public record FtsResult(string ArticleId, double Rank);

    /// <summary>One ranked page of article IDs plus the true total match count (both post-filter).</summary>
    public record FtsPage(List<string> ArticleIds, int Total);

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
        if (!db.Database.IsRelational()) return; // InMemory (Docker-free tests): LINQ search, no tsvector column
        await db.Database.ExecuteSqlRawAsync("""
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = current_schema()
                      AND table_name = 'articles'
                      AND column_name = 'search_vector'
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

    /// <summary>Articles per pass. Each one re-reads and re-parses that article's attachments
    /// from disk, so this is deliberately small — the batch bounds memory, not IO.</summary>
    private const int IndexBatchSize = 200;

    /// <summary>
    /// Brings the index up to date without ever taking it down: only articles that have no
    /// search_vector are computed. This is what startup calls, so a restart costs one indexed
    /// COUNT when everything is already indexed instead of re-deriving the whole corpus.
    /// Resumable — each batch commits, so an interrupted run continues where it stopped.
    /// </summary>
    public Task<int> EnsureIndexedAsync(CancellationToken ct = default)
        => IndexPublishedAsync(onlyMissing: true, "backfill", ct);

    /// <summary>
    /// Recomputes every published article's vector, for when the indexing rules themselves
    /// change. Unlike the previous version this does NOT blank the index first: each article is
    /// overwritten in place, so search keeps working throughout and merely serves some entries
    /// from the old rules until their turn comes. Blanking first meant search was broken for the
    /// entire length of the rebuild, which grows with the corpus.
    /// </summary>
    public Task<int> RebuildAsync(CancellationToken ct = default)
        => IndexPublishedAsync(onlyMissing: false, "rebuild", ct);

    /// <summary>
    /// Walks published articles by keyset (no OFFSET, which degrades on a large corpus) and
    /// recomputes each one's tsvector. Returns the number of articles indexed.
    /// </summary>
    private async Task<int> IndexPublishedAsync(bool onlyMissing, string what, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return 0; // LINQ search reads live article data — no index

        // Anything that left 'published' must stop being searchable. SyncArticleAsync already
        // handles this per article; this covers rows that changed while the app was down. One
        // indexed statement, unlike the per-article work below.
        await db.Database.ExecuteSqlRawAsync(
            """UPDATE articles SET search_vector = NULL WHERE "Status" <> 'published' AND search_vector IS NOT NULL""", ct);

        var missingOnly = onlyMissing ? "AND search_vector IS NULL" : "";
        var lastId = "";
        var indexed = 0;
        var failed = 0;

        while (!ct.IsCancellationRequested)
        {
            // search_vector is raw-SQL infrastructure, not a mapped property, so the batch of
            // ids has to be selected in SQL; the fields themselves then load through EF.
#pragma warning disable EF1002
            var batchIds = await db.Database.SqlQueryRaw<string>(
                $$"""
                SELECT "Id" AS "Value" FROM articles
                WHERE "Status" = 'published' AND "Id" > {0} {{missingOnly}}
                ORDER BY "Id" LIMIT {1}
                """,
                lastId, IndexBatchSize).ToListAsync(ct);
#pragma warning restore EF1002

            if (batchIds.Count == 0) break;

            var batch = await db.Articles
                .Where(a => batchIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Title, a.Excerpt, a.Content })
                .ToListAsync(ct);

            foreach (var article in batch.OrderBy(a => a.Id, StringComparer.Ordinal))
            {
                try
                {
                    await UpdateSearchVectorAsync(article.Id, article.Title ?? "", article.Excerpt, article.Content, ct);
                    indexed++;
                }
                catch (Exception ex)
                {
                    // One unindexable article must not stop the walk. Postgres caps a tsvector at
                    // 1 MB, which a document with enough attachment text can exceed — and since
                    // this runs at startup, an unhandled failure would both block every article
                    // after it in the keyset and stop the application from booting.
                    failed++;
                    logger.LogError(ex, "Full-text indexing failed for article {ArticleId}", article.Id);
                }
            }

            lastId = batchIds[^1];

            // A large corpus makes this a long job; without progress it looks like a hang.
            if (indexed % (IndexBatchSize * 10) == 0)
                logger.LogInformation("Full-text {What} in progress: {Count} articles indexed", what, indexed);
        }

        if (indexed > 0 || failed > 0)
            logger.LogInformation("Full-text {What} complete: {Count} articles indexed, {Failed} failed",
                what, indexed, failed);
        return indexed;
    }

    /// <summary>
    /// Sync a single article to the FTS index (upsert).
    /// Call when an article is published or content changes.
    /// </summary>
    public async Task SyncArticleAsync(Article article)
    {
        if (!db.Database.IsRelational()) return; // no tsvector column on non-relational providers

        if (article.Status != "published")
        {
            await RemoveArticleAsync(article.Id);
            return;
        }

        await UpdateSearchVectorAsync(article.Id, article.Title, article.Excerpt, article.Content);
    }

    /// <summary>Recomputes the weighted tsvector (title=A, excerpt=B, content+attachments=C) for one article.</summary>
    private async Task UpdateSearchVectorAsync(string articleId, string title, string? excerpt, string? contentMarkdown, CancellationToken ct = default)
    {
        var attachmentText = await AttachmentHelper.GetAttachmentTextAsync(db, config, articleId, ct);
        // Weight C carries only body + attachment text — title/excerpt already live in A/B;
        // repeating them here would inflate their effective weight
        var contentText = string.Join(". ",
            new[] { ContentExtractor.ExtractPlainText(contentMarkdown), attachmentText }
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
        if (!db.Database.IsRelational()) return;
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE articles SET search_vector = NULL WHERE \"Id\" = {0}", articleId);
    }

    /// <summary>
    /// Ranked, filtered and paged full-text search, executed entirely in PostgreSQL.
    /// The filters (author/tag/contentType/API key) live in the same statement as the tsquery
    /// match, so ranking, the total count and paging all operate on the real post-filter match
    /// set. There is deliberately no candidate cap: capping first and filtering afterwards hides
    /// matches whenever a common term hits a large slice of the corpus (a filtered search would
    /// return nothing while thousands of real matches sat below the cap).
    /// Precision-first ladder: all terms (AND) → any term (OR) → ILIKE on title/excerpt. Each
    /// rung is itself filtered, so a rung whose matches are all filtered out falls through to
    /// the next one instead of ending the search.
    /// Postgres-only; see <see cref="SearchInMemoryAsync"/> for the non-relational path.
    /// </summary>
    public async Task<FtsPage> SearchPagedAsync(string query, ArticleFilter? filter, int page, int limit, CancellationToken ct = default)
    {
        var tokens = TokenizeQuery(query);

        if (tokens.Count > 0)
        {
            var andPage = await RunStageAsync(args => TsStage(args, string.Join(" & ", tokens)), filter, page, limit, ct);
            if (andPage.Total > 0) return andPage;

            if (tokens.Count > 1)
            {
                var orPage = await RunStageAsync(args => TsStage(args, string.Join(" | ", tokens)), filter, page, limit, ct);
                if (orPage.Total > 0) return orPage;
            }
        }

        // Also the entry point for a filter-only search (e.g. "@author" with no terms left after
        // stripping): an empty query yields the '%%' pattern, i.e. every filtered article.
        return await RunStageAsync(args => LikeStage(args, query), filter, page, limit, ct);
    }

    /// <summary>How one rung of the ladder matches and orders rows. Both fragments are SQL
    /// built from constants plus positional placeholders — never from user input directly.</summary>
    private sealed record SearchStage(string Match, string OrderBy);

    private static SearchStage TsStage(List<object> args, string tsQuery)
    {
        var q = ArticleFilterSql.Placeholder(args, tsQuery);
        return new SearchStage(
            Match: $"a.search_vector @@ to_tsquery('{TsConfig}', {q})",
            OrderBy: $"""ts_rank_cd(a.search_vector, to_tsquery('{TsConfig}', {q})) DESC, a."Id" """);
    }

    private static SearchStage LikeStage(List<object> args, string query)
    {
        var p = ArticleFilterSql.Placeholder(args, $"%{SlugHelper.EscapeLikePattern(query)}%");
        return new SearchStage(
            Match: $"""(a."Title" ILIKE {p} OR a."Excerpt" ILIKE {p})""",
            OrderBy: """a."UpdatedAt" DESC, a."Id" """);
    }

    /// <summary>
    /// Runs one rung: a COUNT over the full post-filter match set, then — only if anything
    /// matched — the ranked page itself. Counting first keeps a rung that matches nothing from
    /// paying for the rank+sort, and gives the caller a true total for pagination.
    /// </summary>
    private async Task<FtsPage> RunStageAsync(Func<List<object>, SearchStage> buildStage, ArticleFilter? filter, int page, int limit, CancellationToken ct)
    {
        var args = new List<object>();
        var stage = buildStage(args);
        var from = $"""
            FROM articles a
            WHERE a."Status" = 'published' AND {stage.Match}{ArticleFilterSql.Build(filter, args)}
            """;

        // EF1002: the interpolated fragments are SQL skeletons assembled from compile-time
        // constants and {n} placeholders only — every user-supplied value (query terms, owner
        // ids, tag slugs, paging) enters through the args array as a bound parameter.
#pragma warning disable EF1002
        var total = await db.Database
            .SqlQueryRaw<int>($"""SELECT COUNT(*)::int AS "Value" {from}""", args.ToArray())
            .SingleAsync(ct);
        if (total == 0) return new FtsPage([], 0);

        var offset = ArticleFilterSql.Placeholder(args, (page - 1) * limit);
        var take = ArticleFilterSql.Placeholder(args, limit);
        var ids = await db.Database
            .SqlQueryRaw<string>(
                $"""
                SELECT a."Id" AS "Value" {from}
                ORDER BY {stage.OrderBy}
                OFFSET {offset} LIMIT {take}
                """,
                args.ToArray())
            .ToListAsync(ct);
#pragma warning restore EF1002

        return new FtsPage(ids, total);
    }

    /// <summary>
    /// Provider-agnostic full-text fallback for non-relational databases (the Docker-free
    /// InMemory test suite). Mirrors the production semantics that don't depend on the Postgres
    /// snowball stemmer: symmetric accent folding (C# transliteration), title-weighted ranking,
    /// and AND-first precision (all terms) falling back to OR (any term). Turkish stemming
    /// (plural→singular) is Postgres-only and not reproduced here. Filtering and paging are the
    /// caller's job — the InMemory corpus is small enough that the candidate cap is harmless.
    /// </summary>
    public async Task<List<FtsResult>> SearchInMemoryAsync(string query, int limit)
    {
        var tokens = TokenizeQuery(query);
        if (tokens.Count == 0) return [];

        var lowered = tokens.Select(t => t.ToLowerInvariant()).ToList();

        var articles = await db.Articles
            .WherePublished()
            .Select(a => new { a.Id, a.Title, a.Excerpt, a.Content, a.UpdatedAt })
            .ToListAsync();

        var scored = new List<(string Id, double Rank, DateTime Updated)>();
        foreach (var a in articles)
        {
            var titleHay = SlugHelper.Transliterate(a.Title ?? "").ToLowerInvariant();
            var bodyHay = SlugHelper.Transliterate(
                (a.Excerpt ?? "") + " " + ContentExtractor.ExtractPlainText(a.Content)).ToLowerInvariant();
            var haystack = titleHay + " " + bodyHay;

            var matched = lowered.Count(t => haystack.Contains(t));
            if (matched == 0) continue;

            var titleMatches = lowered.Count(t => titleHay.Contains(t));
            var allMatch = matched == lowered.Count;
            // AND-matches float to the top; within a tier, title hits rank higher (weight A > B/C)
            var rank = (allMatch ? 1000.0 : 0.0) + titleMatches * 10.0 + matched;
            scored.Add((a.Id, rank, a.UpdatedAt));
        }

        // Precision-first: if anything matches ALL terms, drop partial (OR) matches
        if (scored.Any(s => s.Rank >= 1000.0))
            scored = scored.Where(s => s.Rank >= 1000.0).ToList();

        return scored
            .OrderByDescending(s => s.Rank).ThenByDescending(s => s.Updated).ThenBy(s => s.Id)
            .Take(limit)
            .Select(s => new FtsResult(s.Id, s.Rank))
            .ToList();
    }

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
}
