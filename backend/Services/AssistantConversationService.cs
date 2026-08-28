using System.Security.Claims;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public sealed record AssistantConversationContext(string? ConversationId, string EffectiveMessage);

public sealed class AssistantConversationService(AppDbContext db, IConfiguration config)
{
    public async Task<(AssistantConversationContext? Context, ServiceError? Error)> ResolveAsync(
        AssistantRequest request, ClaimsPrincipal principal, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ConversationId))
            return (new(null, request.Message), null);
        if (!config.GetValue("Assistant:ConversationHistoryEnabled", true))
            return (null, new(400, "Assistant conversation history is disabled."));
        if (principal.GetSource() == "api-key")
            return (null, new(403, "Conversation history requires session authentication."));
        var userId = principal.GetUserId();
        var conversation = await db.AssistantConversations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.ConversationId && x.UserId == userId, ct);
        if (conversation == null) return (null, new(404, "Assistant conversation not found."));

        var recent = await db.AssistantMessages.AsNoTracking()
            .Where(x => x.ConversationId == conversation.Id && x.Role == "user")
            .OrderByDescending(x => x.CreatedAt).Take(3).OrderBy(x => x.CreatedAt)
            .Select(x => x.Content).ToListAsync(ct);
        if (recent.Count == 0 || !LooksLikeFollowUp(request.Message))
            return (new(conversation.Id, request.Message), null);
        var context = string.Join("\n", recent.TakeLast(2).Select((text, i) =>
            $"Önceki kullanıcı mesajı {i + 1}: {text[..Math.Min(text.Length, 900)]}"));
        var max = Math.Clamp(config.GetValue("Assistant:MaxMessageCharacters", 4000), 100, 20_000);
        var prefixBudget = Math.Max(0, max - request.Message.Length - 16);
        var boundedContext = context[..Math.Min(context.Length, prefixBudget)];
        var effective = string.IsNullOrEmpty(boundedContext) ? request.Message
            : $"{boundedContext}\nTakip sorusu: {request.Message}";
        return (new(conversation.Id, effective), null);
    }

    public async Task<AssistantConversation> CreateAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        await PruneExpiredAsync(principal.GetUserId(), ct);
        var conversation = new AssistantConversation { UserId = principal.GetUserId() };
        db.AssistantConversations.Add(conversation); await db.SaveChangesAsync(ct);
        return conversation;
    }

    public async Task AppendAsync(string conversationId, string userText, AssistantResponseDto response,
        string? interactionId, ClaimsPrincipal principal, CancellationToken ct)
    {
        var conversation = await db.AssistantConversations
            .SingleOrDefaultAsync(x => x.Id == conversationId && x.UserId == principal.GetUserId(), ct);
        if (conversation == null) return;
        if (conversation.Title == "Yeni konuşma")
            conversation.Title = userText.Trim()[..Math.Min(userText.Trim().Length, 120)];
        conversation.UpdatedAt = DateTime.UtcNow;
        db.AssistantMessages.AddRange(
            new AssistantMessage { ConversationId = conversationId, Role = "user", Content = userText.Trim() },
            new AssistantMessage { ConversationId = conversationId, Role = "assistant",
                Content = response.Answer ?? "", Route = "knowledge_answer",
                InteractionId = interactionId });
        await db.SaveChangesAsync(ct);
    }

    public async Task PruneExpiredAsync(string userId, CancellationToken ct)
    {
        var days = Math.Clamp(config.GetValue("Assistant:ConversationRetentionDays", 90), 1, 3650);
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var expired = db.AssistantConversations.Where(x => x.UserId == userId && x.UpdatedAt < cutoff);
        if (db.Database.IsRelational()) await expired.ExecuteDeleteAsync(ct);
        else { db.AssistantConversations.RemoveRange(await expired.ToListAsync(ct)); await db.SaveChangesAsync(ct); }
    }

    private static bool LooksLikeFollowUp(string message)
    {
        var text = Helpers.SlugHelper.Transliterate(message.Trim()).ToLowerInvariant();
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string[] references = ["peki", "bunun", "bunu", "bunda", "bu ", "ona", "onun", "ayrica",
            "what about", "that", "this", "it ", "and ", "also"];
        return words.Length <= 8 || references.Any(text.Contains);
    }
}
