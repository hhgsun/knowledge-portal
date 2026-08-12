namespace KnowledgePortal.Api.Models;

public record BulkImportItem(
    string? ExternalId,
    string Title,
    string? Excerpt,
    string? Status,
    string? ContentType,
    string? ContentMarkdown,
    string[]? Tags);

public record BulkImportError(int Row, string? Title, string Error);

public record BulkImportResult(
    bool DryRun,
    int Total,
    int Created,
    int Updated,
    int Skipped,
    int Failed,
    IReadOnlyList<BulkImportError> Errors);
