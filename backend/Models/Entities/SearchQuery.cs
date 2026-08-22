namespace KnowledgePortal.Api.Models.Entities;

public class SearchQuery
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..21];
    public string Query { get; set; } = null!;
    public string? UserId { get; set; }
    public int ResultsCount { get; set; }
    public string? ClickedArticleId { get; set; }
    public string SearchType { get; set; } = "fulltext";
    public int? ResponseTimeMs { get; set; }
    public string? RagTraceId { get; set; }
    public string? RagPromptVersion { get; set; }
    public string? RagRetrievalVersion { get; set; }
    public string? RagReranker { get; set; }
    public string? RagIndexProfile { get; set; }
    public string? RagGroundingStatus { get; set; }
    public string? RagAnswerHash { get; set; }
    public string? RagFeedback { get; set; }
    public string? RagFeedbackReason { get; set; }
    public DateTime? RagFeedbackAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
    public Article? ClickedArticle { get; set; }
}
