using System.Diagnostics;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
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
public class SearchController(AppDbContext db, IConfiguration config) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] string type = "fulltext",
        [FromQuery] int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "Query parameter 'q' is required" });

        limit = Math.Clamp(limit, 1, 50);
        var sw = Stopwatch.StartNew();

        // Parse @tag syntax
        var tagSlugs = new List<string>();
        var searchQuery = q.Trim();
        var words = searchQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var remainingWords = new List<string>();
        foreach (var word in words)
        {
            if (word.StartsWith('@') && word.Length > 1) tagSlugs.Add(word[1..]);
            else remainingWords.Add(word);
        }
        searchQuery = string.Join(' ', remainingWords).Trim();

        // Tag-based search
        if (tagSlugs.Count > 0)
        {
            var tags = await db.Tags.Where(t => tagSlugs.Contains(t.Slug)).ToListAsync();
            if (tags.Count == 0)
                return Ok(new { results = Array.Empty<object>(), query = q, type = "tag", tags = tagSlugs, responseTimeMs = sw.ElapsedMilliseconds, total = 0 });

            var foundTagIds = tags.Select(t => t.Id).ToList();
            var tagArticleIds = await db.ArticleTags
                .Where(at => foundTagIds.Contains(at.TagId))
                .GroupBy(at => at.ArticleId)
                .Where(g => g.Count() >= foundTagIds.Count)
                .Select(g => g.Key)
                .ToListAsync();

            var tagQuery = db.Articles.Where(a => tagArticleIds.Contains(a.Id) && a.Status == "published");
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                var esc = searchQuery.Replace("%", "\\%").Replace("_", "\\_");
                tagQuery = tagQuery.Where(a => EF.Functions.Like(a.Title, $"%{esc}%", "\\") || (a.Excerpt != null && EF.Functions.Like(a.Excerpt, $"%{esc}%", "\\")));
            }

            var tagResults = await tagQuery.OrderByDescending(a => a.UpdatedAt).Take(limit)
                .Select(a => new { a.Id, a.Title, a.Slug, a.Excerpt, a.ContentType, UpdatedAt = a.UpdatedAt.ToString("o") })
                .ToListAsync();

            sw.Stop();
            var tagSearchType = string.IsNullOrWhiteSpace(searchQuery) ? "tag" : "tag-search";
            var tagSearchRecord = new SearchQuery { Query = q.Trim(), UserId = User.Identity?.IsAuthenticated == true ? User.GetUserId() : null, ResultsCount = tagResults.Count, SearchType = tagSearchType, ResponseTimeMs = (int)sw.ElapsedMilliseconds };
            db.SearchQueries.Add(tagSearchRecord);
            await db.SaveChangesAsync();
            return Ok(new { results = tagResults, query = q, type = tagSearchType, tags = tagSlugs, responseTimeMs = sw.ElapsedMilliseconds, total = tagResults.Count, searchQueryId = tagSearchRecord.Id });
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
                var ragRecord = new SearchQuery { Query = q.Trim(), UserId = User.Identity?.IsAuthenticated == true ? User.GetUserId() : null, ResultsCount = ragResult.Sources.Count, SearchType = "rag", ResponseTimeMs = (int)sw.ElapsedMilliseconds };
                db.SearchQueries.Add(ragRecord);
                await db.SaveChangesAsync();
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
                var articles = await db.Articles
                    .Where(a => articleIds.Contains(a.Id) && a.Status == "published")
                    .Select(a => new { a.Id, a.Title, a.Slug, a.Excerpt, a.ContentType, UpdatedAt = a.UpdatedAt.ToString("o") })
                    .ToListAsync();

                var scoredResults = semanticResults
                    .Select(sr => { var a = articles.FirstOrDefault(a => a.Id == sr.ArticleId); return a == null ? null : new { a.Id, a.Title, a.Slug, a.Excerpt, a.ContentType, a.UpdatedAt, Score = Math.Round(sr.Score, 4) }; })
                    .Where(r => r != null).ToList();

                sw.Stop();
                var semRecord = new SearchQuery { Query = q.Trim(), UserId = User.Identity?.IsAuthenticated == true ? User.GetUserId() : null, ResultsCount = scoredResults.Count, SearchType = "semantic", ResponseTimeMs = (int)sw.ElapsedMilliseconds };
                db.SearchQueries.Add(semRecord);
                await db.SaveChangesAsync();
                return Ok(new { results = scoredResults, query = q, type = "semantic", responseTimeMs = sw.ElapsedMilliseconds, total = scoredResults.Count, indexingPending, searchQueryId = semRecord.Id });
            }
            catch
            {
                sw.Stop();
                return Ok(new { results = Array.Empty<object>(), query = q, type = "semantic", responseTimeMs = sw.ElapsedMilliseconds, total = 0, indexingPending, warning = "Semantic search failed" });
            }
        }

        // ═══ HYBRID (fulltext + semantic via RRF) ═══
        if (type == "hybrid")
        {
            var escapedHybrid = searchQuery.Replace("%", "\\%").Replace("_", "\\_");
            var fulltextTask = db.Articles
                .Where(a => a.Status == "published" && (EF.Functions.Like(a.Title, $"%{escapedHybrid}%", "\\") || (a.Excerpt != null && EF.Functions.Like(a.Excerpt, $"%{escapedHybrid}%", "\\"))))
                .OrderByDescending(a => a.UpdatedAt).Take(limit)
                .Select(a => new { a.Id, a.Title, a.Slug, a.Excerpt, a.ContentType, UpdatedAt = a.UpdatedAt.ToString("o") })
                .ToListAsync();

            List<VectorSearchService.VectorSearchResult>? semanticHits = null;
            if (ollamaEnabled && vectorSearch != null)
            {
                try { semanticHits = await vectorSearch.SearchAsync(searchQuery, limit); }
                catch { /* semantic unavailable — fulltext only */ }
            }

            var fulltextResults = await fulltextTask;

            // RRF merge
            const int k = 60;
            const double alphaFulltext = 0.4;
            const double alphaSemantic = 0.6;
            var rrfScores = new Dictionary<string, (double Score, string MatchType)>();

            for (int i = 0; i < fulltextResults.Count; i++)
                rrfScores[fulltextResults[i].Id] = (alphaFulltext / (k + i + 1), "fulltext");

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
            var allArticles = await db.Articles
                .Where(a => allIds.Contains(a.Id) && a.Status == "published")
                .Select(a => new { a.Id, a.Title, a.Slug, a.Excerpt, a.ContentType, UpdatedAt = a.UpdatedAt.ToString("o") })
                .ToListAsync();

            var hybridResults = rrfScores.OrderByDescending(kv => kv.Value.Score).Take(limit)
                .Select(kv => { var a = allArticles.FirstOrDefault(a => a.Id == kv.Key); return a == null ? null : new { a.Id, a.Title, a.Slug, a.Excerpt, a.ContentType, a.UpdatedAt, Score = Math.Round(kv.Value.Score, 4), MatchType = kv.Value.MatchType }; })
                .Where(r => r != null).ToList();

            sw.Stop();
            var hybridRecord = new SearchQuery { Query = q.Trim(), UserId = User.Identity?.IsAuthenticated == true ? User.GetUserId() : null, ResultsCount = hybridResults.Count, SearchType = "hybrid", ResponseTimeMs = (int)sw.ElapsedMilliseconds };
            db.SearchQueries.Add(hybridRecord);
            await db.SaveChangesAsync();

            var warning = semanticHits == null && ollamaEnabled ? "Semantic search unavailable — using fulltext only" : (string?)null;
            return Ok(new { results = hybridResults, query = q, type = "hybrid", responseTimeMs = sw.ElapsedMilliseconds, total = hybridResults.Count, indexingPending, searchQueryId = hybridRecord.Id, warning });
        }

        // ═══ FULLTEXT (default) ═══
        var escapedSearch = searchQuery.Replace("%", "\\%").Replace("_", "\\_");
        var ftResults = await db.Articles
            .Where(a => a.Status == "published" && (EF.Functions.Like(a.Title, $"%{escapedSearch}%", "\\") || (a.Excerpt != null && EF.Functions.Like(a.Excerpt, $"%{escapedSearch}%", "\\"))))
            .OrderByDescending(a => a.UpdatedAt).Take(limit)
            .Select(a => new { a.Id, a.Title, a.Slug, a.Excerpt, a.ContentType, UpdatedAt = a.UpdatedAt.ToString("o") })
            .ToListAsync();

        sw.Stop();
        var ftRecord = new SearchQuery { Query = q.Trim(), UserId = User.Identity?.IsAuthenticated == true ? User.GetUserId() : null, ResultsCount = ftResults.Count, SearchType = "fulltext", ResponseTimeMs = (int)sw.ElapsedMilliseconds };
        db.SearchQueries.Add(ftRecord);
        await db.SaveChangesAsync();
        return Ok(new { results = ftResults, query = q, type = "fulltext", responseTimeMs = sw.ElapsedMilliseconds, total = ftResults.Count, indexingPending, searchQueryId = ftRecord.Id });
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
    public async Task<IActionResult> Reindex()
    {
        if (User.GetSource() == "api-key")
            return StatusCode(403, new { error = "This endpoint requires session authentication" });
        if (!config.GetValue("Ollama:Enabled", false))
            return StatusCode(503, new { error = "Ollama is not enabled" });

        var count = await db.Articles.Where(a => a.Status == "published")
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IndexedAt, (DateTime?)null));
        await db.ArticleEmbeddings.ExecuteDeleteAsync();

        var vectorSearch = HttpContext.RequestServices.GetService<VectorSearchService>();
        vectorSearch?.InvalidateCache();

        return Ok(new { message = "Reindex queued", articlesQueued = count });
    }

    [HttpGet("embedding-status")]
    [RequirePermission(Permissions.UsersManage)]
    public async Task<IActionResult> EmbeddingStatus()
    {
        if (User.GetSource() == "api-key")
            return StatusCode(403, new { error = "This endpoint requires session authentication" });

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
}

