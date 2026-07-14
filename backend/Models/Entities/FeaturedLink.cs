namespace KnowledgePortal.Api.Models.Entities;

public class FeaturedLink
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..21];
    public string Label { get; set; } = null!;     // Display label shown in the sidebar
    public string LinkType { get; set; } = null!;  // "content_type" | "tag" | "custom"
    public string Target { get; set; } = null!;    // content_type value, tag slug, or custom URL/path
    public string? Icon { get; set; }              // Lucide icon name e.g. "star", "bookmark"
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
