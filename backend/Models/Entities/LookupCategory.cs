namespace KnowledgePortal.Api.Models.Entities;

/// <summary>
/// Defines one controlled, DB-driven article classification dimension. Values remain in
/// lookup_values so the existing content_type contract stays backwards compatible.
/// </summary>
public class LookupCategory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..21];
    public string Key { get; set; } = null!;
    public string Label { get; set; } = null!;
    public string Cardinality { get; set; } = "single"; // single | multiple
    public bool IsRequired { get; set; }
    public string? DefaultValueId { get; set; }
    public string RagBehavior { get; set; } = "filter"; // none | filter
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public LookupValue? DefaultValue { get; set; }
    public ICollection<LookupValue> Values { get; set; } = [];
}
