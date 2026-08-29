using KnowledgePortal.Api.Data;
using KnowledgePortal.Api.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgePortal.Api.Services;

public record ContentGovernanceDto(
    string ApprovalState,
    string? ApprovedAt,
    string? ApprovedBy,
    string ReviewState,
    string? LastReviewedAt,
    string? NextReviewAt,
    int ReviewIntervalDays,
    string ContentTypeLabel,
    int AuthorityWeight,
    string AuthorityLevel,
    int ReliabilityScore,
    string[] Warnings);

/// <summary>
/// Derives decision-support metadata without assuming fixed content-type values.
/// Authority is configured on any assigned dynamic lookup row. The highest active assigned
/// authority is used, with legacy content_type lookup fallback for pre-classification articles.
/// Approval is optional:
/// directly published/imported content remains usable but is explicitly marked not_recorded.
/// </summary>
public sealed class ContentGovernanceService(AppDbContext db)
{
    public async Task<Dictionary<string, ContentGovernanceDto>> BuildAsync(
        IReadOnlyCollection<Article> articles, CancellationToken ct = default)
    {
        if (articles.Count == 0) return [];

        var types = articles.Select(a => a.ContentType).Distinct().ToList();
        var lookup = await db.LookupValues
            .Where(l => l.Category == "content_type" && types.Contains(l.Value))
            .ToDictionaryAsync(l => l.Value, ct);
        var authorityByArticle = await ResolveAuthorityWeightsAsync(db, articles, ct);
        var approverIds = articles.Where(a => a.ApprovedById != null).Select(a => a.ApprovedById!).Distinct().ToList();
        var approvers = await db.Users.Where(u => approverIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        var now = DateTime.UtcNow;
        return articles.ToDictionary(a => a.Id, a => Build(a, lookup.GetValueOrDefault(a.ContentType),
            authorityByArticle.GetValueOrDefault(a.Id, 50), approvers, now));
    }

    public static async Task<Dictionary<string, int>> ResolveAuthorityWeightsAsync(
        AppDbContext db, IReadOnlyCollection<Article> articles, CancellationToken ct = default)
    {
        if (articles.Count == 0) return [];
        var articleIds = articles.Select(article => article.Id).ToList();
        var assignments = await db.ArticleLookupValues.AsNoTracking()
            .Where(assignment => articleIds.Contains(assignment.ArticleId)
                && assignment.LookupValue.IsActive
                && assignment.LookupValue.CategoryDefinition.IsActive)
            .Select(assignment => new
            {
                assignment.ArticleId,
                assignment.LookupValue.AuthorityWeight
            }).ToListAsync(ct);
        var result = assignments.GroupBy(assignment => assignment.ArticleId)
            .ToDictionary(group => group.Key,
                group => Math.Clamp(group.Max(assignment => assignment.AuthorityWeight), 0, 100));

        var missingTypes = articles.Where(article => !result.ContainsKey(article.Id))
            .Select(article => article.ContentType).Distinct().ToList();
        var legacyAuthority = await db.LookupValues.AsNoTracking()
            .Where(value => value.Category == "content_type" && missingTypes.Contains(value.Value))
            .ToDictionaryAsync(value => value.Value, value => value.AuthorityWeight, ct);
        foreach (var article in articles.Where(article => !result.ContainsKey(article.Id)))
            result[article.Id] = Math.Clamp(legacyAuthority.GetValueOrDefault(article.ContentType, 50), 0, 100);
        return result;
    }

    private static ContentGovernanceDto Build(Article article, LookupValue? contentType,
        int resolvedAuthorityWeight,
        IReadOnlyDictionary<string, string> approvers, DateTime now)
    {
        var authorityWeight = Math.Clamp(resolvedAuthorityWeight, 0, 100);
        var approvalRecorded = article.ApprovedAt != null && article.ApprovedById != null;
        var nextReview = article.LastReviewedAt?.AddDays(Math.Max(1, article.ReviewIntervalDays));
        var reviewState = article.LastReviewedAt == null ? "not_recorded"
            : nextReview <= now ? "overdue"
            : nextReview <= now.AddDays(14) ? "due_soon"
            : "current";

        var warnings = new List<string>();
        if (!approvalRecorded) warnings.Add("No approval record exists; this content may have been published directly or imported.");
        if (reviewState == "not_recorded") warnings.Add("No review date is recorded.");
        if (reviewState == "overdue") warnings.Add("The configured review date has passed.");
        if (contentType == null) warnings.Add("The content type has no lookup label metadata.");
        else if (!contentType.IsActive) warnings.Add("The content type is currently inactive.");

        var approvalScore = approvalRecorded ? 100 : 55;
        var freshnessScore = reviewState switch { "current" => 100, "due_soon" => 75, "overdue" => 25, _ => 45 };
        var reliability = (int)Math.Round(authorityWeight * 0.5 + approvalScore * 0.3 + freshnessScore * 0.2);

        return new ContentGovernanceDto(
            approvalRecorded ? "approved" : "not_recorded",
            article.ApprovedAt?.ToString("o"),
            article.ApprovedById != null ? approvers.GetValueOrDefault(article.ApprovedById) : null,
            reviewState,
            article.LastReviewedAt?.ToString("o"),
            nextReview?.ToString("o"),
            article.ReviewIntervalDays,
            contentType?.Label ?? article.ContentType,
            authorityWeight,
            authorityWeight >= 80 ? "high" : authorityWeight >= 50 ? "standard" : "low",
            reliability,
            warnings.ToArray());
    }
}
