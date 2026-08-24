using System.Security.Claims;
using KnowledgePortal.Api.Auth;
using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Helpers;
using KnowledgePortal.Api.Models;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public sealed record CreateArticleCommand(
    string? Title,
    string? ContentMarkdown,
    string? Excerpt,
    string? Status,
    string? ContentType,
    string[]? Tags,
    int? ReviewIntervalDays = null,
    string? ExternalId = null);

/// <summary>
/// Owns article write invariants shared by REST create/update, bulk import and source import.
/// Transport layers remain responsible only for authorization routing and response shaping.
/// </summary>
public sealed class ArticleMutationService(
    AppDbContext db,
    ArticleService articles,
    ContentTypeService contentTypes)
{
    private static readonly HashSet<string> ValidStatuses = ["draft", "published", "archived"];

    public async Task<ServiceError?> ValidateAsync(
        CreateArticleCommand command,
        ClaimsPrincipal user,
        CancellationToken ct = default)
        => (await ValidateAsync(command.Title, command.Status, command.ContentType,
            command.ReviewIntervalDays, user, requireArchivePermission: true, ct)).Error;

    public async Task<(Article? Article, ServiceError? Error)> CreateAsync(
        CreateArticleCommand command,
        ClaimsPrincipal user,
        string changeSummary,
        bool queueReindex = true,
        CancellationToken ct = default)
    {
        var validation = await ValidateAsync(command.Title, command.Status, command.ContentType,
            command.ReviewIntervalDays, user, requireArchivePermission: true, ct);
        if (validation.Error != null) return (null, validation.Error);

        var article = new Article
        {
            Title = validation.Title!,
            Slug = await db.GenerateUniqueArticleSlugAsync(validation.Title!),
            Content = command.ContentMarkdown?.Trim(),
            Excerpt = command.Excerpt?.Trim(),
            Status = validation.Status!,
            OwnerId = user.GetUserId(),
            ContentType = validation.ContentType!,
            CreatedViaApiKeyId = user.GetApiKeyId(),
            ExternalId = string.IsNullOrWhiteSpace(command.ExternalId) ? null : command.ExternalId.Trim(),
            PublishedAt = validation.Status == "published" ? DateTime.UtcNow : null,
            LastReviewedAt = null,
            ReadTimeMinutes = ContentExtractor.CalculateReadTime(command.ContentMarkdown),
            ReviewIntervalDays = validation.ReviewIntervalDays
        };

        db.Articles.Add(article);
        await articles.AddVersionAsync(article.Id, article.Title, article.Content, user.GetUserId(), changeSummary);
        if (command.Tags is { Length: > 0 })
            await articles.AttachTagsAsync(article.Id, command.Tags, CanCreateTags(user));

        await db.SaveChangesAsync(ct);
        if (queueReindex) await articles.QueueReindexAsync(article, ct);
        return (article, null);
    }

    public async Task<ServiceError?> UpdateAsync(
        Article article,
        UpdateArticleRequest request,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        if (!RbacService.CanEditArticle(user, article.OwnerId == user.GetUserId()))
            return new ServiceError(403, "You do not have permission to edit this article");

        var validation = await ValidateAsync(
            request.Title ?? article.Title,
            request.Status ?? article.Status,
            request.ContentType ?? article.ContentType,
            request.ReviewIntervalDays ?? article.ReviewIntervalDays,
            user,
            requireArchivePermission: request.Status?.Equals("archived", StringComparison.OrdinalIgnoreCase) == true
                && article.Status != "archived",
            ct);
        if (validation.Error != null) return validation.Error;

        var originalStatus = article.Status;
        var contentChanged = request.ContentMarkdown != null
            && request.ContentMarkdown.Trim() != article.Content;
        var approvalInvalidated = false;

        if (request.Title != null)
        {
            approvalInvalidated |= validation.Title != article.Title;
            article.Title = validation.Title!;
            article.Slug = await db.GenerateUniqueArticleSlugAsync(article.Title, article.Id);
        }
        if (request.ContentMarkdown != null)
        {
            article.Content = request.ContentMarkdown.Trim();
            article.ReadTimeMinutes = ContentExtractor.CalculateReadTime(article.Content);
            approvalInvalidated |= contentChanged;
        }
        if (request.Excerpt != null)
        {
            var excerpt = request.Excerpt.Trim();
            approvalInvalidated |= excerpt != article.Excerpt;
            article.Excerpt = excerpt;
        }
        if (request.ContentType != null)
        {
            approvalInvalidated |= validation.ContentType != article.ContentType;
            article.ContentType = validation.ContentType!;
        }
        if (request.ReviewIntervalDays.HasValue)
            article.ReviewIntervalDays = validation.ReviewIntervalDays;

        if (request.Status != null)
        {
            approvalInvalidated |= validation.Status != article.Status;
            await ApplyStatusAsync(article, validation.Status!, originalStatus, ct);
        }
        if (approvalInvalidated) ArticleService.InvalidateApproval(article);

        article.UpdatedAt = DateTime.UtcNow;
        if (contentChanged && article.Status == "published") article.IndexedAt = null;
        if (contentChanged)
            await articles.AddVersionAsync(article.Id, article.Title, article.Content, user.GetUserId(), request.ChangeSummary?.Trim());

        if (request.Tags != null)
        {
            ArticleService.InvalidateApproval(article);
            await ReplaceTagsAsync(article.Id, request.Tags, user, ct);
        }

        await db.SaveChangesAsync(ct);
        await articles.QueueReindexAsync(article, ct);
        return null;
    }

    public async Task<ServiceError?> ReplaceFromImportAsync(
        Article article,
        CreateArticleCommand command,
        ClaimsPrincipal user,
        string changeSummary,
        CancellationToken ct = default)
    {
        if (!RbacService.CanEditArticle(user, article.OwnerId == user.GetUserId()))
            return new ServiceError(403, "You do not have permission to update the matching article");

        var validation = await ValidateAsync(command.Title, command.Status, command.ContentType,
            command.ReviewIntervalDays ?? article.ReviewIntervalDays, user,
            requireArchivePermission: !string.Equals(article.Status, command.Status, StringComparison.OrdinalIgnoreCase)
                && string.Equals(command.Status, "archived", StringComparison.OrdinalIgnoreCase),
            ct);
        if (validation.Error != null) return validation.Error;

        var content = command.ContentMarkdown?.Trim();
        var contentChanged = article.Content != content;
        var originalStatus = article.Status;
        var trustChanged = article.Title != validation.Title
            || article.Excerpt != command.Excerpt?.Trim()
            || article.ContentType != validation.ContentType
            || originalStatus != validation.Status
            || contentChanged
            || command.Tags != null;

        article.Title = validation.Title!;
        article.Slug = await db.GenerateUniqueArticleSlugAsync(article.Title, article.Id);
        article.Excerpt = command.Excerpt?.Trim();
        article.ContentType = validation.ContentType!;
        article.Content = content;
        article.ExternalId ??= string.IsNullOrWhiteSpace(command.ExternalId) ? null : command.ExternalId.Trim();
        article.ReviewIntervalDays = validation.ReviewIntervalDays;
        article.UpdatedAt = DateTime.UtcNow;
        article.ReadTimeMinutes = ContentExtractor.CalculateReadTime(content);
        await ApplyStatusAsync(article, validation.Status!, originalStatus, ct);
        if (trustChanged) ArticleService.InvalidateApproval(article);

        if (contentChanged)
            await articles.AddVersionAsync(article.Id, article.Title, content, user.GetUserId(), changeSummary);
        await ReplaceTagsAsync(article.Id, command.Tags ?? [], user, ct);

        await db.SaveChangesAsync(ct);
        await articles.QueueReindexAsync(article, ct);
        return null;
    }

    private async Task<(string? Title, string? Status, string? ContentType, int ReviewIntervalDays, ServiceError? Error)>
        ValidateAsync(string? title, string? status, string? contentType, int? reviewIntervalDays,
            ClaimsPrincipal user, bool requireArchivePermission, CancellationToken ct)
    {
        var normalizedTitle = title?.Trim() ?? "";
        if (normalizedTitle.Length is < 1 or > 300)
            return (null, null, null, 0, new ServiceError(400, "Title is required (1-300 chars)"));

        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? "draft" : status.Trim().ToLowerInvariant();
        if (!ValidStatuses.Contains(normalizedStatus))
            return (null, null, null, 0,
                new ServiceError(400, $"Invalid status. Allowed: {string.Join(", ", ValidStatuses)}"));
        if (normalizedStatus == "published" && !RbacService.HasPermission(user, Permissions.ArticlesPublish))
            return (null, null, null, 0, new ServiceError(403, "You do not have permission to publish articles"));
        if (requireArchivePermission && normalizedStatus == "archived"
            && !RbacService.HasPermission(user, Permissions.ArticlesArchive))
            return (null, null, null, 0, new ServiceError(403, "You do not have permission to archive articles"));

        var resolvedType = await contentTypes.ResolveAsync(contentType, ct);
        if (resolvedType.Error != null)
            return (null, null, null, 0, resolvedType.Error);

        var interval = reviewIntervalDays ?? 90;
        if (interval is < 1 or > 3650)
            return (null, null, null, 0,
                new ServiceError(400, "reviewIntervalDays must be between 1 and 3650"));

        return (normalizedTitle, normalizedStatus, resolvedType.Value, interval, null);
    }

    private async Task ApplyStatusAsync(Article article, string status, string originalStatus, CancellationToken ct)
    {
        if (status == "published" && originalStatus != "published")
        {
            article.PublishedAt = DateTime.UtcNow;
            article.IndexedAt = null;
        }
        if (status != "published" && originalStatus == "published")
        {
            article.IndexedAt = null;
            var embeddings = await db.ArticleEmbeddings.Where(embedding => embedding.ArticleId == article.Id).ToListAsync(ct);
            if (embeddings.Count > 0) db.ArticleEmbeddings.RemoveRange(embeddings);
        }
        article.Status = status;
    }

    private async Task ReplaceTagsAsync(string articleId, string[] tags, ClaimsPrincipal user, CancellationToken ct)
    {
        var existing = await db.ArticleTags.Where(tag => tag.ArticleId == articleId).ToListAsync(ct);
        db.ArticleTags.RemoveRange(existing);
        if (tags.Length > 0)
            await articles.AttachTagsAsync(articleId, tags, CanCreateTags(user));
    }

    private static bool CanCreateTags(ClaimsPrincipal user) =>
        user.GetSource() == "api-key" || RbacService.HasPermission(user, Permissions.TagsManage);
}
