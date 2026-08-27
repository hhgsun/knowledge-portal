namespace KnowledgePortal.Api.Models.Entities;

public class AssistantConversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..21];
    public string UserId { get; set; } = null!;
    public string Title { get; set; } = "Yeni konuşma";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public User User { get; set; } = null!;
    public List<AssistantMessage> Messages { get; set; } = [];
}

public class AssistantMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..21];
    public string ConversationId { get; set; } = null!;
    public string Role { get; set; } = null!;
    public string Content { get; set; } = null!;
    public string? Route { get; set; }
    public string? InteractionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public AssistantConversation Conversation { get; set; } = null!;
}
