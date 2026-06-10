using System.Diagnostics;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/search")]
[Authorize]
[EnableRateLimiting("search")]
public class SearchController(AppDbContext db) : ControllerBase
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

        // Parse @tag syntax (supports multiple: @tag1 @tag2 query)
        var tagSlugs = new List<string>();
        var searchQuery = q.Trim();
        var words = searchQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var remainingWords = new List<string>();
        foreach (var word in words)
        {
            if (word.StartsWith('@') && word.Length > 1)
                tagSlugs.Add(word[1..]);
            else
                remainingWords.Add(word);
        }
        searchQuery = string.Join(' ', remainingWords).Trim();

        // Tag-based search
        if (tagSlugs.Count > 0)
        {
            var tags = await db.Tags.Where(t => tagSlugs.Contains(t.Slug)).ToListAsync();
            if (tags.Count == 0)
                return Ok(new { results = Array.Empty<object>(), query = q, type = "tag", tags = tagSlugs, responseTimeMs = sw.ElapsedMilliseconds, total = 0 });

            // Find articles that have ALL specified tags (AND logic)
            var foundTagIds = tags.Select(t => t.Id).ToList();
            var tagArticleIds = await db.ArticleTags
                .Where(at => foundTagIds.Contains(at.TagId))
                .GroupBy(at => at.ArticleId)
                .Where(g => g.Count() >= foundTagIds.Count)
                .Select(g => g.Key)
                .ToListAsync();

            var tagQuery = db.Articles
                .Where(a => tagArticleIds.Contains(a.Id) && a.Status == "published");

            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                var esc = searchQuery.Replace("%", "\\%").Replace("_", "\\_");
                tagQuery = tagQuery.Where(a => EF.Functions.Like(a.Title, $"%{esc}%", "\\") || (a.Excerpt != null && EF.Functions.Like(a.Excerpt, $"%{esc}%", "\\")));
            }

            var tagResults = await tagQuery
                .OrderByDescending(a => a.UpdatedAt)
                .Take(limit)
                .Select(a => new { a.Id, a.Title, a.Slug, a.Excerpt, a.ContentType, a.Difficulty, UpdatedAt = a.UpdatedAt.ToString("o") })
                .ToListAsync();

            sw.Stop();
            var tagSearchType = string.IsNullOrWhiteSpace(searchQuery) ? "tag" : "tag-search";

            // Record search
            var tagSearchRecord = new SearchQuery
            {
                Query = q.Trim(),
                UserId = User.Identity?.IsAuthenticated == true ? User.GetUserId() : null,
                ResultsCount = tagResults.Count,
                SearchType = tagSearchType,
                ResponseTimeMs = (int)sw.ElapsedMilliseconds
            };
            db.SearchQueries.Add(tagSearchRecord);
            await db.SaveChangesAsync();

            return Ok(new { results = tagResults, query = q, type = tagSearchType, tags = tagSlugs, responseTimeMs = sw.ElapsedMilliseconds, total = tagResults.Count, searchQueryId = tagSearchRecord.Id });
        }

        // Standard search (SQL LIKE for now — semantic/hybrid/rag in future phase)
        var escapedSearch = searchQuery.Replace("%", "\\%").Replace("_", "\\_");
        var results = await db.Articles
            .Where(a => a.Status == "published" &&
                (EF.Functions.Like(a.Title, $"%{escapedSearch}%", "\\") || (a.Excerpt != null && EF.Functions.Like(a.Excerpt, $"%{escapedSearch}%", "\\"))))
            .OrderByDescending(a => a.UpdatedAt)
            .Take(limit)
            .Select(a => new { a.Id, a.Title, a.Slug, a.Excerpt, a.ContentType, a.Difficulty, UpdatedAt = a.UpdatedAt.ToString("o") })
            .ToListAsync();

        sw.Stop();

        // Record search
        var searchRecord = new SearchQuery
        {
            Query = q.Trim(),
            UserId = User.Identity?.IsAuthenticated == true ? User.GetUserId() : null,
            ResultsCount = results.Count,
            SearchType = type switch { "fulltext" or "hybrid" or "semantic" => type, _ => "fulltext" },
            ResponseTimeMs = (int)sw.ElapsedMilliseconds
        };
        db.SearchQueries.Add(searchRecord);
        await db.SaveChangesAsync();

        // RAG placeholder
        if (type == "rag")
        {
            return Ok(new
            {
                answer = "RAG is not yet available. Semantic search and AI-powered answers will be enabled in a future update.",
                sources = Array.Empty<object>(),
                query = q,
                type = "rag",
                responseTimeMs = sw.ElapsedMilliseconds
            });
        }

        return Ok(new { results, query = q, type, responseTimeMs = sw.ElapsedMilliseconds, total = results.Count, searchQueryId = searchRecord.Id });
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
}

