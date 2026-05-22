using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
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
        var helpful = await db.ArticleFeedback.CountAsync(f => f.ArticleId == articleId && f.Helpful);
        var notHelpful = await db.ArticleFeedback.CountAsync(f => f.ArticleId == articleId && !f.Helpful);
        return Ok(new { helpful, notHelpful });
    }
}

public record FeedbackRequest(bool Helpful, string? Comment = null);
