using System.Diagnostics;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/search")]
[Authorize]
[EnableRateLimiting("search")]
public class SearchController(AppDbContext db, IConfiguration config, ArticleService articleService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] string type = "fulltext",
        [FromQuery] int limit = 20,
        [FromQuery] bool onlyOwnContent = false,
        [FromQuery] bool includeContent = false,
        [FromQuery] bool includeAttachments = false,
        [FromQuery] List<string>? tag = null,
        [FromQuery] List<string>? author = null,
        [FromQuery] List<string>? contentType = null)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "Query parameter 'q' is required" });

        limit = Math.Clamp(limit, 1, 50);
        var sw = Stopwatch.StartNew();

        // Parse inline syntax: ## → contentType, # → tag, @ → user
        var tagSlugs = new List<string>();
        var authorSlugs = new List<string>();
        var contentTypeSlugs = new List<string>();
        var searchQuery = q.Trim();
        var words = searchQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var remainingWords = new List<string>();
        foreach (var word in words)
        {
            if (word.StartsWith("##") && word.Length > 2)
                contentTypeSlugs.Add(word[2..]);
            else if (word.StartsWith('#') && word.Length > 1)
                tagSlugs.Add(word[1..]);
            else if (word.StartsWith('@') && word.Length > 1)
                authorSlugs.Add(word[1..]);
            else
                remainingWords.Add(word);
        }
        searchQuery = string.Join(' ', remainingWords).Trim();

        // Merge query parameters with inline syntax
        if (tag?.Count > 0)
            tagSlugs.AddRange(tag.SelectMany(t => t.Split(',')).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()));
        tagSlugs = tagSlugs.Distinct().ToList();

        if (author?.Count > 0)
            authorSlugs.AddRange(author.SelectMany(a => a.Split(',')).Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim()));
        authorSlugs = authorSlugs.Distinct().ToList();

        // Resolve author slugs to IDs (OR logic — articles from any of these authors)
        List<string>? authorFilterIds = authorSlugs.Count > 0
            ? await db.ResolveAuthorIdsAsync(authorSlugs)
            : null;

        // Resolve contentType slugs (OR logic — merge with query param)
        List<string>? contentTypeFilter = contentTypeSlugs.Count > 0 ? contentTypeSlugs.Distinct().ToList() : null;
        if (contentType?.Count > 0)
        {
            contentTypeFilter ??= [];
            foreach (var ct in contentType.SelectMany(c => c.Split(',')).Select(c => c.Trim()).Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                if (!contentTypeFilter.Contains(ct))
                    contentTypeFilter.Add(ct);
            }
        }

        // API key scoping: when onlyOwnContent=true and request via API key, filter to that key's articles
        var callerApiKeyId = User.FindFirst("apiKeyId")?.Value;
        var scopedApiKeyId = onlyOwnContent && callerApiKeyId != null ? callerApiKeyId : null;

        // Resolve tag article IDs for filtering
        List<string>? tagFilterArticleIds = null;
        if (tagSlugs.Count > 0)
        {
            var tags = await db.Tags.Where(t => tagSlugs.Contains(t.Slug)).ToListAsync();
            if (tags.Count == 0)
            {
                sw.Stop();
                return Ok(new { results = Array.Empty<object>(), query = q, type = "tag", tags = tagSlugs, responseTimeMs = sw.ElapsedMilliseconds, total = 0 });
            }

            var foundTagIds = tags.Select(t => t.Id).ToList();
            tagFilterArticleIds = await db.ArticleTags
                .Where(at => foundTagIds.Contains(at.TagId))
                .GroupBy(at => at.ArticleId)
                .Where(g => g.Count() >= foundTagIds.Count)
                .Select(g => g.Key)
                .ToListAsync();
        }

        // All resolved filters, applied uniformly to every search flavor below
        var filter = new ArticleFilter(authorFilterIds, contentTypeFilter, scopedApiKeyId, tagFilterArticleIds);

        // Tag-only search (no remaining query text or explicit tag type)
        if (tagSlugs.Count > 0 && string.IsNullOrWhiteSpace(searchQuery))
        {
            var tagQuery = ArticleService.ApplyFilter(db.Articles.WherePublished(), filter);

            var tagResultsRaw = await tagQuery.OrderByDescending(a => a.UpdatedAt).Take(limit)
                .Select(a => new { a.Id, a.Title, a.Slug, a.Excerpt, a.ContentType, a.Content, UpdatedAt = a.UpdatedAt.ToString("o") })
                .ToListAsync();
            var tagAttachmentMap = includeAttachments ? await AttachmentHelper.GetAttachmentMapAsync(db, tagResultsRaw.Select(a => a.Id).ToList()) : null;
            var tagEnrichment = await articleService.GetEnrichmentAsync(tagResultsRaw.Select(a => a.Id));
            var tagResults = tagResultsRaw.Select(a => BuildResult(a.Id, a.Title, a.Slug, a.Excerpt, a.ContentType, a.Content, a.UpdatedAt, includeContent, tagAttachmentMap, tagEnrichment.GetValueOrDefault(a.Id))).ToList();

            sw.Stop();
            var tagSearchRecord = await RecordSearchAsync(q, tagResults.Count, "tag", sw.ElapsedMilliseconds);
            return Ok(new { results = tagResults, query = q, type = "tag", tags = tagSlugs, responseTimeMs = sw.ElapsedMilliseconds, total = tagResults.Count, searchQueryId = tagSearchRecord.Id });
        }

        // Resolve AI services
        var ollamaEnabled = config.GetValue("Ollama:Enabled", false);
        var vectorSearch = ollamaEnabled ? HttpContext.RequestServices.GetService<VectorSearchService>() : null;
        var indexingPending = await db.Articles.AnyAsync(a => a.Status == "published" && a.IndexedAt == null);

        // ═══ RAG ═══
        if (type == "rag")
        {
            if (!ollamaEnabled || vectorSearch == null)
            {
                sw.Stop();
                return Ok(new { answer = "AI arama şu anda kullanılamıyor. Ollama servisi aktif değil.", sources = Array.Empty<object>(), query = q, type = "rag", responseTimeMs = sw.ElapsedMilliseconds, indexingPending });
            }

            var ragService = HttpContext.RequestServices.GetService<RagService>();
            if (ragService == null)
            {
                sw.Stop();
                return Ok(new { answer = "RAG servisi kullanılamıyor.", sources = Array.Empty<object>(), query = q, type = "rag", responseTimeMs = sw.ElapsedMilliseconds, indexingPending });
            }

            try
            {
                var ragResult = await ragService.AskAsync(searchQuery);
                sw.Stop();
                var ragRecord = await RecordSearchAsync(q, ragResult.Sources.Count, "rag", sw.ElapsedMilliseconds);
                return Ok(new { answer = ragResult.Answer, sources = ragResult.Sources.Select(s => new { s.ArticleId, s.Title, s.Slug, s.Score }), query = q, type = "rag", responseTimeMs = sw.ElapsedMilliseconds, indexingPending, searchQueryId = ragRecord.Id });
            }
            catch (Exception ex)
            {
                sw.Stop();
                return Ok(new { answer = "AI yanıtı oluşturulurken bir hata oluştu: " + ex.Message, sources = Array.Empty<object>(), query = q, type = "rag", responseTimeMs = sw.ElapsedMilliseconds, indexingPending });
            }
        }

        // ═══ SEMANTIC ═══
        if (type == "semantic")
        {
            if (!ollamaEnabled || vectorSearch == null)
            {
                sw.Stop();
                return Ok(new { results = Array.Empty<object>(), query = q, type = "semantic", responseTimeMs = sw.ElapsedMilliseconds, total = 0, indexingPending, warning = "Semantic search unavailable — Ollama disabled" });
            }

            try
            {
                var semanticResults = await vectorSearch.SearchAsync(searchQuery, limit);
                var articleIds = semanticResults.Select(r => r.ArticleId).ToList();
                var semQuery = ArticleService.ApplyFilter(db.Articles.WherePublished().Where(a => articleIds.Contains(a.Id)), filter);
                var articles = await semQuery
                    .Select(a => new { a.Id, a.Title, a.Slug, a.Excerpt, a.ContentType, a.Content, UpdatedAt = a.UpdatedAt.ToString("o") })
                    .ToListAsync();

                var semAttachmentMap = includeAttachments ? await AttachmentHelper.GetAttachmentMapAsync(db, articles.Select(a => a.Id).ToList()) : null;
                var semEnrichment = await articleService.GetEnrichmentAsync(articles.Select(a => a.Id));
                var scoredResults = semanticResults
                    .Select(sr => { var a = articles.FirstOrDefault(a => a.Id == sr.ArticleId); return a == null ? null : BuildResult(a.Id, a.Title, a.Slug, a.Excerpt, a.ContentType, a.Content, a.UpdatedAt, includeContent, semAttachmentMap, semEnrichment.GetValueOrDefault(a.Id), Math.Round(sr.Score, 4)); })
                    .Where(r => r != null).ToList();

                sw.Stop();
                var semRecord = await RecordSearchAsync(q, scoredResults.Count, "semantic", sw.ElapsedMilliseconds);
                return Ok(new { results = scoredResults, query = q, type = "semantic", responseTimeMs = sw.ElapsedMilliseconds, total = scoredResults.Count, indexingPending, searchQueryId = semRecord.Id });
            }
            catch
            {
                sw.Stop();
                return Ok(new { results = Array.Empty<object>(), query = q, type = "semantic", responseTimeMs = sw.ElapsedMilliseconds, total = 0, indexingPending, warning = "Semantic search failed" });
            }
        }

        // ═══ HYBRID (full-text + semantic via RRF) ═══
        if (type == "hybrid")
        {
            // Full-text leg (rank order + LIKE fallback handled by the service)
            var fulltextResults = (await articleService.SearchPublishedAsync(searchQuery, limit, filter))
                .Select(a => a.Id)
                .ToList();

            List<VectorSearchService.VectorSearchResult>? semanticHits = null;
            if (ollamaEnabled && vectorSearch != null)
            {
                try { semanticHits = await vectorSearch.SearchAsync(searchQuery, limit); }
                catch { /* semantic unavailable — fulltext only */ }
            }

            // RRF merge
            const int k = 60;
            const double alphaFulltext = 0.4;
            const double alphaSemantic = 0.6;
            var rrfScores = new Dictionary<string, (double Score, string MatchType)>();

            for (int i = 0; i < fulltextResults.Count; i++)
                rrfScores[fulltextResults[i]] = (alphaFulltext / (k + i + 1), "fulltext");

            if (semanticHits != null)
            {
                for (int i = 0; i < semanticHits.Count; i++)
                {
                    var id = semanticHits[i].ArticleId;
                    var score = alphaSemantic / (k + i + 1);
                    if (rrfScores.TryGetValue(id, out var existing))
                        rrfScores[id] = (existing.Score + score, "both");
                    else
                        rrfScores[id] = (score, "semantic");
                }
            }

            var allIds = rrfScores.Keys.ToList();
            var hybridMergeQuery = ArticleService.ApplyFilter(db.Articles.WherePublished().Where(a => allIds.Contains(a.Id)), filter);
            var allArticles = await hybridMergeQuery
                .Select(a => new { a.Id, a.Title, a.Slug, a.Excerpt, a.ContentType, a.Content, UpdatedAt = a.UpdatedAt.ToString("o") })
                .ToListAsync();

            var hybridAttachmentMap = includeAttachments ? await AttachmentHelper.GetAttachmentMapAsync(db, allArticles.Select(a => a.Id).ToList()) : null;
            var hybridEnrichment = await articleService.GetEnrichmentAsync(allArticles.Select(a => a.Id));
            var hybridResults = rrfScores.OrderByDescending(kv => kv.Value.Score).Take(limit)
                .Select(kv => { var a = allArticles.FirstOrDefault(a => a.Id == kv.Key); return a == null ? null : BuildResult(a.Id, a.Title, a.Slug, a.Excerpt, a.ContentType, a.Content, a.UpdatedAt, includeContent, hybridAttachmentMap, hybridEnrichment.GetValueOrDefault(a.Id), Math.Round(kv.Value.Score, 4), kv.Value.MatchType); })
                .Where(r => r != null).ToList();

            sw.Stop();
            var hybridRecord = await RecordSearchAsync(q, hybridResults.Count, "hybrid", sw.ElapsedMilliseconds);

            var warning = semanticHits == null && ollamaEnabled ? "Semantic search unavailable — using fulltext only" : (string?)null;
            return Ok(new { results = hybridResults, query = q, type = "hybrid", responseTimeMs = sw.ElapsedMilliseconds, total = hybridResults.Count, indexingPending, searchQueryId = hybridRecord.Id, warning });
        }

        // ═══ FULLTEXT (default) — rank order + LIKE fallback handled by the service ═══
        var ftArticles = await articleService.SearchPublishedAsync(searchQuery, limit, filter);
        var ftAttachmentMap = includeAttachments ? await AttachmentHelper.GetAttachmentMapAsync(db, ftArticles.Select(a => a.Id).ToList()) : null;
        var ftEnrichment = await articleService.GetEnrichmentAsync(ftArticles.Select(a => a.Id));
        var ftFinalResults = ftArticles
            .Select(a => BuildResult(a.Id, a.Title, a.Slug, a.Excerpt, a.ContentType, a.Content, a.UpdatedAt.ToString("o"), includeContent, ftAttachmentMap, ftEnrichment.GetValueOrDefault(a.Id)))
            .ToList();

        sw.Stop();
        var ftRecord = await RecordSearchAsync(q, ftFinalResults.Count, "fulltext", sw.ElapsedMilliseconds);
        return Ok(new { results = ftFinalResults, query = q, type = "fulltext", responseTimeMs = sw.ElapsedMilliseconds, total = ftFinalResults.Count, indexingPending, searchQueryId = ftRecord.Id });
    }

    [HttpPost("click")]
    public async Task<IActionResult> RecordClick([FromBody] RecordClickRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.SearchQueryId) || string.IsNullOrWhiteSpace(req.ArticleId))
            return BadRequest(new { error = "searchQueryId and articleId are required" });

        var searchQuery = await db.SearchQueries.FindAsync(req.SearchQueryId);
        if (searchQuery == null)
            return NotFound(new { error = "Search query not found" });

        var userId = User.GetUserId();
        if (searchQuery.UserId != userId)
            return StatusCode(403, new { error = "Cannot update another user's search query" });

        searchQuery.ClickedArticleId = req.ArticleId;
        await db.SaveChangesAsync();
        return Ok(new { message = "Click recorded" });
    }

    [HttpPost("reindex")]
    [RequirePermission(Permissions.UsersManage)]
    [RequireSessionAuth]
    public async Task<IActionResult> Reindex()
    {
        if (!config.GetValue("Ollama:Enabled", false))
            return StatusCode(503, new { error = "Ollama is not enabled" });

        var count = await db.Articles.WherePublished()
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IndexedAt, (DateTime?)null));
        await db.ArticleEmbeddings.ExecuteDeleteAsync();

        // Rebuild FTS index
        await articleService.RebuildIndexAsync();

        return Ok(new { message = "Reindex queued", articlesQueued = count });
    }

    [HttpGet("embedding-status")]
    [RequirePermission(Permissions.UsersManage)]
    [RequireSessionAuth]
    public async Task<IActionResult> EmbeddingStatus()
    {
        var totalPublished = await db.Articles.CountAsync(a => a.Status == "published");
        var totalIndexed = await db.Articles.CountAsync(a => a.Status == "published" && a.IndexedAt != null);

        return Ok(new
        {
            totalPublished,
            totalIndexed,
            pendingCount = totalPublished - totalIndexed,
            ollamaEnabled = config.GetValue("Ollama:Enabled", false),
            modelName = config["Ollama:EmbeddingModel"] ?? "nomic-embed-text"
        });
    }

    [HttpGet("authors")]
    public async Task<IActionResult> Authors()
    {
        var authors = await db.Users
            .Select(u => new { u.Id, u.Name, u.Slug })
            .OrderBy(u => u.Name)
            .ToListAsync();
        return Ok(authors);
    }

    private async Task<SearchQuery> RecordSearchAsync(string query, int resultsCount, string searchType, long elapsedMs)
    {
        var record = new SearchQuery
        {
            Query = query.Trim(),
            UserId = User.Identity?.IsAuthenticated == true ? User.GetUserId() : null,
            ResultsCount = resultsCount,
            SearchType = searchType,
            ResponseTimeMs = (int)elapsedMs
        };
        db.SearchQueries.Add(record);
        await db.SaveChangesAsync();
        return record;
    }

    private static SearchResultDto BuildResult(string id, string title, string slug, string? excerpt, string contentType, string? content, string updatedAt, bool includeContent, Dictionary<string, List<object>>? attachmentMap, ArticleEnrichment? enrichment, double? score = null, string? matchType = null)
    {
        return new SearchResultDto(
            id, title, slug, excerpt, contentType, updatedAt,
            enrichment?.Status,
            enrichment?.OwnerName,
            enrichment?.ApiKeyName,
            enrichment?.Tags,
            enrichment?.ViewCount ?? 0,
            enrichment?.WilsonScore ?? 0.0,
            score,
            matchType,
            includeContent ? ContentExtractor.ExtractPlainText(content) : null,
            attachmentMap?.GetValueOrDefault(id));
    }
}

