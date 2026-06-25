namespace KnowledgePortal.Api.Models.Entities;

public class LookupValue
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..21];
    public string Category { get; set; } = null!; // "content_type"
    public string Value { get; set; } = null!;    // e.g. "reference", "how-to"
    public string Label { get; set; } = null!;    // Display label e.g. "How-To Guide"
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
