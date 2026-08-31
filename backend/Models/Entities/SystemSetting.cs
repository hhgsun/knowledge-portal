namespace KnowledgePortal.Api.Models.Entities;

public class SystemSetting
{
    public string Key { get; set; } = null!;
    public string Value { get; set; } = null!;
    public string? UpdatedById { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User? UpdatedBy { get; set; }
}
