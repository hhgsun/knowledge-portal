using System.Diagnostics;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/search")]
[Authorize]
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

        // Parse @tag syntax
        string? tagSlug = null;
        var searchQuery = q.Trim();
        if (searchQuery.StartsWith('@'))
        {
            var parts = searchQuery.Split(' ', 2);
            tagSlug = parts[0][1..]; // Remove @
            searchQuery = parts.Length > 1 ? parts[1].Trim() : "";
        }

        // Tag-based search
        if (tagSlug != null)
        {
            var tag = await db.Tags.FirstOrDefaultAsync(t => t.Slug == tagSlug);
            if (tag == null)
                return Ok(new { results = Array.Empty<object>(), query = q, type = "tag", tag = tagSlug, responseTimeMs = sw.ElapsedMilliseconds, total = 0 });

            var tagArticleIds = await db.ArticleTags
                .Where(at => at.TagId == tag.Id)
                .Select(at => at.ArticleId)
                .ToListAsync();

            var tagQuery = db.Articles
                .Where(a => tagArticleIds.Contains(a.Id) && a.Status == "published");

            if (!string.IsNullOrWhiteSpace(searchQuery))
                tagQuery = tagQuery.Where(a => a.Title.Contains(searchQuery) || (a.Excerpt != null && a.Excerpt.Contains(searchQuery)));

            var tagResults = await tagQuery
                .OrderByDescending(a => a.UpdatedAt)
                .Take(limit)
                .Select(a => new { a.Id, a.Title, a.Slug, a.Excerpt, a.ContentType, a.Difficulty, UpdatedAt = a.UpdatedAt.ToString("o") })
                .ToListAsync();

            sw.Stop();
            var tagSearchType = string.IsNullOrWhiteSpace(searchQuery) ? "tag" : "tag-search";

            // Record search
            db.SearchQueries.Add(new SearchQuery
            {
                Query = q.Trim(),
                UserId = User.Identity?.IsAuthenticated == true ? User.GetUserId() : null,
                ResultsCount = tagResults.Count,
                SearchType = tagSearchType,
                ResponseTimeMs = (int)sw.ElapsedMilliseconds
            });
            await db.SaveChangesAsync();

            return Ok(new { results = tagResults, query = q, type = tagSearchType, tag = tagSlug, responseTimeMs = sw.ElapsedMilliseconds, total = tagResults.Count });
        }

        // Standard search (SQL LIKE for now — semantic/hybrid/rag in future phase)
        var results = await db.Articles
            .Where(a => a.Status == "published" &&
                (a.Title.Contains(searchQuery) || (a.Excerpt != null && a.Excerpt.Contains(searchQuery))))
            .OrderByDescending(a => a.UpdatedAt)
            .Take(limit)
            .Select(a => new { a.Id, a.Title, a.Slug, a.Excerpt, a.ContentType, a.Difficulty, UpdatedAt = a.UpdatedAt.ToString("o") })
            .ToListAsync();

        sw.Stop();

        // Record search
        db.SearchQueries.Add(new SearchQuery
        {
            Query = q.Trim(),
            UserId = User.Identity?.IsAuthenticated == true ? User.GetUserId() : null,
            ResultsCount = results.Count,
            SearchType = type switch { "fulltext" or "hybrid" or "semantic" => type, _ => "fulltext" },
            ResponseTimeMs = (int)sw.ElapsedMilliseconds
        });
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

        return Ok(new { results, query = q, type, responseTimeMs = sw.ElapsedMilliseconds, total = results.Count });
    }
}
