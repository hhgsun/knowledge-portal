using System.Text.Json;

namespace KnowledgePortal.Api.Models;

public record SourceImportPreview(
    int SourceIndex,
    string FileName,
    string Title,
    string? Excerpt,
    JsonElement Content,
    bool Parsed,
    bool KeepOriginal,
    string ProcessingMode,
    string? Warning);

public record SourceImportDraft(
    int SourceIndex,
    string Title,
    JsonElement Content,
    string? Excerpt,
    string? ContentType,
    string? Status,
    string[]? Tags,
    bool KeepOriginal = true);

public record SourceImportCommitRequest(SourceImportDraft[] Drafts);

public record SourceImportCommitItem(int SourceIndex, string? ArticleId, string? Slug, string Title, string? Error);
public record SourceImportCommitResult(int Created, int Failed, SourceImportCommitItem[] Items);
