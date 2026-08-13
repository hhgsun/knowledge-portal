using System.Security.Claims;

namespace KnowledgePortal.Api.Auth;

public static class RbacService
{
    private static readonly Dictionary<string, HashSet<string>> RolePermissions = new()
    {
        ["admin"] =
        [
            Permissions.ArticlesCreate, Permissions.ArticlesEditOwn, Permissions.ArticlesEditAny,
            Permissions.ArticlesDeleteOwn, Permissions.ArticlesDeleteAny,
            Permissions.ArticlesPublish, Permissions.ArticlesArchive, Permissions.ArticlesApprove,
            Permissions.TagsManage, Permissions.UsersManage, Permissions.AnalyticsView,
            Permissions.ApiKeysManage, Permissions.ApiKeysManageAny, Permissions.FeaturedLinksManage
        ],
        ["editor"] =
        [
            Permissions.ArticlesCreate, Permissions.ArticlesEditOwn,
            Permissions.ArticlesDeleteOwn, Permissions.ArticlesPublish, Permissions.ArticlesArchive, Permissions.ArticlesApprove,
            Permissions.TagsManage, Permissions.AnalyticsView, Permissions.ApiKeysManage
        ],
        ["viewer"] =
        [
            Permissions.ArticlesCreate, Permissions.ArticlesEditOwn, Permissions.ArticlesDeleteOwn, Permissions.ArticlesPublish,
            Permissions.ApiKeysManage
        ]
    };

    /// <summary>
    /// Permissions an API-key principal can never hold, regardless of the owner's role.
    /// API keys are read/write integrations — destructive deletes stay session-only
    /// ("silme gibi işlemler yapamasın").
    /// </summary>
    private static readonly HashSet<string> ApiKeyDeniedPermissions =
    [
        Permissions.ArticlesDeleteOwn, Permissions.ArticlesDeleteAny
    ];

    public static bool HasPermission(string role, string permission)
    {
        return RolePermissions.TryGetValue(role, out var permissions)
            && permissions.Contains(permission);
    }

    public static bool CanEditArticle(string role, bool isOwner) =>
        HasPermission(role, Permissions.ArticlesEditAny)
        || (isOwner && HasPermission(role, Permissions.ArticlesEditOwn));

    public static bool CanDeleteArticle(string role, bool isOwner) =>
        HasPermission(role, Permissions.ArticlesDeleteAny)
        || (isOwner && HasPermission(role, Permissions.ArticlesDeleteOwn));

    /// <summary>Viewers only see published articles or their own; other roles see everything.</summary>
    public static bool CanViewArticle(string role, string articleStatus, bool isOwner) =>
        role != "viewer" || articleStatus == "published" || isOwner;

    // ─── Principal-aware API (applies the API-key permission cap) ───────
    // API keys carry at most editor authority (admin-owned keys are capped down)
    // and never any delete permission. Session principals are unaffected.

    /// <summary>Effective role: API-key principals owned by admins act as editor.</summary>
    public static string GetEffectiveRole(ClaimsPrincipal user)
    {
        var role = user.GetRole();
        return user.GetSource() == "api-key" && role == "admin" ? "editor" : role;
    }

    public static bool HasPermission(ClaimsPrincipal user, string permission) =>
        !(user.GetSource() == "api-key" && ApiKeyDeniedPermissions.Contains(permission))
        && HasPermission(GetEffectiveRole(user), permission);

    public static bool CanEditArticle(ClaimsPrincipal user, bool isOwner) =>
        CanEditArticle(GetEffectiveRole(user), isOwner);

    public static bool CanDeleteArticle(ClaimsPrincipal user, bool isOwner) =>
        user.GetSource() != "api-key" && CanDeleteArticle(GetEffectiveRole(user), isOwner);

    public static bool CanViewArticle(ClaimsPrincipal user, string articleStatus, bool isOwner) =>
        CanViewArticle(GetEffectiveRole(user), articleStatus, isOwner);
}
