using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/articles/{articleId}/feedback")]
[Authorize]
public class ArticleFeedbackController(AppDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(string articleId, [FromBody] FeedbackRequest req)
    {
        if (!await db.Articles.AnyAsync(a => a.Id == articleId))
            return NotFound(new { error = "Article not found" });

        db.ArticleFeedback.Add(new ArticleFeedback
        {
            ArticleId = articleId,
            UserId = User.GetUserId(),
            Helpful = req.Helpful,
            Comment = req.Comment?.Trim()
        });
        await db.SaveChangesAsync();

        return StatusCode(201, new { message = "Feedback submitted" });
    }

    [HttpGet]
    public async Task<IActionResult> Get(string articleId)
    {
        var feedbacks = await db.ArticleFeedback
            .Where(f => f.ArticleId == articleId)
            .Include(f => f.User)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync();

        var helpful = feedbacks.Count(f => f.Helpful);
        var notHelpful = feedbacks.Count(f => !f.Helpful);
        var comments = feedbacks
            .Where(f => !string.IsNullOrWhiteSpace(f.Comment))
            .Select(f => new
            {
                f.Id,
                f.Helpful,
                f.Comment,
                userName = f.User?.Name ?? "Unknown",
                f.CreatedAt
            })
            .ToList();

        return Ok(new { helpful, notHelpful, comments });
    }
}

