namespace KnowledgePortal.Api.Models.Entities;

public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..21];
    public string Name { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string? Avatar { get; set; }
    public string Role { get; set; } = "viewer";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Article> Articles { get; set; } = [];
    public ICollection<ApiKey> ApiKeys { get; set; } = [];
}
