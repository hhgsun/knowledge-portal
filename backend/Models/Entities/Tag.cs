namespace KnowledgePortal.Api.Models.Entities;

public class Tag
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..21];
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;

    public ICollection<ArticleTag> ArticleTags { get; set; } = [];
}
