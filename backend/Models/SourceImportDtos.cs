namespace KnowledgePortal.Api.Models;

public record SourceImportPreview(
    int SourceIndex,
    string FileName,
    string Title,
    string? Excerpt,
    string ContentMarkdown,
    bool Parsed,
    bool KeepOriginal,
    string ProcessingMode,
    string? Warning,
    string? AnalysisError);

public record SourceImportDraft(
    int SourceIndex,
    string Title,
    string ContentMarkdown,
    string? Excerpt,
    string? ContentType,
    string? Status,
    string[]? Tags,
    bool KeepOriginal = true,
    bool OriginalIncludeInIndex = false,
    int[]? AdditionalAttachmentIndexes = null,
    bool[]? AdditionalAttachmentIncludeInIndex = null);

public record SourceImportCommitRequest(SourceImportDraft[] Drafts);

public record SourceImportCommitItem(int SourceIndex, string FileName, string? ArticleId, string? Slug, string Title, string? Error);
public record SourceImportCommitResult(int Created, int Failed, SourceImportCommitItem[] Items);
