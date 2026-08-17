using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/articles/{articleId}")]
[Authorize]
public class ArticleFeedbackController(AppDbContext db, ArticleService articleService) : ControllerBase
{
    // ─── Votes ────────────────────────────────────────────────

    [HttpPost("vote")]
    public async Task<IActionResult> Vote(string articleId, [FromBody] VoteRequest req)
    {
        if (await articleService.GetViewableByIdAsync(articleId, User) == null)
            return NotFound(new { error = "Article not found" });

        var userId = User.GetUserId();
        var existing = await db.ArticleVotes
            .FirstOrDefaultAsync(v => v.ArticleId == articleId && v.UserId == userId);

        // If reason provided with helpful=true, ignore it
        var reason = req.IsHelpful ? null : req.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason)) reason = null;

        if (existing != null)
        {
            if (existing.IsHelpful == req.IsHelpful)
            {
                // Same vote → toggle off (remove)
                db.ArticleVotes.Remove(existing);
                await db.SaveChangesAsync();
                return Ok(new { action = "removed" });
            }
            else
            {
                // Different vote → update
                existing.IsHelpful = req.IsHelpful;
                existing.Reason = reason;
                existing.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                return Ok(new { action = "changed" });
            }
        }

        // New vote
        db.ArticleVotes.Add(new ArticleVote
        {
            ArticleId = articleId,
            UserId = userId,
            IsHelpful = req.IsHelpful,
            Reason = reason
        });
        await db.SaveChangesAsync();

        return StatusCode(201, new { action = "created" });
    }

    [HttpDelete("vote")]
    public async Task<IActionResult> RemoveVote(string articleId)
    {
        if (await articleService.GetViewableByIdAsync(articleId, User) == null)
            return NotFound(new { error = "Article not found" });

        var userId = User.GetUserId();
        var existing = await db.ArticleVotes
            .FirstOrDefaultAsync(v => v.ArticleId == articleId && v.UserId == userId);

        if (existing == null)
            return NotFound(new { error = "No vote found" });

        db.ArticleVotes.Remove(existing);
        await db.SaveChangesAsync();
        return Ok(new { message = "Vote removed" });
    }

    [HttpGet("votes")]
    public async Task<IActionResult> GetVotes(string articleId)
    {
        if (await articleService.GetViewableByIdAsync(articleId, User) == null)
            return NotFound(new { error = "Article not found" });

        var userId = User.GetUserId();
        var votes = await db.ArticleVotes
            .Where(v => v.ArticleId == articleId)
            .ToListAsync();

        var helpful = votes.Count(v => v.IsHelpful);
        var notHelpful = votes.Count(v => !v.IsHelpful);
        var wilsonScore = SlugHelper.WilsonScore(helpful, notHelpful);

        var userVote = votes.FirstOrDefault(v => v.UserId == userId);
        bool? userVoteValue = userVote?.IsHelpful;

        var reasons = votes
            .Where(v => !v.IsHelpful && !string.IsNullOrWhiteSpace(v.Reason))
            .Select(v => v.Reason!)
            .ToList();

        return Ok(new { helpful, notHelpful, wilsonScore, userVote = userVoteValue, reasons });
    }

    // ─── Comments ─────────────────────────────────────────────

    [HttpPost("comments")]
    public async Task<IActionResult> CreateComment(string articleId, [FromBody] CommentRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Comment))
            return BadRequest(new { error = "Comment is required" });

        if (await articleService.GetViewableByIdAsync(articleId, User) == null)
            return NotFound(new { error = "Article not found" });

        var userId = User.GetUserId();
        db.ArticleComments.Add(new ArticleComment
        {
            ArticleId = articleId,
            UserId = userId,
            Comment = req.Comment.Trim()
        });
        await db.SaveChangesAsync();

        return StatusCode(201, new { message = "Comment added" });
    }

    [HttpGet("comments")]
    public async Task<IActionResult> GetComments(string articleId)
    {
        if (await articleService.GetViewableByIdAsync(articleId, User) == null)
            return NotFound(new { error = "Article not found" });

        var userId = User.GetUserId();
        var comments = await db.ArticleComments
            .Where(c => c.ArticleId == articleId)
            .Include(c => c.User)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new
            {
                c.Id,
                c.Comment,
                userName = c.User.Name,
                c.CreatedAt,
                isOwn = c.UserId == userId
            })
            .ToListAsync();

        return Ok(new { comments });
    }

    [HttpDelete("comments/{commentId}")]
    [RequireSessionAuth] // destructive deletes are session-only — API keys cannot delete
    public async Task<IActionResult> DeleteComment(string articleId, string commentId)
    {
        if (await articleService.GetViewableByIdAsync(articleId, User) == null)
            return NotFound(new { error = "Article not found" });

        var userId = User.GetUserId();
        var role = User.GetRole();
        var comment = await db.ArticleComments
            .FirstOrDefaultAsync(c => c.Id == commentId && c.ArticleId == articleId);

        if (comment == null)
            return NotFound(new { error = "Comment not found" });

        if (comment.UserId != userId && role != "admin")
            return StatusCode(403, new { error = "You can only delete your own comments" });

        db.ArticleComments.Remove(comment);
        await db.SaveChangesAsync();
        return Ok(new { message = "Comment deleted" });
    }
}

