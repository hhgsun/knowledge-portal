namespace KnowledgePortal.Api.Models.Entities;

public class ArticleTag
{
    public string ArticleId { get; set; } = null!;
    public string TagId { get; set; } = null!;

    public Article Article { get; set; } = null!;
    public Tag Tag { get; set; } = null!;
}
