namespace KnowledgePortal.Api.Models.Entities;

public class RagEvaluationDataset
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = null!;
    public string Description { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string CasesJson { get; set; } = "[]";
    public string ThresholdsJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<RagEvaluationRun> Runs { get; set; } = [];
}

public class RagEvaluationRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DatasetId { get; set; } = null!;
    public string RequestedById { get; set; } = null!;
    public string Status { get; set; } = "pending";
    public int TotalCases { get; set; }
    public int CompletedCases { get; set; }
    public int AttemptCount { get; set; }
    public string? WorkerId { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public string DatasetVersion { get; set; } = "1.0.0";
    public string CasesSnapshotJson { get; set; } = "[]";
    public string ThresholdsSnapshotJson { get; set; } = "{}";
    public string RuntimeSnapshotJson { get; set; } = "{}";
    public string? MetricsJson { get; set; }
    public string? ResultsJson { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public RagEvaluationDataset Dataset { get; set; } = null!;
    public User RequestedBy { get; set; } = null!;
}
