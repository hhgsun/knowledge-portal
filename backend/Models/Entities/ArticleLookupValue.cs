namespace KnowledgePortal.Api.Models.Entities;

/// <summary>Many-to-many assignment between an article and a controlled lookup value.</summary>
public class ArticleLookupValue
{
    public string ArticleId { get; set; } = null!;
    public string LookupValueId { get; set; } = null!;

    public Article Article { get; set; } = null!;
    public LookupValue LookupValue { get; set; } = null!;
}
