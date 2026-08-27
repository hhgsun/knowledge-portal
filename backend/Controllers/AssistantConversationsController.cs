using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Controllers;

[ApiController]
[Route("api/assistant/conversations")]
[Authorize]
[RequireSessionAuth]
public sealed class AssistantConversationsController(AppDbContext db,
    AssistantConversationService conversations) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        await conversations.PruneExpiredAsync(User.GetUserId(), HttpContext.RequestAborted);
        var items = await db.AssistantConversations.AsNoTracking()
            .Where(x => x.UserId == User.GetUserId()).OrderByDescending(x => x.UpdatedAt)
            .Select(x => new { x.Id, x.Title, x.CreatedAt, x.UpdatedAt,
                messageCount = x.Messages.Count }).Take(100).ToListAsync();
        return Ok(new { conversations = items });
    }

    [HttpPost]
    public async Task<IActionResult> Create()
    {
        var item = await conversations.CreateAsync(User, HttpContext.RequestAborted);
        return StatusCode(201, new { item.Id, item.Title, item.CreatedAt, item.UpdatedAt });
    }

    [HttpGet("{id}/messages")]
    public async Task<IActionResult> Messages(string id)
    {
        var owned = await db.AssistantConversations.AsNoTracking()
            .AnyAsync(x => x.Id == id && x.UserId == User.GetUserId());
        if (!owned) return NotFound(new { error = "Assistant conversation not found." });
        var messages = await db.AssistantMessages.AsNoTracking().Where(x => x.ConversationId == id)
            .OrderBy(x => x.CreatedAt).Select(x => new { x.Id, x.Role, x.Content, x.Route,
                x.InteractionId, x.CreatedAt }).ToListAsync();
        return Ok(new { messages });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var item = await db.AssistantConversations.SingleOrDefaultAsync(
            x => x.Id == id && x.UserId == User.GetUserId());
        if (item == null) return NotFound(new { error = "Assistant conversation not found." });
        db.AssistantConversations.Remove(item); await db.SaveChangesAsync();
        return Ok(new { message = "Conversation deleted." });
    }

    [HttpDelete]
    public async Task<IActionResult> Clear()
    {
        var query = db.AssistantConversations.Where(x => x.UserId == User.GetUserId());
        var count = await query.CountAsync();
        if (db.Database.IsRelational()) await query.ExecuteDeleteAsync();
        else { db.AssistantConversations.RemoveRange(await query.ToListAsync()); await db.SaveChangesAsync(); }
        return Ok(new { message = "Conversation history cleared.", deleted = count });
    }
}
